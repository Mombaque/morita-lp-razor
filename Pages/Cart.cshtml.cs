using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class CartModel(ICartCookieStore cart, ICatalogClient client) : PageModel
{
    public CartState State { get; private set; } = new(DateTimeOffset.UtcNow, []);
    public CatalogQuoteResult Quote { get; private set; } = CatalogQuoteResult.Unavailable();
    public bool IsEmpty => State.Lines.Count == 0;
    public string? MutationMessage => TempData["CartMessage"] as string;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        State = cart.Read();
        if (!IsEmpty)
            Quote = await client.QuoteAsync(new CatalogQuoteRequest(State.Lines.Select(x => new CatalogQuoteItem(x.PublicOfferId, x.Quantity)).ToList()), cancellationToken);
    }

    public IActionResult OnPostUpdate(Guid publicOfferId, int quantity)
    {
        if (!cart.Update(publicOfferId, quantity))
            TempData["CartMessage"] = "Não foi possível atualizar este item. Use uma quantidade entre 1 e 10 unidades.";
        return RedirectToPage();
    }

    public IActionResult OnPostRemove(Guid publicOfferId)
    {
        if (!cart.Remove(publicOfferId))
            TempData["CartMessage"] = "Este item já não está no carrinho.";
        return RedirectToPage();
    }

    public IActionResult OnPostClear() { cart.Clear(); return RedirectToPage(); }
}
