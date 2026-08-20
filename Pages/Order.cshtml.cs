using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class OrderModel(IOrderClient client, IOrderAccessCookieStore access) : PageModel
{
    public PublicOrder? Order { get; private set; }
    public string? Message { get; private set; }
    [BindProperty(SupportsGet = true)] public string PublicOrderNumber { get; set; } = "";
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        if (!OrderAccessCookieStore.IsValidOrderNumber(PublicOrderNumber) || access.Read(PublicOrderNumber) is not { } credential) { access.Clear(); Message = "Este pedido não está disponível neste dispositivo."; return; }
        var result = await client.GetAsync(PublicOrderNumber, credential.Token, cancellationToken);
        Order = result.Order; Message = result.Message ?? (Order is null ? "Este pedido não está disponível neste dispositivo." : null);
        if (Order is null) access.Clear();
    }
}
