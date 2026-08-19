using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public interface ICatalogClient
{
    Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default);
}
