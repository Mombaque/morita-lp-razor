using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Tests;

public sealed class ProductCatalogTests
{
    public static TheoryData<string> CategoryPages => new() { "/JiuJitsu", "/MuayThai" };

    [Fact]
    public void Every_product_maps_to_a_request_wizard_product_type()
    {
        var wizardProductTypes = ReadWizardProductTypes();
        var service = new ProductService();

        foreach (var product in service.GetJiuJitsuProducts().Concat(service.GetMuayThaiProducts()))
        {
            Assert.Contains(product.RequestProductType, wizardProductTypes);
        }
    }

    [Fact]
    public void Product_slugs_are_present_and_unique()
    {
        var service = new ProductService();
        var slugs = service.GetJiuJitsuProducts().Concat(service.GetMuayThaiProducts()).Select(p => p.Slug).ToList();

        Assert.All(slugs, slug => Assert.Matches("^[a-z0-9-]+$", slug));
        Assert.Equal(slugs.Count, slugs.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(CategoryPages))]
    public async Task Category_page_renders_wizard_and_whatsapp_actions_for_each_product(string path)
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        var service = new ProductService();
        var products = path == "/JiuJitsu" ? service.GetJiuJitsuProducts() : service.GetMuayThaiProducts();
        foreach (var product in products)
        {
            Assert.Contains($"id=\"{product.Slug}\"", html);
            Assert.Contains($"data-request-product=\"{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(product.RequestProductType)}\"", html);
        }

        Assert.Contains($"https://wa.me/{WhatsAppLink.Phone}?text=", html);
        Assert.Contains("data-track-event=\"whatsapp_product_click\"", html);
        Assert.Contains("class=\"quick-nav\"", html);
    }

    private static HashSet<string> ReadWizardProductTypes()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "customer-product-request.js"));
        var block = Regex.Match(script, @"const PRODUCT_TYPE = \{(?<body>.*?)\};", RegexOptions.Singleline);
        Assert.True(block.Success, "PRODUCT_TYPE block not found in customer-product-request.js");

        return Regex.Matches(block.Groups["body"].Value, @":\s*'(?<value>[^']+)'")
            .Select(match => match.Groups["value"].Value)
            .ToHashSet();
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Morita.LP.Razor.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
