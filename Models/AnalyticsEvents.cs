namespace Morita.LP.Razor.Models;

public sealed record AnalyticsEvent(string Event, string? TransactionId, object Ecommerce);

public static class AnalyticsEvents
{
    public const string DefaultCurrency = "BRL";

    public static AnalyticsEvent ViewItem(Product product) => new("view_item", null, new
    {
        currency = product.Currency ?? DefaultCurrency,
        value = product.Price ?? 0,
        items = new[] { Item(product, 0) }
    });

    public static AnalyticsEvent ViewItemList(string listName, IReadOnlyList<Product> products) => new("view_item_list", null, new
    {
        item_list_name = listName,
        items = products.Select((product, index) => Item(product, index, listName)).ToList()
    });

    public static AnalyticsEvent ViewCart(CatalogQuoteResult quote) => CartEvent("view_cart", quote);

    public static AnalyticsEvent BeginCheckout(CatalogQuoteResult quote) => CartEvent("begin_checkout", quote);

    public static AnalyticsEvent Purchase(PublicOrder order) => new("purchase", order.PublicOrderNumber, new
    {
        transaction_id = order.PublicOrderNumber,
        currency = string.IsNullOrWhiteSpace(order.Currency) ? DefaultCurrency : order.Currency,
        value = order.Amount,
        shipping = order.Shipping?.Price ?? 0,
        items = order.Lines.Select((line, index) => new
        {
            item_name = ProductName(line.Presentation) ?? line.Description,
            item_variant = PresentationVariant(line.Presentation),
            price = line.UnitPrice,
            quantity = line.Quantity,
            index
        }).ToList()
    });

    private static AnalyticsEvent CartEvent(string name, CatalogQuoteResult quote) => new(name, null, new
    {
        currency = quote.Currency ?? DefaultCurrency,
        value = quote.Total ?? 0,
        items = quote.Lines
            .Where(line => string.Equals(line.Availability, "available", StringComparison.OrdinalIgnoreCase))
            .Select((line, index) => new
            {
                item_id = line.Slug,
                item_name = ProductName(line.Presentation),
                item_variant = Variant(line.ColorLabel, line.SizeLabel),
                price = line.UnitPrice ?? 0,
                quantity = line.Quantity,
                index
            })
            .ToList()
    });

    private static object Item(Product product, int index, string? listName = null) => new
    {
        item_id = product.Slug,
        item_name = product.Nome,
        item_brand = product.Brand?.Label,
        item_category = product.Category?.Label,
        item_category2 = product.Modality?.Label,
        item_list_name = listName,
        price = product.Price ?? 0,
        index
    };

    private static string? ProductName(string? presentation) =>
        presentation?.Split(PresentationSeparator, 2)[0].Trim() is { Length: > 0 } name ? name : null;

    private static string? PresentationVariant(string? presentation) =>
        presentation?.Split(PresentationSeparator) is { Length: > 1 } parts
            ? string.Join(" / ", parts.Skip(1).Select(part => part.Trim()).Where(part => part.Length > 0))
            : null;

    private const string PresentationSeparator = " · ";

    private static string? Variant(string? color, string? size) =>
        string.Join(" / ", new[] { color, size }.Where(part => !string.IsNullOrWhiteSpace(part))) is { Length: > 0 } variant ? variant : null;
}
