using Morita.LP.Razor.Models;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class CheckoutPaymentMethodTests
{
    [Fact]
    public void Normalize_keeps_pix_and_card_and_drops_unknown_values()
    {
        Assert.Equal(["pix", "card"], StorefrontOnlinePayments.Normalize(["PIX", "card", "wire"]));
        Assert.Empty(StorefrontOnlinePayments.Normalize(null));
        Assert.Empty(StorefrontOnlinePayments.Normalize([]));
    }

    [Fact]
    public void Default_method_prefers_pix_so_existing_copy_stays_valid()
    {
        Assert.Equal("pix", StorefrontOnlinePayments.DefaultMethod(["pix", "card"]));
        Assert.Equal("card", StorefrontOnlinePayments.DefaultMethod(["card"]));
        Assert.Equal("", StorefrontOnlinePayments.DefaultMethod([]));
        Assert.Equal("Continuar para o PIX", StorefrontOnlinePayments.ContinueLabel("pix"));
        Assert.Equal("Continuar para o PIX", StorefrontOnlinePayments.ContinueLabel(null));
        Assert.Equal("Continuar para o cartão", StorefrontOnlinePayments.ContinueLabel("card"));
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
