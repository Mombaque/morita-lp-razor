using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class ReviewModel(IReviewClient client) : PageModel
{
    private const string InvalidLink = "Este link de avaliação é inválido ou expirou. Abra o link do e-mail ou do seu pedido novamente.";
    private const string Unavailable = "Não foi possível carregar agora. Tente novamente em instantes.";

    [BindProperty(SupportsGet = true)] public string? Token { get; set; }
    [BindProperty] public List<ReviewEntry> Entries { get; set; } = [];
    public ReviewForm? Form { get; private set; }
    public bool Done { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
            return Page();
        var open = Form!.Items.Where(item => !item.Reviewed).Select(item => item.Slug).ToHashSet(StringComparer.Ordinal);
        var chosen = Entries
            .Where(entry => entry.Rating is >= 1 and <= 5 && open.Contains(entry.Slug ?? ""))
            .Select(entry => new ReviewInput(entry.Slug!, entry.Rating, Clean(entry.Comment)))
            .ToList();
        if (Entries.Any(entry => (entry.Comment?.Length ?? 0) > ReviewPresentation.MaximumCommentLength))
        {
            Error = $"O comentário deve ter até {ReviewPresentation.MaximumCommentLength} caracteres.";
            return Page();
        }
        if (chosen.Count == 0)
        {
            Error = "Escolha as estrelas de pelo menos um produto.";
            return Page();
        }

        var result = await client.SubmitAsync(Token!.Trim(), chosen, cancellationToken);
        switch (result.State)
        {
            case ReviewSubmitState.Accepted:
                Done = true;
                break;
            case ReviewSubmitState.NotFound:
                Form = null;
                Error = InvalidLink;
                break;
            case ReviewSubmitState.Invalid:
                Error = result.Message ?? "Confira as avaliações e tente novamente.";
                break;
            default:
                Error = Unavailable;
                break;
        }
        return Page();
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        if (!ReviewClient.IsValidToken(Token?.Trim()))
        {
            Error = InvalidLink;
            return false;
        }

        var result = await client.GetFormAsync(Token!.Trim(), cancellationToken);
        Form = result.Form;
        if (result.State == ReviewLoadState.Success)
            return true;
        Error = result.State == ReviewLoadState.NotFound ? InvalidLink : Unavailable;
        return false;
    }

    private static string? Clean(string? comment) => string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

    public sealed class ReviewEntry
    {
        public string? Slug { get; set; }
        public int Rating { get; set; }
        public string? Comment { get; set; }
    }
}
