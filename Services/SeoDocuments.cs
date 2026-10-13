using System.Text;
using System.Xml;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public sealed class SeoDocuments(ICatalogClient catalog, IConfiguration configuration)
{
    public const int MaximumCatalogPages = 50;
    private static readonly string[] StaticPaths = ["/", "/jiu-jitsu", "/muay-thai", "/kids", "/products"];
    private static readonly string[] PrivatePaths = ["/cart", "/checkout", "/conta", "/order/", "/assistant/", "/analytics/", "/customer-product-request", "/descadastrar", "/*?handler="];

    public string BaseUrl => (configuration["CanonicalBaseUrl"] ?? "https://moritafight.com.br/").TrimEnd('/');

    public string Robots()
    {
        var builder = new StringBuilder();
        builder.Append("User-agent: *\n");
        builder.Append("Allow: /\n");
        foreach (var path in PrivatePaths)
            builder.Append("Disallow: ").Append(path).Append('\n');
        builder.Append("\nSitemap: ").Append(BaseUrl).Append("/sitemap.xml\n");
        return builder.ToString();
    }

    public async Task<string?> SitemapAsync(CancellationToken cancellationToken)
    {
        var slugs = new List<string>();
        for (var page = 1; page <= MaximumCatalogPages; page++)
        {
            var result = await catalog.GetCatalogAsync(new CatalogQuery(null, null, null, null, null, null, null, page), cancellationToken);
            if (result.State == CatalogLoadState.Unavailable)
                return null;
            slugs.AddRange(result.Items.Select(product => product.Slug).OfType<string>().Where(slug => slug.Length > 0));
            if (page >= result.TotalPages)
                break;
        }

        var urls = StaticPaths.Select(path => path == "/" ? $"{BaseUrl}/" : BaseUrl + path)
            .Concat(slugs.Distinct(StringComparer.Ordinal).Select(slug => $"{BaseUrl}/products/{Uri.EscapeDataString(slug)}"));
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, Async = true };
        await using var output = new StringWriterWithEncoding(Encoding.UTF8);
        await using (var writer = XmlWriter.Create(output, settings))
        {
            await writer.WriteStartDocumentAsync();
            await writer.WriteStartElementAsync(null, "urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
            foreach (var url in urls)
            {
                await writer.WriteStartElementAsync(null, "url", null);
                await writer.WriteElementStringAsync(null, "loc", null, url);
                await writer.WriteEndElementAsync();
            }
            await writer.WriteEndElementAsync();
            await writer.WriteEndDocumentAsync();
        }
        return output.ToString();
    }

    private sealed class StringWriterWithEncoding(Encoding encoding) : StringWriter
    {
        public override Encoding Encoding => encoding;
    }
}
