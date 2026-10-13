using System.Globalization;
using System.Text;
using System.Xml;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public sealed class SeoDocuments(ICatalogClient catalog, IConfiguration configuration)
{
    public const int MaximumCatalogPages = 50;
    private const string GoogleNamespace = "http://base.google.com/ns/1.0";
    private const string StoreName = "Morita Fight";
    private static readonly string[] StaticPaths = ["/", "/jiu-jitsu", "/muay-thai", "/kids", "/products"];
    private static readonly string[] PrivatePaths = ["/cart", "/checkout", "/conta", "/order/", "/assistant/", "/analytics/", "/customer-product-request", "/descadastrar", "/avaliar", "/avaliacoes/descadastrar", "/*?handler="];

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
        var products = await CatalogProductsAsync(cancellationToken);
        if (products is null)
            return null;
        var slugs = products.Select(product => product.Slug).OfType<string>().Where(slug => slug.Length > 0);

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

    public async Task<string?> MerchantFeedAsync(CancellationToken cancellationToken)
    {
        var products = await CatalogProductsAsync(cancellationToken);
        if (products is null)
            return null;

        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, Async = true };
        await using var output = new StringWriterWithEncoding(Encoding.UTF8);
        await using (var writer = XmlWriter.Create(output, settings))
        {
            await writer.WriteStartDocumentAsync();
            await writer.WriteStartElementAsync(null, "rss", null);
            await writer.WriteAttributeStringAsync(null, "version", null, "2.0");
            await writer.WriteAttributeStringAsync("xmlns", "g", null, GoogleNamespace);
            await writer.WriteStartElementAsync(null, "channel", null);
            await writer.WriteElementStringAsync(null, "title", null, StoreName);
            await writer.WriteElementStringAsync(null, "link", null, $"{BaseUrl}/");
            await writer.WriteElementStringAsync(null, "description", null, "Produtos da loja online Morita Fight.");
            foreach (var item in products.Where(product => !string.IsNullOrWhiteSpace(product.Slug)).SelectMany(FeedItems))
                await WriteFeedItemAsync(writer, item);
            await writer.WriteEndElementAsync();
            await writer.WriteEndElementAsync();
            await writer.WriteEndDocumentAsync();
        }
        return output.ToString();
    }

    private async Task<List<Product>?> CatalogProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<Product>();
        for (var page = 1; page <= MaximumCatalogPages; page++)
        {
            var result = await catalog.GetCatalogAsync(new CatalogQuery(null, null, null, null, null, null, null, page), cancellationToken);
            if (result.State == CatalogLoadState.Unavailable)
                return null;
            products.AddRange(result.Items);
            if (page >= result.TotalPages)
                break;
        }
        return products.DistinctBy(product => product.Slug ?? product.Nome).ToList();
    }

    private IEnumerable<IReadOnlyList<(string Name, string Value)>> FeedItems(Product product)
    {
        var link = $"{BaseUrl}/products/{Uri.EscapeDataString(product.Slug!)}";
        foreach (var variant in product.Variants)
        {
            var images = (variant.Images.Count > 0 ? variant.Images : product.Imagens).Select(AbsoluteUrl).ToList();
            foreach (var offer in variant.Offers)
            {
                var price = offer.UnitPrice ?? product.Price;
                if (images.Count == 0 || price is not > 0)
                    continue;
                yield return FeedItem(product, variant, offer, link, images, price.Value);
            }
        }
    }

    private static List<(string Name, string Value)> FeedItem(Product product, ProductVariant variant, ProductOffer offer, string link, List<string> images, decimal price)
    {
        var color = offer.ColorLabel ?? variant.ColorLabel;
        var title = string.Join(" - ", new[] { product.Nome, color, offer.SizeLabel }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var item = new List<(string Name, string Value)>
        {
            ("id", offer.PublicOfferId.ToString()),
            ("item_group_id", product.Slug!),
            ("title", Truncate(title, 150)),
            ("description", Truncate(string.IsNullOrWhiteSpace(product.Descricao) ? product.Nome : product.Descricao, 5000)),
            ("link", $"{link}?publicOfferId={offer.PublicOfferId}"),
            ("image_link", images[0]),
            ("availability", string.Equals(offer.Availability, "available", StringComparison.OrdinalIgnoreCase) ? "in_stock" : "out_of_stock"),
            ("price", $"{price.ToString("0.00", CultureInfo.InvariantCulture)} {offer.Currency ?? product.Currency ?? "BRL"}"),
            ("condition", "new"),
            ("brand", string.IsNullOrWhiteSpace(product.Brand?.Label) ? StoreName : product.Brand.Label),
            ("identifier_exists", "no")
        };
        item.AddRange(images.Skip(1).Take(10).Select(image => ("additional_image_link", image)));
        if (!string.IsNullOrWhiteSpace(color))
            item.Add(("color", color));
        if (!string.IsNullOrWhiteSpace(offer.SizeLabel))
            item.Add(("size", offer.SizeLabel));
        if (product.Audience != PublicCatalogAudience.All)
            item.Add(("age_group", product.Audience == PublicCatalogAudience.Kids ? "kids" : "adult"));
        var productType = string.Join(" > ", new[] { product.Modality?.Label, product.Category?.Label }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (productType.Length > 0)
            item.Add(("product_type", productType));
        return item;
    }

    private static async Task WriteFeedItemAsync(XmlWriter writer, IReadOnlyList<(string Name, string Value)> fields)
    {
        await writer.WriteStartElementAsync(null, "item", null);
        foreach (var (name, value) in fields)
            await writer.WriteElementStringAsync("g", name, GoogleNamespace, value);
        await writer.WriteEndElementAsync();
    }

    private string AbsoluteUrl(string path) =>
        Uri.TryCreate(path, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https"
            ? path
            : $"{BaseUrl}/{path.TrimStart('/')}";

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private sealed class StringWriterWithEncoding(Encoding encoding) : StringWriter
    {
        public override Encoding Encoding => encoding;
    }
}
