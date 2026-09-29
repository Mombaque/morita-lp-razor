using Morita.LP.Razor.Models;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class CurrencyPresentationTests
{
    [Theory]
    [InlineData("BRL", "R$")]
    [InlineData("brl", "R$")]
    [InlineData("USD", "USD")]
    [InlineData(null, "R$")]
    public void Symbol_maps_brazilian_real_to_customer_facing_symbol(string? currency, string expected)
    {
        Assert.Equal(expected, CurrencyPresentation.Symbol(currency));
    }

    [Theory]
    [InlineData("BRL 50.00", "R$ 50.00")]
    [InlineData("brl 12,50", "R$ 12,50")]
    [InlineData("R$ 99,90", "R$ 99,90")]
    public void NormalizeFormattedPrice_replaces_only_currency_code(string formattedPrice, string expected)
    {
        Assert.Equal(expected, CurrencyPresentation.NormalizeFormattedPrice(formattedPrice));
    }

    [Theory]
    [InlineData(12, "BRL", "R$ 12,00")]
    [InlineData(1234.5, "BRL", "R$ 1.234,50")]
    [InlineData(1234567.891, null, "R$ 1.234.567,89")]
    [InlineData(99.9, "USD", "USD 99,90")]
    public void Format_uses_brazilian_separators(decimal amount, string? currency, string expected)
    {
        Assert.Equal(expected, CurrencyPresentation.Format(amount, currency));
    }

    [Fact]
    public void Format_of_missing_amount_is_empty()
    {
        Assert.Equal("", CurrencyPresentation.Format((decimal?)null, "BRL"));
    }
}
