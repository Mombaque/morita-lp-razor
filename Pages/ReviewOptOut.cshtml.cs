using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class ReviewOptOutModel(IReviewClient client) : PageModel
{
    private const string InvalidLink = "Este link está incompleto ou é inválido. Abra o link do e-mail novamente.";

    [BindProperty(SupportsGet = true)] public string? Token { get; set; }
    [BindProperty(SupportsGet = true, Name = "pronto")] public bool Sent { get; set; }
    public bool Done { get; private set; }
    public string? Error { get; private set; }
    public bool HasToken => ReviewClient.IsValidOptOutToken(Token?.Trim());

    public void OnGet()
    {
        if (Sent)
            Done = true;
        else if (!HasToken)
            Error = InvalidLink;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!HasToken)
        {
            Error = InvalidLink;
            return Page();
        }

        switch (await client.OptOutAsync(Token!.Trim(), cancellationToken))
        {
            case ReviewOptOutState.Done:
                return RedirectToPage(new { pronto = "true" });
            case ReviewOptOutState.Invalid:
                Error = InvalidLink;
                break;
            default:
                Error = "Não foi possível concluir agora. Tente novamente em instantes.";
                break;
        }
        return Page();
    }
}
