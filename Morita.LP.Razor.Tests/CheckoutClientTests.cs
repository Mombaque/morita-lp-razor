using System.Net;
using System.Net.Http;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class CheckoutClientTests
{
    [Fact]
    public async Task Create_sends_both_credentials_and_rejects_bad_totals()
    {
        var offer = Guid.NewGuid();
        using var handler = new RecordingHandler("""{"publicCheckoutId":"11111111-1111-1111-1111-111111111111","status":"active","expiresAt":"2026-08-20T12:30:00Z","accessExpiresAt":"2026-09-19T12:00:00Z","lines":[{"publicOfferId":"00000000-0000-0000-0000-000000000001","quantity":1,"presentation":"Item","unitPrice":10,"lineTotal":9}],"merchandiseTotal":9,"discountTotal":0,"freightTotal":0,"total":9,"currency":"BRL","pickup":{"publicPickupId":"22222222-2222-2222-2222-222222222222","displayName":"Loja","address":{},"hours":"Dia","instructions":""},"contact":{"name":"N","email":"e@e.com","phone":"1"}}""");
        var client = Create(handler);
        var result = await client.CreateAsync(new([new(offer, 1)], new CheckoutContact { Name = "N", Email = "e@e.com", Phone = "1" }, new CheckoutFulfillment("pickup", Guid.NewGuid())), new string('i', 32), new string('a', 32));
        Assert.Equal(CheckoutLoadState.Malformed, result.State);
        Assert.Equal(new string('i', 32), handler.Request!.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal(new string('a', 32), handler.Request.Headers.GetValues("X-Checkout-Access-Token").Single());
        Assert.Equal("203.0.113.9", handler.Request.Headers.GetValues("X-Morita-Client-IP").Single());
        Assert.Equal("proxy-secret", handler.Request.Headers.GetValues("X-Morita-Proxy-Secret").Single());
    }

    [Fact]
    public async Task Checkout_creation_keeps_reservation_error_mapping()
    {
        foreach (var pair in new[] { (HttpStatusCode.UnprocessableEntity, CheckoutLoadState.Validation), (HttpStatusCode.Conflict, CheckoutLoadState.Conflict) })
        {
            using var handler = new RecordingHandler(pair.Item1);
            var result = await Create(handler).CreateAsync(
                new([], new CheckoutContact(), new CheckoutFulfillment("pickup", Guid.NewGuid())),
                new string('i', 32),
                new string('a', 32));
            Assert.Equal(pair.Item2, result.State);
            if (pair.Item1 == HttpStatusCode.UnprocessableEntity) Assert.Equal("Não foi possível reservar os itens com os dados atuais.", result.Message);
            if (pair.Item1 == HttpStatusCode.Conflict) Assert.Equal("A tentativa de checkout mudou. Tente novamente.", result.Message);
        }
    }

    [Fact]
    public async Task Create_maps_a_valid_authoritative_response_and_requires_the_requested_pickup()
    {
        var offer = Guid.NewGuid();
        var pickup = Guid.NewGuid();
        var body = $$$"""{"publicCheckoutId":"11111111-1111-1111-1111-111111111111","status":"active","expiresAt":"2026-08-20T12:30:00Z","accessExpiresAt":"2026-09-19T12:00:00Z","lines":[{"publicOfferId":"{{{offer}}}","quantity":1,"presentation":"Item","unitPrice":10,"lineTotal":10}],"merchandiseTotal":10,"discountTotal":0,"freightTotal":0,"total":10,"currency":"BRL","fulfillmentMethod":"pickup","pickup":{"publicPickupId":"{{{pickup}}}","displayName":"Loja","address":{"street":"Rua A","number":"1","neighborhood":"Centro","city":"Sorocaba","state":"SP","postalCode":"18000-000"},"hours":"09:00-18:00","instructions":"Documento"},"contact":{"name":"Ana","email":"ana@example.com","phone":"+5515999999999"}}""";
        var result = await Create(new RecordingHandler(body)).CreateAsync(
            new([new(offer, 1)], new CheckoutContact { Name = "Ana", Email = "ana@example.com", Phone = "15999999999" }, new CheckoutFulfillment("pickup", pickup)),
            new string('i', 32),
            new string('a', 32));

        Assert.Equal(CheckoutLoadState.Success, result.State);
        Assert.Equal(10, result.Checkout?.Total);

        var wrongPickupResult = await Create(new RecordingHandler(body)).CreateAsync(
            new([new(offer, 1)], new CheckoutContact { Name = "Ana", Email = "ana@example.com", Phone = "15999999999" }, new CheckoutFulfillment("pickup", Guid.NewGuid())),
            new string('i', 32),
            new string('a', 32));
        Assert.Equal(CheckoutLoadState.Malformed, wrongPickupResult.State);
    }

    [Fact]
    public async Task Shipping_quote_sends_opaque_lines_and_maps_authoritative_options()
    {
        var offer = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        using var handler = new RecordingHandler(JsonSerializer.Serialize(new
        {
            expiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            currency = "BRL",
            options = new[] { new { publicShippingQuoteId = quoteId, serviceName = "PAC", carrierName = "Correios", price = 18.5m, minimumDeliveryDays = 4, maximumDeliveryDays = 7 } }
        }));

        var result = await Create(handler).QuoteShippingAsync(new([new(offer, 2)], "01310-100"));

        Assert.Equal(CheckoutLoadState.Success, result.State);
        Assert.Equal(quoteId, Assert.Single(result.Quote!.Options).PublicShippingQuoteId);
        Assert.Equal("https://api.test/v1/storefront/checkout/shipping/quotes", handler.Request!.RequestUri!.ToString());
        Assert.Contains($"\"publicOfferId\":\"{offer}\"", handler.Body);
        Assert.Contains("\"destinationPostalCode\":\"01310-100\"", handler.Body);
    }

    [Fact]
    public async Task Shipping_checkout_submits_quote_and_address_and_maps_freight_snapshot()
    {
        var offer = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        var checkoutJson = JsonSerializer.Serialize(new
        {
            publicCheckoutId = Guid.NewGuid(),
            status = "active",
            expiresAt = DateTimeOffset.UtcNow.AddHours(1),
            accessExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            lines = new[] { new { publicOfferId = offer, quantity = 1, presentation = "Kimono / M1", unitPrice = 100m, lineTotal = 100m } },
            merchandiseTotal = 100m,
            discountTotal = 0m,
            freightTotal = 18.5m,
            total = 118.5m,
            currency = "BRL",
            fulfillmentMethod = "shipping",
            shipping = new { carrierName = "Correios", serviceName = "PAC", price = 18.5m, minimumDeliveryDays = 4, maximumDeliveryDays = 7, address = new { recipient = "Ana", street = "Avenida Paulista", number = "1000", neighborhood = "Bela Vista", city = "São Paulo", state = "SP", postalCode = "01310100", countryCode = "BR" } },
            contact = new { name = "Ana", email = "ana@example.com", phone = "11999999999" }
        });
        using var handler = new RecordingHandler(checkoutJson);
        var address = new CheckoutAddress { Recipient = "Ana", Street = "Avenida Paulista", Number = "1000", Neighborhood = "Bela Vista", City = "São Paulo", State = "sp", PostalCode = "01310-100", CountryCode = "BR" };

        var result = await Create(handler).CreateAsync(
            new([new(offer, 1)], new CheckoutContact { Name = "Ana", Email = "ana@example.com", Phone = "11999999999" }, new CheckoutFulfillment("shipping", PublicShippingQuoteId: quoteId, ShippingAddress: address)),
            new string('i', 32), new string('a', 32));

        Assert.Equal(CheckoutLoadState.Success, result.State);
        Assert.Equal("shipping", result.Checkout!.FulfillmentMethod);
        Assert.Equal(18.5m, result.Checkout.FreightTotal);
        Assert.Equal("01310100", result.Checkout.Shipping!.Address.PostalCode);
        Assert.Contains($"\"publicShippingQuoteId\":\"{quoteId}\"", handler.Body);
        Assert.Contains("\"method\":\"shipping\"", handler.Body);
    }

    [Fact]
    public async Task Pickup_create_sends_the_billing_address_only_when_given()
    {
        var offer = Guid.NewGuid();
        using var withBilling = new RecordingHandler("{}");
        using var withoutBilling = new RecordingHandler("{}");
        var contact = new CheckoutContact { Name = "Ana", Email = "ana@example.com", Phone = "11999999999" };
        var billing = new CheckoutAddress { Recipient = "Ana", Street = "Rua XV de Novembro", Number = "100", Neighborhood = "Centro", City = "Sorocaba", State = "SP", PostalCode = "18010-000", CountryCode = "BR" };

        await Create(withBilling).CreateAsync(new([new(offer, 1)], contact, new CheckoutFulfillment("pickup", Guid.NewGuid()), billing), new string('i', 32), new string('a', 32));
        await Create(withoutBilling).CreateAsync(new([new(offer, 1)], contact, new CheckoutFulfillment("pickup", Guid.NewGuid())), new string('i', 32), new string('a', 32));

        Assert.Contains("\"billingAddress\":{", withBilling.Body);
        Assert.Contains("\"city\":\"Sorocaba\"", withBilling.Body);
        Assert.DoesNotContain("billingAddress", withoutBilling.Body);
    }

    [Fact]
    public async Task Configuration_exposes_shipping_without_requiring_pickup()
    {
        var result = await Create(new RecordingHandler("{\"pickupEnabled\":false,\"shippingEnabled\":true,\"currency\":\"BRL\"}"))
            .GetConfigurationAsync();

        Assert.Equal(CheckoutLoadState.Success, result.State);
        Assert.True(result.Configuration!.ShippingEnabled);
        Assert.Null(result.Configuration.Pickup);
        Assert.Empty(result.Configuration.OnlinePaymentMethods);
    }

    [Fact]
    public async Task Configuration_maps_online_payment_methods_and_ignores_unknown_values()
    {
        var result = await Create(new RecordingHandler("{\"pickupEnabled\":false,\"shippingEnabled\":true,\"currency\":\"BRL\",\"onlinePaymentMethods\":[\"PIX\",\"card\",\"wire\"]}"))
            .GetConfigurationAsync();

        Assert.Equal(CheckoutLoadState.Success, result.State);
        Assert.Equal(new[] { OnlinePaymentMethod.Pix, OnlinePaymentMethod.Card }, result.Configuration!.OnlinePaymentMethods);
    }

    [Fact]
    public async Task Configuration_maps_embedded_mode_and_payment_client()
    {
        var result = await Create(new RecordingHandler("{\"pickupEnabled\":false,\"shippingEnabled\":true,\"currency\":\"BRL\",\"onlinePaymentMethods\":[\"pix\",\"card\"],\"onlinePaymentCheckoutMode\":\"Embedded\",\"onlinePaymentClient\":{\"providerKey\":\"mercadopago\",\"publicKey\":\"TEST-abc-123\",\"maxInstallments\":6}}"))
            .GetConfigurationAsync();

        Assert.True(result.Configuration!.EmbeddedPayments);
        Assert.Equal("mercadopago", result.Configuration.PaymentClient!.ProviderKey);
        Assert.Equal("TEST-abc-123", result.Configuration.PaymentClient.PublicKey);
        Assert.Equal(6, result.Configuration.PaymentClient.MaxInstallments);
    }

    [Theory]
    [InlineData("{\"providerKey\":\"unknown\",\"maxInstallments\":6}")]
    [InlineData("{\"providerKey\":\"mercadopago\",\"maxInstallments\":6}")]
    [InlineData("{\"providerKey\":\"mercadopago\",\"publicKey\":\"<script>\",\"maxInstallments\":6}")]
    [InlineData("{\"providerKey\":\"fake\",\"maxInstallments\":30}")]
    public async Task Configuration_falls_back_to_hosted_when_payment_client_is_invalid(string client)
    {
        var result = await Create(new RecordingHandler("{\"pickupEnabled\":false,\"shippingEnabled\":true,\"currency\":\"BRL\",\"onlinePaymentMethods\":[\"card\"],\"onlinePaymentCheckoutMode\":\"Embedded\",\"onlinePaymentClient\":" + client + "}"))
            .GetConfigurationAsync();

        Assert.False(result.Configuration!.EmbeddedPayments);
        Assert.Null(result.Configuration.PaymentClient);
    }

    [Fact]
    public async Task Embedded_card_initiation_sends_only_the_token_payload_and_maps_pending_without_url()
    {
        var id = Guid.NewGuid();
        var handler = new RecordingHandler(JsonSerializer.Serialize(new { status = "pending", method = "card", amount = 10.00m, currency = "BRL", expiresAt = DateTimeOffset.UtcNow.AddMinutes(10), checkoutMode = "Embedded", installments = 3 }));
        var card = new EmbeddedCardPayment("tok_123456789", "visa", "25", 3, "buyer@example.com", "CPF", "52998224725");

        var result = await Create(handler).InitiateEmbeddedCardAsync(id, new string('a', 32), new string('i', 32), card);

        Assert.Equal(PaymentLoadState.Success, result.State);
        Assert.True(result.Payment!.Embedded);
        Assert.Null(result.Payment.CheckoutUrl);
        Assert.Equal(3, result.Payment.Installments);
        Assert.Equal($"https://api.test/v1/storefront/checkouts/{id:D}/payments/card", handler.Request!.RequestUri!.ToString());
        Assert.Contains("\"token\":\"tok_123456789\"", handler.Body);
        Assert.Contains("\"installments\":3", handler.Body);
        Assert.Contains("\"identificationNumber\":\"52998224725\"", handler.Body);
    }

    [Fact]
    public async Task Embedded_payment_rejects_hosted_url_and_maps_failure_and_challenge()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        var withUrl = await Create(new RecordingHandler(JsonSerializer.Serialize(new { status = "pending", method = "card", amount = 10.00m, currency = "BRL", expiresAt = expires, checkoutMode = "Embedded", checkoutUrl = HostedCheckoutUrl })))
            .GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        var failed = await Create(new RecordingHandler(JsonSerializer.Serialize(new { status = "failed", method = "card", amount = 10.00m, currency = "BRL", expiresAt = expires, checkoutMode = "Embedded", failureReason = "insufficient_funds" })))
            .GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        var challenge = await Create(new RecordingHandler(JsonSerializer.Serialize(new { status = "pending", method = "card", amount = 10.00m, currency = "BRL", expiresAt = expires, checkoutMode = "Embedded", challenge = new { url = "https://acs.example/challenge", creq = "creq" } })))
            .GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        var insecureChallenge = await Create(new RecordingHandler(JsonSerializer.Serialize(new { status = "pending", method = "card", amount = 10.00m, currency = "BRL", expiresAt = expires, checkoutMode = "Embedded", challenge = new { url = "http://acs.example/challenge", creq = "creq" } })))
            .GetPaymentAsync(Guid.NewGuid(), new string('a', 32));

        Assert.Equal(PaymentLoadState.Malformed, withUrl.State);
        Assert.Equal("insufficient_funds", failed.Payment!.FailureReason);
        Assert.Equal(new PaymentChallenge("https://acs.example/challenge", "creq"), challenge.Payment!.Challenge);
        Assert.Equal(PaymentLoadState.Malformed, insecureChallenge.State);
    }

    [Fact]
    public async Task Cancel_maps_no_content_to_success()
    {
        var result = await Create(new RecordingHandler(HttpStatusCode.NoContent))
            .CancelAsync(Guid.NewGuid(), new string('a', 32));

        Assert.Equal(CheckoutLoadState.Success, result.State);
    }

    [Fact]
    public async Task Pix_initiation_sends_route_body_and_headers()
    {
        var id = Guid.NewGuid();
        var handler = new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10)));
        var result = await Create(handler).InitiatePixAsync(id, new string('a', 32), new string('i', 32));
        Assert.Equal(PaymentLoadState.Success, result.State);
        Assert.Equal($"https://api.test/v1/storefront/checkouts/{id:D}/payments/pix", handler.Request!.RequestUri!.ToString());
        Assert.Equal(new string('a', 32), handler.Request.Headers.GetValues("X-Checkout-Access-Token").Single());
        Assert.Equal(new string('i', 32), handler.Request.Headers.GetValues("Idempotency-Key").Single());
        Assert.Contains("\"method\":\"pix\"", handler.Body);
    }

    [Fact]
    public async Task Card_initiation_sends_method_and_maps_hosted_checkout_url()
    {
        var id = Guid.NewGuid();
        var handler = new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10), method: "card", checkoutUrl: HostedCheckoutUrl, includePix: false));
        var result = await Create(handler).InitiateCardAsync(id, new string('a', 32), new string('i', 32));
        Assert.Equal(PaymentLoadState.Success, result.State);
        Assert.Equal(OnlinePaymentMethod.Card, result.Payment!.Method);
        Assert.Equal(HostedCheckoutUrl, result.Payment.CheckoutUrl);
        Assert.Equal("", result.Payment.PixCopyPaste);
        Assert.Equal($"https://api.test/v1/storefront/checkouts/{id:D}/payments/card", handler.Request!.RequestUri!.ToString());
        Assert.Equal(new string('a', 32), handler.Request.Headers.GetValues("X-Checkout-Access-Token").Single());
        Assert.Equal(new string('i', 32), handler.Request.Headers.GetValues("Idempotency-Key").Single());
        Assert.Contains("\"method\":\"card\"", handler.Body);
        Assert.DoesNotContain("paymentToken", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("4242424242424242", handler.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, PaymentLoadState.Validation, "Não foi possível iniciar o pagamento com cartão com os dados atuais.")]
    [InlineData(HttpStatusCode.Conflict, PaymentLoadState.Conflict, "A tentativa de pagamento com cartão mudou. Atualize a página e tente novamente.")]
    public async Task Card_initiation_uses_payment_specific_error_mapping(HttpStatusCode status, PaymentLoadState state, string message)
    {
        var result = await Create(new RecordingHandler(status))
            .InitiateCardAsync(Guid.NewGuid(), new string('a', 32), new string('i', 32));

        Assert.Equal(state, result.State);
        Assert.Equal(message, result.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, PaymentLoadState.Validation, "Não foi possível iniciar o pagamento PIX com os dados atuais.")]
    [InlineData(HttpStatusCode.Conflict, PaymentLoadState.Conflict, "A tentativa de pagamento PIX mudou. Atualize a página e tente novamente.")]
    public async Task Pix_initiation_uses_payment_specific_error_mapping(HttpStatusCode status, PaymentLoadState state, string message)
    {
        var result = await Create(new RecordingHandler(status))
            .InitiatePixAsync(Guid.NewGuid(), new string('a', 32), new string('i', 32));

        Assert.Equal(state, result.State);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public async Task Payment_get_and_cancel_use_protected_routes()
    {
        var id = Guid.NewGuid();
        using var getHandler = new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10)));
        await Create(getHandler).GetPaymentAsync(id, new string('a', 32));
        Assert.Equal($"https://api.test/v1/storefront/checkouts/{id:D}/payment", getHandler.Request!.RequestUri!.ToString());
        using var cancelHandler = new RecordingHandler(HttpStatusCode.NoContent);
        var result = await Create(cancelHandler).CancelPaymentAsync(id, new string('a', 32));
        Assert.Equal(PaymentLoadState.Success, result.State);
        Assert.Equal($"https://api.test/v1/storefront/checkouts/{id:D}/payment/cancel", cancelHandler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Pix_terminal_response_may_be_expired_but_converted_requires_public_number()
    {
        var expired = await Create(new RecordingHandler(PaymentJson("failed", DateTimeOffset.UtcNow.AddDays(-1)))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Success, expired.State);
        var converted = await Create(new RecordingHandler(PaymentJson("converted", DateTimeOffset.UtcNow.AddDays(-1), "MF-0123456789ABCDEF"))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Success, converted.State);
        var invalid = await Create(new RecordingHandler(PaymentJson("converted", DateTimeOffset.UtcNow.AddDays(-1)))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Malformed, invalid.State);
    }

    [Fact]
    public async Task Pix_rejects_bad_qr_and_status()
    {
        var badQr = await Create(new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10), null, "bm90LXBuZw=="))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Malformed, badQr.State);
        var missingPendingPix = await Create(new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10), includePix: false))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Malformed, missingPendingPix.State);
        var processingWithoutPix = await Create(new RecordingHandler(PaymentJson("conversionpending", DateTimeOffset.UtcNow.AddMinutes(10), includePix: false))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Success, processingWithoutPix.State);
        var cancellationPending = await Create(new RecordingHandler(PaymentJson("CancellationPending", DateTimeOffset.UtcNow.AddMinutes(10), includePix: false))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Success, cancellationPending.State);
        Assert.Equal("cancellationpending", cancellationPending.Payment!.Status);
        var pendingCard = await Create(new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10), method: "card", checkoutUrl: HostedCheckoutUrl, includePix: false))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Success, pendingCard.State);
        Assert.Equal(OnlinePaymentMethod.Card, pendingCard.Payment!.Method);
        Assert.Equal(HostedCheckoutUrl, pendingCard.Payment.CheckoutUrl);
        var pendingCardWithoutUrl = await Create(new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10), method: "card", includePix: false))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Malformed, pendingCardWithoutUrl.State);
        var pendingCardWithPix = await Create(new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10), method: "card", checkoutUrl: HostedCheckoutUrl))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Malformed, pendingCardWithPix.State);
        var disallowedCheckoutUrl = await Create(new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10), method: "card", checkoutUrl: "http://example.com/checkout", includePix: false))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Malformed, disallowedCheckoutUrl.State);
        var pendingPixHosted = await Create(new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10), method: "pix", checkoutUrl: "https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=1", includePix: false))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Success, pendingPixHosted.State);
        Assert.Equal("https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=1", pendingPixHosted.Payment!.CheckoutUrl);
        Assert.Equal("", pendingPixHosted.Payment.PixCopyPaste);
        var convertedCard = await Create(new RecordingHandler(PaymentJson("converted", DateTimeOffset.UtcNow.AddDays(-1), "MF-0123456789ABCDEF", method: "card", includePix: false))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Success, convertedCard.State);
        Assert.Equal("MF-0123456789ABCDEF", convertedCard.Payment!.PublicOrderNumber);
        Assert.Null(convertedCard.Payment.CheckoutUrl);
        var badStatus = await Create(new RecordingHandler(PaymentJson("unknown", DateTimeOffset.UtcNow.AddMinutes(10)))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Malformed, badStatus.State);
        var unknownMethod = await Create(new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10), method: "wire"))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Malformed, unknownMethod.State);
        var numericMethod = await Create(new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10), method: "0"))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Malformed, numericMethod.State);
    }

    [Fact]
    public async Task Checkout_accepts_every_backend_payment_lifecycle_status_and_rejects_unknown()
    {
        foreach (var status in new[] { "Active", "Cancelled", "Expired", "PaymentPending", "ConversionPending", "RefundPending", "Completed", "Refunded" })
        {
            var result = await Create(new RecordingHandler(CheckoutJson(status))).GetAsync(Guid.NewGuid(), new string('a', 32));
            Assert.Equal(CheckoutLoadState.Success, result.State);
            Assert.Equal(status.ToLowerInvariant(), result.Checkout!.Status);
        }
        Assert.Equal(CheckoutLoadState.Malformed, (await Create(new RecordingHandler(CheckoutJson("Unknown"))).GetAsync(Guid.NewGuid(), new string('a', 32))).State);
    }

    [Fact]
    public void Open_checkout_still_awaits_pix_when_the_charge_was_not_created()
    {
        var missing = new PixPayment { Status = "failed", FailureReason = "invalid_payment_data" };
        var declined = new PixPayment { Status = "failed", FailureReason = "card_rejected" };

        Assert.True(missing.NeedsPixPresentation("paymentpending"));
        Assert.True(new PixPayment { Status = "failed", FailureReason = "provider_unauthorized" }.NeedsPixPresentation("active"));
        Assert.False(declined.NeedsPixPresentation("paymentpending"));
        Assert.False(missing.NeedsPixPresentation("completed"));
        Assert.False(new PixPayment { Status = "failed", Method = OnlinePaymentMethod.Card, FailureReason = "invalid_payment_data" }.NeedsPixPresentation("paymentpending"));
    }

    private const string HostedCheckoutUrl = "http://127.0.0.1/v1/testing/online-payments/hosted/ref";
    private static string PaymentJson(string status, DateTimeOffset expires, string? order = null, string? qr = null, bool includePix = true, string? method = null, string? checkoutUrl = null) => JsonSerializer.Serialize(new { status, method, amount = 10.00m, currency = "BRL", expiresAt = expires, pixCopyPaste = includePix ? "000201010212" : null, qrCodePngBase64 = includePix ? qr ?? PngBase64 : null, checkoutUrl, publicOrderNumber = order });
    private static string CheckoutJson(string status) => JsonSerializer.Serialize(new { publicCheckoutId = Guid.Parse("11111111-1111-1111-1111-111111111111"), status, expiresAt = DateTimeOffset.UtcNow.AddHours(1), accessExpiresAt = DateTimeOffset.UtcNow.AddDays(30), lines = new[] { new { publicOfferId = Guid.Parse("22222222-2222-2222-2222-222222222222"), quantity = 1, presentation = "Item", unitPrice = 10m, lineTotal = 10m } }, merchandiseTotal = 10m, discountTotal = 0m, freightTotal = 0m, total = 10m, currency = "BRL", fulfillmentMethod = "pickup", pickup = new { publicPickupId = Guid.Parse("33333333-3333-3333-3333-333333333333"), displayName = "Loja", address = new { street = "Rua", number = "1", neighborhood = "Centro", city = "Sorocaba", state = "SP", postalCode = "18000-000" }, hours = "09:00", instructions = "" }, contact = new { name = "Ana", email = "ana@example.com", phone = "1" } });
    private const string PngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAElEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    private static CheckoutClient Create(HttpMessageHandler handler)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        var options = new CatalogApiOptions { BaseUrl = "https://api.test", TimeoutSeconds = 2, ProxySecret = "proxy-secret" };
        return new(
            StorefrontApiTestPipeline.CreateClient(handler, options, context),
            Options.Create(options),
            NullLogger<CheckoutClient>.Instance);
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public RecordingHandler(HttpStatusCode status) : this("") { Status = status; }
        private HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
