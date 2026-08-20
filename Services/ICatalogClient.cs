using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public interface ICatalogClient
{
    Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default);
    Task<CatalogPage> GetCatalogAsync(CatalogQuery query, CancellationToken cancellationToken = default) => Task.FromResult(new CatalogPage([], query.Page, CatalogQuery.PageSize, 0, 0, CatalogLoadState.Unavailable));
    Task<CatalogFilters?> GetFiltersAsync(CancellationToken cancellationToken = default) => Task.FromResult<CatalogFilters?>(null);
    Task<ProductDetailResult> GetProductAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(ProductDetailResult.Unavailable());
    Task<CatalogResult> GetRelatedAsync(string slug, int limit = 4, CancellationToken cancellationToken = default) => Task.FromResult(CatalogResult.Unavailable());
    Task<CatalogQuoteResult> QuoteAsync(CatalogQuoteRequest request, CancellationToken cancellationToken = default) => Task.FromResult(CatalogQuoteResult.Unavailable());
}
