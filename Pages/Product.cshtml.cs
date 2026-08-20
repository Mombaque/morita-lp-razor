using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public class ProductModel(ICatalogClient client, IConfiguration configuration) : PageModel
{
    public Product? Product { get; private set; }
    public CatalogResult Related { get; private set; } = CatalogResult.Empty();
    public CatalogLoadState State { get; private set; }
    public Guid? SelectedOfferId { get; private set; }
    public int Quantity { get; private set; } = 1;
    public ProductOffer? SelectedOffer { get; private set; }
    public bool SelectionConfirmed { get; private set; }
    public string? WhatsAppUrl { get; private set; }
    public string ProductJsonLd { get; private set; } = "{}";
    public string BreadcrumbJsonLd { get; private set; } = "{}";
    public string BaseUrl => (configuration["CanonicalBaseUrl"] ?? "https://moritafight.com.br/").TrimEnd('/');

    public async Task<IActionResult> OnGetAsync(string slug, Guid? publicOfferId, int? quantity, CancellationToken cancellationToken)
    {
        var detail = await client.GetProductAsync(slug, cancellationToken);
        State = detail.State;
        if (detail.State == CatalogLoadState.NotFound) return NotFound();
        if (detail.Product is null) return Page();
        Product = detail.Product;
        foreach (var variant in Product.Variants)
            foreach (var offer in variant.Offers)
            {
                offer.ColorId ??= variant.ColorId;
                offer.ColorLabel ??= variant.ColorLabel;
            }
        Quantity = Math.Clamp(quantity ?? 1, 1, 99);
        if (quantity is < 1 or > 99) ModelState.AddModelError("quantity", "A quantidade deve estar entre 1 e 99.");
        SelectedOfferId = publicOfferId;
        SelectedOffer = publicOfferId is null ? null : Product.Variants.SelectMany(v => v.Offers).FirstOrDefault(o => o.PublicOfferId == publicOfferId);
        if (publicOfferId is not null && SelectedOffer is null) ModelState.AddModelError("publicOfferId", "Selecione uma oferta válida.");
        if (SelectedOffer is not null && !string.Equals(SelectedOffer.Availability, "available", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError("publicOfferId", "A oferta selecionada está indisponível.");
        if (publicOfferId is not null && SelectedOffer is not null && ModelState.IsValid)
        {
            var quote = await client.QuoteAsync(new CatalogQuoteRequest([new CatalogQuoteItem(publicOfferId.Value, Quantity)]), cancellationToken);
            if (quote.State == CatalogLoadState.Success)
            {
                SelectionConfirmed = true;
                var publicColor = SelectedOffer.ColorLabel ?? "Cor única";
                var publicSize = SelectedOffer.SizeLabel ?? "Tamanho único";
                var text = $"Olá, gostaria de consultar {Product.Nome} — {publicColor} / {publicSize}, quantidade {Quantity}.";
                WhatsAppUrl = $"https://wa.me/5515981079332?text={Uri.EscapeDataString(text)}";
            }
            else
            {
                ModelState.AddModelError("publicOfferId", "Não foi possível confirmar essa oferta. Tente novamente.");
            }
        }
        var url = $"{BaseUrl}/products/{Uri.EscapeDataString(Product.Slug ?? slug)}";
        var offers = Product.Variants.SelectMany(v => v.Offers).Select(o => new Dictionary<string, object?> { ["@type"] = "Offer", ["price"] = o.UnitPrice ?? Product.Price, ["priceCurrency"] = o.Currency ?? Product.Currency ?? "BRL", ["availability"] = $"https://schema.org/{(o.Availability == "available" ? "InStock" : "OutOfStock")}" }).ToList();
        ProductJsonLd = JsonSerializer.Serialize(new Dictionary<string, object?> { ["@context"] = "https://schema.org", ["@type"] = "Product", ["name"] = Product.Nome, ["description"] = Product.Descricao, ["url"] = url, ["image"] = Product.Imagens, ["offers"] = offers });
        BreadcrumbJsonLd = JsonSerializer.Serialize(new Dictionary<string, object?> { ["@context"] = "https://schema.org", ["@type"] = "BreadcrumbList", ["itemListElement"] = new[] { new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 1, ["name"] = "Produtos", ["item"] = $"{BaseUrl}/products" }, new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 2, ["name"] = Product.Nome, ["item"] = url } } });
        Related = await client.GetRelatedAsync(slug, 4, cancellationToken);
        return Page();
    }
}
