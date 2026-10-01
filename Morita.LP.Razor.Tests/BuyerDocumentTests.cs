using Morita.LP.Razor.Models;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class BuyerDocumentTests
{
    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("12abc34501de35", true)]
    [InlineData("529.982.247-24", false)]
    [InlineData("111.111.111-11", false)]
    [InlineData("123", false)]
    public void IsValid_checks_cpf_and_cnpj_digits(string value, bool expected) => Assert.Equal(expected, BuyerDocument.IsValid(value));

    [Fact]
    public void Format_and_normalize_round_trip()
    {
        Assert.Equal("529.982.247-25", BuyerDocument.Format("52998224725"));
        Assert.Equal("12.ABC.345/01DE-35", BuyerDocument.Format("12abc34501de35"));
        Assert.Equal("52998224725", BuyerDocument.Normalize("529.982.247-25"));
    }

    [Fact]
    public void Attribute_accepts_empty_values_and_rejects_invalid_ones()
    {
        var attribute = new BuyerDocumentAttribute();

        Assert.True(attribute.IsValid(""));
        Assert.False(attribute.IsValid("123"));
    }
}
