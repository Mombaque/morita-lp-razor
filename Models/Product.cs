namespace Morita.LP.Razor.Models;

public class Product
{
    public string Nome { get; set; } = string.Empty;
    public string Alt { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public List<string> Imagens { get; set; } = new();
}
