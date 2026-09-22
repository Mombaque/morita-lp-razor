using System;
using System.Collections.Generic;
using System.Text.Json;
using Morita.LP.Razor.Models;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class CheckoutPaymentMethodTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Normalize_keeps_pix_and_card_and_drops_unknown_values()
    {
        Assert.Equal([OnlinePaymentMethod.Pix, OnlinePaymentMethod.Card], StorefrontOnlinePayments.Normalize(["PIX", "card", "wire"]));
        Assert.Empty(StorefrontOnlinePayments.Normalize((IEnumerable<string>?)null));
        Assert.Empty(StorefrontOnlinePayments.Normalize(Array.Empty<string>()));
        Assert.Equal([OnlinePaymentMethod.Pix], StorefrontOnlinePayments.Normalize([OnlinePaymentMethod.Pix, OnlinePaymentMethod.Pix]));
    }

    [Fact]
    public void Default_method_prefers_pix_so_existing_copy_stays_valid()
    {
        Assert.Equal(OnlinePaymentMethod.Pix, StorefrontOnlinePayments.DefaultMethod([OnlinePaymentMethod.Pix, OnlinePaymentMethod.Card]));
        Assert.Equal(OnlinePaymentMethod.Card, StorefrontOnlinePayments.DefaultMethod([OnlinePaymentMethod.Card]));
        Assert.Null(StorefrontOnlinePayments.DefaultMethod([]));
        Assert.Equal("Continuar para o PIX", StorefrontOnlinePayments.ContinueLabel(OnlinePaymentMethod.Pix));
        Assert.Equal("Continuar para o PIX", StorefrontOnlinePayments.ContinueLabel(null));
        Assert.Equal("Continuar para o cartão", StorefrontOnlinePayments.ContinueLabel(OnlinePaymentMethod.Card));
        Assert.Equal("pix", OnlinePaymentMethod.Pix.ToWireValue());
        Assert.Equal("card", OnlinePaymentMethod.Card.ToWireValue());
    }

    [Fact]
    public void Json_round_trips_lowercase_wire_values_and_rejects_numeric_tokens()
    {
        Assert.Equal("\"pix\"", JsonSerializer.Serialize(OnlinePaymentMethod.Pix, JsonOptions));
        Assert.Equal("\"card\"", JsonSerializer.Serialize(OnlinePaymentMethod.Card, JsonOptions));
        Assert.Equal(OnlinePaymentMethod.Pix, JsonSerializer.Deserialize<OnlinePaymentMethod>("\"PIX\"", JsonOptions));
        Assert.Equal(OnlinePaymentMethod.Card, JsonSerializer.Deserialize<OnlinePaymentMethod>("\"card\"", JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OnlinePaymentMethod>("\"wire\"", JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OnlinePaymentMethod>("0", JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OnlinePaymentMethod>("\"0\"", JsonOptions));
    }

    [Theory]
    [InlineData("http://127.0.0.1/v1/testing/online-payments/hosted/ref", true)]
    [InlineData("https://pay.example/checkout/ref", true)]
    [InlineData("http://example.com/checkout", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData(null, false)]
    public void Hosted_checkout_urls_allow_https_or_loopback_http(string? url, bool expected)
    {
        Assert.Equal(expected, StorefrontHostedCheckoutUrl.IsAllowed(url));
    }
}
