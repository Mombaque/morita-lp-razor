using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public sealed class CatalogService(ProductService legacy, ICatalogClient api, IOptions<StorefrontOptions> options)
{
    private readonly bool _apiMode = string.Equals(options.Value.ProductSource, "Api", StringComparison.OrdinalIgnoreCase);

    public Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default) =>
        _apiMode
            ? api.GetProductsAsync(modality, cancellationToken)
            : Task.FromResult(CatalogResult.Success(legacy.GetLegacyProducts(modality)));
}
