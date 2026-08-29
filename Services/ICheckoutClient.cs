using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public interface ICheckoutClient
{
    Task<CheckoutConfigurationResult> GetConfigurationAsync(CancellationToken cancellationToken = default);
    Task<ShippingQuoteResult> QuoteShippingAsync(ShippingQuoteRequest request, CancellationToken cancellationToken = default) => Task.FromResult(ShippingQuoteResult.Failure(CheckoutLoadState.Unavailable));
    Task<CheckoutResult> CreateAsync(CheckoutCreateRequest request, string idempotencyKey, string accessToken, CancellationToken cancellationToken = default);
    Task<CheckoutResult> CreateForAccountAsync(CheckoutCreateRequest request, string idempotencyKey, string accessToken, string? storefrontSession, CancellationToken cancellationToken = default)
        => CreateAsync(request, idempotencyKey, accessToken, cancellationToken);
    Task<CheckoutResult> GetAsync(Guid publicCheckoutId, string accessToken, CancellationToken cancellationToken = default);
    Task<CheckoutResult> CancelAsync(Guid publicCheckoutId, string accessToken, CancellationToken cancellationToken = default);
    Task<PaymentResult> InitiatePixAsync(Guid publicCheckoutId, string accessToken, string idempotencyKey, CancellationToken cancellationToken = default) => Task.FromResult(PaymentResult.Failure(PaymentLoadState.Unavailable));
    Task<PaymentResult> GetPaymentAsync(Guid publicCheckoutId, string accessToken, CancellationToken cancellationToken = default) => Task.FromResult(PaymentResult.Failure(PaymentLoadState.NotFound));
    Task<PaymentResult> CancelPaymentAsync(Guid publicCheckoutId, string accessToken, CancellationToken cancellationToken = default) => Task.FromResult(PaymentResult.Failure(PaymentLoadState.Unavailable));
}
