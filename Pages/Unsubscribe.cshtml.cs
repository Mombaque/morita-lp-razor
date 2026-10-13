using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class UnsubscribeModel(ICustomerAccountClient client) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Token { get; set; }
    public bool Done { get; private set; }
    public string? Error { get; private set; }
    public bool HasToken => !string.IsNullOrWhiteSpace(Token) && Token.Length <= 200;

    public void OnGet()
    {
        if (!HasToken) Error = "Este link de descadastro está incompleto. Abra o link do e-mail novamente.";
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!HasToken)
        {
            Error = "Este link de descadastro está incompleto. Abra o link do e-mail novamente.";
            return Page();
        }

        var result = await client.UnsubscribeMarketingAsync(Token!.Trim(), cancellationToken);
        if (result.Value)
            Done = true;
        else
            Error = result.State == AccountLoadState.Validation
                ? "Este link de descadastro é inválido. Você também pode desativar os e-mails em Minha conta."
                : "Não foi possível concluir agora. Tente novamente em instantes.";
        return Page();
    }
}
