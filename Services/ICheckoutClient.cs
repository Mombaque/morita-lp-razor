using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public interface ICheckoutClient
{
    Task<CheckoutConfigurationResult> GetConfigurationAsync(CancellationToken cancellationToken = default);
    Task<CheckoutResult> CreateAsync(CheckoutCreateRequest request, string idempotencyKey, string accessToken, CancellationToken cancellationToken = default);
    Task<CheckoutResult> GetAsync(Guid publicCheckoutId, string accessToken, CancellationToken cancellationToken = default);
    Task<CheckoutResult> CancelAsync(Guid publicCheckoutId, string accessToken, CancellationToken cancellationToken = default);
}
