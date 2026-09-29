namespace Morita.LP.Razor.Models;

public class Product
{
    public string Nome { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    /// <summary>Must match a PRODUCT_TYPE value in wwwroot/js/customer-product-request.js.</summary>
    public string RequestProductType { get; set; } = string.Empty;
    public string Alt { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public List<string> Imagens { get; set; } = new();
    /// <summary>Short facts shown as chips on the product card (sizes, brands).</summary>
    public List<string> Highlights { get; set; } = new();
}
