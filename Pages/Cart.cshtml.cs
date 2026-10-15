using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class CartModel(ICartCookieStore cart, ICatalogClient client, ICartMutationService cartMutations, ICheckoutClient? checkout = null, ICartPriceReview? priceReview = null) : PageModel
{
    public decimal? FreeShippingMinimumAmount { get; private set; }
    public CartState State { get; private set; } = new(DateTimeOffset.UtcNow, []);
    public CatalogQuoteResult Quote { get; private set; } = CatalogQuoteResult.Unavailable();
    public IReadOnlyList<CartPriceChange> PriceChanges { get; private set; } = [];
    public bool IsEmpty => State.Lines.Count == 0;
    public string? MutationMessage => TempData["CartMessage"] as string;
    public Guid? MutationOfferId => TempData["CartMessageOfferId"] switch
    {
        Guid offerId => offerId,
        string value when Guid.TryParse(value, out var offerId) => offerId,
        _ => null
    };
    public bool HasLineMutationMessage => MutationOfferId is { } offerId && Quote.Lines.Any(x => x.PublicOfferId == offerId);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        State = cart.Read();
        if (!IsEmpty)
        {
            Quote = await client.QuoteAsync(new CatalogQuoteRequest(State.Lines.Select(x => new CatalogQuoteItem(x.PublicOfferId, x.Quantity)).ToList()), cancellationToken);
            PriceChanges = priceReview?.Acknowledge(State, Quote) ?? [];
            if (checkout is not null && Quote.State == CatalogLoadState.Success)
                FreeShippingMinimumAmount = (await checkout.GetConfigurationAsync(cancellationToken)).Configuration?.FreeShippingMinimumAmount;
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync(Guid publicOfferId, int quantity, CancellationToken cancellationToken)
    {
        var result = await cartMutations.UpdateAsync(publicOfferId, quantity, cancellationToken);
        if (result.Succeeded)
            return RedirectToPage();
        TempData["CartMessage"] = GetMutationMessage(result.Status);
        TempData["CartMessageOfferId"] = publicOfferId;
        return RedirectToPage(null, null, null, CartLineAnchor(publicOfferId));
    }

    public static string CartLineAnchor(Guid publicOfferId) => $"cart-line-{publicOfferId:D}";

    public IActionResult OnPostRemove(Guid publicOfferId)
    {
        if (!cart.Remove(publicOfferId))
            TempData["CartMessage"] = "Este item já não está no carrinho.";
        return RedirectToPage();
    }

    public IActionResult OnPostClear() { cart.Clear(); return RedirectToPage(); }

    private static string GetMutationMessage(CartMutationStatus status) => status switch
    {
        CartMutationStatus.Insufficient => "A quantidade solicitada não está disponível. Reduza a quantidade e tente novamente.",
        CartMutationStatus.Inactive => "Esta oferta está inativa e não pode ser atualizada.",
        CartMutationStatus.Removed => "Esta oferta foi removida e não pode ser atualizada.",
        CartMutationStatus.Unavailable => "Não foi possível confirmar a disponibilidade agora. Seus itens foram preservados; tente novamente.",
        CartMutationStatus.PersistenceFailed => "Não foi possível atualizar este item. Seus itens foram preservados; tente novamente.",
        _ => "Não foi possível atualizar este item. Use uma quantidade entre 1 e 10 unidades."
    };
}
