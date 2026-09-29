using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Tests;

public sealed class LandingSectionsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LandingSectionsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    public static TheoryData<string> LandingPages => new() { "/", "/JiuJitsu", "/MuayThai" };

    [Theory]
    [MemberData(nameof(LandingPages))]
    public async Task Landing_pages_render_footer_faq_and_team_sections(string path)
    {
        var html = await _factory.CreateClient().GetStringAsync(path);

        Assert.Contains("class=\"site-footer\"", html);
        Assert.Contains("data-open-status", html);
        Assert.Contains("href=\"/Privacidade\"", html);
        Assert.Contains("class=\"faq-section\"", html);
        Assert.Contains("data-track-event=\"whatsapp_team_click\"", html);
        Assert.Contains("class=\"nav-cta\"", html);
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/JiuJitsu", true)]
    [InlineData("/MuayThai", false)]
    public async Task Size_calculator_is_rendered_on_kimono_pages(string path, bool expected)
    {
        var html = await _factory.CreateClient().GetStringAsync(path);

        Assert.Equal(expected, html.Contains("data-size-calculator"));
    }

    [Fact]
    public async Task Faq_structured_data_is_valid_json_matching_visible_questions()
    {
        var html = await _factory.CreateClient().GetStringAsync("/");
        var match = Regex.Match(html, "<script type=\"application/ld\\+json\">(?<json>\\{\"@context\":\"https://schema.org\",\"@type\":\"FAQPage\".*?)</script>", RegexOptions.Singleline);
        Assert.True(match.Success, "FAQPage JSON-LD not found");

        using var document = JsonDocument.Parse(match.Groups["json"].Value);
        var questions = document.RootElement.GetProperty("mainEntity").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToList();

        Assert.Equal(FaqContent.Items.Select(item => item.Question), questions);
    }

    [Fact]
    public async Task Store_hours_json_uses_javascript_weekday_keys()
    {
        using var hours = JsonDocument.Parse(StoreInfo.HoursJson);

        Assert.False(hours.RootElement.TryGetProperty("0", out _)); // closed on Sunday
        Assert.Equal(9, hours.RootElement.GetProperty("6")[0].GetInt32());
        Assert.Equal(13, hours.RootElement.GetProperty("6")[1].GetInt32());

        var html = await _factory.CreateClient().GetStringAsync("/");
        Assert.Contains("data-hours=\"{&quot;1&quot;:[9,18]", html);
    }

    [Fact]
    public async Task Privacy_page_is_served_and_describes_collected_data()
    {
        var response = await _factory.CreateClient().GetAsync("/Privacidade");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Política de Privacidade", html);
        Assert.Contains("13.709/2018", html);
        Assert.Contains("WhatsApp", html);
    }

    [Fact]
    public async Task Caneleiras_card_renders_its_photos()
    {
        var html = await _factory.CreateClient().GetStringAsync("/MuayThai");

        Assert.Contains("id=\"caneleiras\"", html);
        Assert.Contains("/images/muay-thai/caneleira-st-azul.webp", html);
        Assert.Contains("/images/muay-thai/caneleira-st-vermelha.webp", html);
        Assert.DoesNotContain("via.placeholder.com", html);
    }

    [Fact]
    public void Every_product_image_exists_on_disk()
    {
        var root = Path.Combine(FindRepoRoot(), "wwwroot");
        var service = new ProductService();
        var images = service.GetJiuJitsuProducts().Concat(service.GetMuayThaiProducts()).SelectMany(p => p.Imagens);

        Assert.All(images, image => Assert.True(File.Exists(Path.Combine(root, image.TrimStart('/'))), $"Missing {image}"));
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
