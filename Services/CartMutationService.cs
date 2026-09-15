using System.Net.Http;
using System.Text.Json;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public enum CartMutationStatus
{
    Success,
    InvalidQuantity,
    Insufficient,
    Inactive,
    Removed,
    Unavailable,
    PersistenceFailed
}

public sealed record CartMutationResult(CartMutationStatus Status)
{
    public bool Succeeded => Status == CartMutationStatus.Success;
}

public interface ICartMutationService
{
    Task<CartMutationResult> AddAsync(Guid publicOfferId, int quantity, CancellationToken cancellationToken = default);
    Task<CartMutationResult> UpdateAsync(Guid publicOfferId, int quantity, CancellationToken cancellationToken = default);
}

/// <summary>Checks current catalog availability before changing the opaque cart cookie.</summary>
public sealed class CartMutationService(ICartCookieStore cart, ICatalogClient catalog) : ICartMutationService
{
    public async Task<CartMutationResult> AddAsync(Guid publicOfferId, int quantity, CancellationToken cancellationToken = default)
    {
        var state = cart.Read();
        if (!TryBuildAdd(state, publicOfferId, quantity, out var lines))
            return new(CartMutationStatus.InvalidQuantity);

        var status = await CheckAvailabilityAsync(publicOfferId, lines, cancellationToken);
        if (status != CartMutationStatus.Success)
            return new(status);

        return cart.Add(publicOfferId, quantity)
            ? new(CartMutationStatus.Success)
            : new(CartMutationStatus.PersistenceFailed);
    }

    public async Task<CartMutationResult> UpdateAsync(Guid publicOfferId, int quantity, CancellationToken cancellationToken = default)
    {
        var state = cart.Read();
        if (!TryBuildUpdate(state, publicOfferId, quantity, out var lines))
            return new(CartMutationStatus.InvalidQuantity);

        var status = await CheckAvailabilityAsync(publicOfferId, lines, cancellationToken);
        if (status != CartMutationStatus.Success)
            return new(status);

        return cart.Update(publicOfferId, quantity)
            ? new(CartMutationStatus.Success)
            : new(CartMutationStatus.PersistenceFailed);
    }

    private async Task<CartMutationStatus> CheckAvailabilityAsync(Guid publicOfferId, IReadOnlyList<CartLine> lines, CancellationToken cancellationToken)
    {
        CatalogQuoteResult quote;
        try
        {
            quote = await catalog.QuoteAsync(new CatalogQuoteRequest(lines.Select(x => new CatalogQuoteItem(x.PublicOfferId, x.Quantity)).ToList()), cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CartMutationStatus.Unavailable;
        }
        catch (HttpRequestException)
        {
            return CartMutationStatus.Unavailable;
        }
        catch (JsonException)
        {
            return CartMutationStatus.Unavailable;
        }

        if (quote.State == CatalogLoadState.Unavailable)
            return CartMutationStatus.Unavailable;

        var line = quote.Lines.FirstOrDefault(x => x.PublicOfferId == publicOfferId);
        return line?.Availability?.Trim().ToLowerInvariant() switch
        {
            "available" => CartMutationStatus.Success,
            "insufficient" => CartMutationStatus.Insufficient,
            "inactive" => CartMutationStatus.Inactive,
            "removed" => CartMutationStatus.Removed,
            _ => CartMutationStatus.Unavailable
        };
    }

    private static bool TryBuildAdd(CartState state, Guid publicOfferId, int quantity, out IReadOnlyList<CartLine> lines)
    {
        lines = [];
        if (publicOfferId == Guid.Empty || quantity is < 1 or > CartCookieStore.MaxUnitsPerLine)
            return false;

        var next = state.Lines.ToList();
        var index = next.FindIndex(x => x.PublicOfferId == publicOfferId);
        var targetQuantity = (index < 0 ? 0 : next[index].Quantity) + quantity;
        if (targetQuantity > CartCookieStore.MaxUnitsPerLine || index < 0 && next.Count >= CartCookieStore.MaxLines || state.Lines.Sum(x => x.Quantity) + quantity > CartCookieStore.MaxTotalUnits)
            return false;

        if (index < 0)
            next.Add(new(publicOfferId, quantity));
        else
            next[index] = new(publicOfferId, targetQuantity);
        lines = next;
        return true;
    }

    private static bool TryBuildUpdate(CartState state, Guid publicOfferId, int quantity, out IReadOnlyList<CartLine> lines)
    {
        lines = [];
        if (publicOfferId == Guid.Empty || quantity is < 1 or > CartCookieStore.MaxUnitsPerLine)
            return false;

        var next = state.Lines.ToList();
        var index = next.FindIndex(x => x.PublicOfferId == publicOfferId);
        if (index < 0 || state.Lines.Sum(x => x.Quantity) - next[index].Quantity + quantity > CartCookieStore.MaxTotalUnits)
            return false;

        next[index] = new(publicOfferId, quantity);
        lines = next;
        return true;
    }
}
