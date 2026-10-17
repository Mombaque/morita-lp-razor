using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Morita.LP.Razor.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public sealed class StatusCodeModel : PageModel
{
    public int Code { get; private set; }
    public bool NotFound => Code == StatusCodes.Status404NotFound;

    public IActionResult OnGet(int code)
    {
        if (HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodeReExecuteFeature>() is null) return NotFound();
        Code = code;
        return Page();
    }
}
