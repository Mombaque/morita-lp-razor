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

    [Fact]
    public void Card_token_accepts_fake_scenarios_and_rejects_a_primary_account_number()
    {
        Assert.True(StorefrontCardPaymentToken.TryNormalize("tok_pending", out var pending));
        Assert.Equal("tok_pending", pending);
        Assert.True(StorefrontCardPaymentToken.TryNormalize(" tok_approve ", out var approved));
        Assert.Equal("tok_approve", approved);
        Assert.False(StorefrontCardPaymentToken.TryNormalize("4242424242424242", out _));
        Assert.True(StorefrontCardPaymentToken.LooksLikePrimaryAccountNumber("4242 4242 4242 4242"));
        Assert.False(StorefrontCardPaymentToken.TryNormalize("short", out _));
    }
}
