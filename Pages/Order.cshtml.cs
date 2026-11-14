using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class OrderModel(IOrderClient client, IOrderAccessCookieStore access, ICustomerAccountCookieStore? accountCookies = null) : PageModel
{
    public const string NotOnThisDevice = "Este pedido não está disponível neste dispositivo.";
    public const string TemporarilyUnavailable = "O pedido não pôde ser consultado agora. Atualize a página em instantes.";
    public PublicOrder? Order { get; private set; }
    public string? Message { get; private set; }
    public bool CanRetry { get; private set; }
    [BindProperty(SupportsGet = true)] public string PublicOrderNumber { get; set; } = "";
    [BindProperty(SupportsGet = true)] public bool Placed { get; set; }
    public bool TrackPurchase => Placed && Order is { PaymentStatus: var status } && status.ToLowerInvariant() is "converted" or "approved";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        if (!OrderAccessCookieStore.IsValidOrderNumber(PublicOrderNumber)) { Message = NotOnThisDevice; return Page(); }
        // Without this device's order link, a signed-in shopper still reaches the order through their account.
        if (access.Read(PublicOrderNumber) is not { } credential) return AccountOrder() ?? NotAvailable();

        var result = await client.GetAsync(PublicOrderNumber, credential.Token, cancellationToken);
        Order = result.Order;
        if (Order is not null) return Page();
        switch (result.State)
        {
            case OrderLoadState.Unauthorized:
                // The API no longer accepts this device's token for the order, so the stored link is useless.
                access.Clear();
                return AccountOrder() ?? NotAvailable();
            case OrderLoadState.Malformed:
                // Keep the link: the account page reads the same order without this page's stricter checks.
                return AccountOrder() ?? Unavailable(result.Message);
            default:
                // Timeouts and outages are temporary; keep the link so a refresh shows the order again.
                return Unavailable(result.Message);
        }
    }

    public async Task<IActionResult> OnGetInvoiceAsync(string kind, CancellationToken cancellationToken)
    {
        if (!OrderAccessCookieStore.IsValidOrderNumber(PublicOrderNumber) || !PublicOrderInvoice.IsKnownFile(kind) || access.Read(PublicOrderNumber) is not { } credential) return NotFound();
        var file = await client.DownloadInvoiceAsync(PublicOrderNumber, credential.Token, kind, cancellationToken);
        return file is null ? NotFound() : File(file.Content, file.ContentType, file.FileName);
    }

    private IActionResult? AccountOrder() => accountCookies?.Read() is null
        ? null
        : RedirectToPage("/Account", null, new { publicOrderNumber = PublicOrderNumber.ToUpperInvariant() }, "pedidos");

    private PageResult NotAvailable() { Message = NotOnThisDevice; return Page(); }
    private PageResult Unavailable(string? message) { Message = message ?? TemporarilyUnavailable; CanRetry = true; return Page(); }
}
