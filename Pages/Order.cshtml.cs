using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class OrderModel(IOrderClient client, IOrderAccessCookieStore access, ICustomerAccountClient account, ICustomerAccountCookieStore accountCookies) : PageModel
{
    public PublicOrder? Order { get; private set; }
    public string? Message { get; private set; }
    public bool CanClaim { get; private set; }
    public bool SignedIn { get; private set; }
    public string? ClaimMessage { get; private set; }
    [BindProperty(SupportsGet = true)] public string PublicOrderNumber { get; set; } = "";
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        if (!OrderAccessCookieStore.IsValidOrderNumber(PublicOrderNumber) || access.Read(PublicOrderNumber) is not { } credential) { access.Clear(); Message = "Este pedido não está disponível neste dispositivo."; return; }
        var result = await client.GetAsync(PublicOrderNumber, credential.Token, cancellationToken);
        Order = result.Order; Message = result.Message ?? (Order is null ? "Este pedido não está disponível neste dispositivo." : null);
        if (Order is null) access.Clear();
        CanClaim = Order is not null && accountCookies.Read() is not null;
        SignedIn = CanClaim;
    }
    public async Task<IActionResult> OnPostClaimAsync(CancellationToken cancellationToken)
    {
        if (!OrderAccessCookieStore.IsValidOrderNumber(PublicOrderNumber) || access.Read(PublicOrderNumber) is not { } credential || accountCookies.Read() is not { } session)
        { ClaimMessage = "Entre na conta e abra este pedido neste dispositivo para reivindicá-lo."; await OnGetAsync(cancellationToken); return Page(); }
        var result = await account.ClaimOrderAsync(session.Token, PublicOrderNumber, credential.Token, cancellationToken);
        if (result.Value) return RedirectToPage("/Account");
        if (result.State == AccountLoadState.Unauthorized) accountCookies.Clear();
        ClaimMessage = result.State == AccountLoadState.Conflict ? "Este pedido já pertence a outra conta." : "Não foi possível vincular este pedido.";
        await OnGetAsync(cancellationToken); return Page();
    }
}
