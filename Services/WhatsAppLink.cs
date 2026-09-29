namespace Morita.LP.Razor.Services;

public static class WhatsAppLink
{
    public const string Phone = "5515981079332";
    public const string CatalogUrl = "https://wa.me/c/" + Phone;

    public static string Chat(string message) =>
        $"https://wa.me/{Phone}?text={Uri.EscapeDataString(message)}";

    public static string ProductInterest(string productName) =>
        Chat($"Olá, Morita! Tenho interesse em {productName}. Pode me ajudar com tamanho e disponibilidade?");
}
