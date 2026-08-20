using Microsoft.AspNetCore.Mvc;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.ViewComponents;

public sealed class CartIndicatorViewComponent(ICartCookieStore cart) : ViewComponent
{
    public IViewComponentResult Invoke() => View(new CartIndicatorModel(cart.Read().Lines.Sum(x => x.Quantity)));
}

public sealed record CartIndicatorModel(int Count);
