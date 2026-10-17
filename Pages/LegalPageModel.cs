using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public abstract class LegalPageModel(IStoreInfoClient storeInfo) : PageModel
{
    public const string FallbackAddressLine = "Rua Coronel Nogueira Padilha, 429 - Sorocaba, SP";
    public const string WhatsAppUrl = "https://wa.me/5515981079332";
    public const string WhatsAppDisplay = "(15) 98107-9332";

    public StorefrontStore? Store { get; private set; }

    public string StoreName => Store?.HasLegalIdentity == true ? $"{Store.LegalName} (CNPJ {Store.Cnpj})" : "Morita";

    public string AddressLine => Store?.Address?.Line is { Length: > 0 } line ? line : FallbackAddressLine;

    public string? ContactEmail => Store?.ContactEmail;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Store = await storeInfo.GetAsync(cancellationToken);
    }
}
