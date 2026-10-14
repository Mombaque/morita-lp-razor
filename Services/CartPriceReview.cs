using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public sealed record CartPriceChange(string Presentation, decimal PreviousUnitPrice, decimal CurrentUnitPrice, string? Currency);

public interface ICartPriceReview
{
    IReadOnlyList<CartPriceChange> Acknowledge(CartState state, CatalogQuoteResult quote);
}

public sealed class CartPriceReview(ICartCookieStore cart) : ICartPriceReview
{
    public IReadOnlyList<CartPriceChange> Acknowledge(CartState state, CatalogQuoteResult quote)
    {
        if (quote.State == CatalogLoadState.Unavailable || state.Lines.Count == 0)
            return [];

        var quoted = quote.Lines
            .Where(line => line.UnitPrice is not null)
            .DistinctBy(line => line.PublicOfferId)
            .ToDictionary(line => line.PublicOfferId);

        var changes = state.Lines
            .Where(line => line.SeenUnitPrice is { } seen && quoted.TryGetValue(line.PublicOfferId, out var current) && current.UnitPrice != seen)
            .Select(line => ToChange(line, quoted[line.PublicOfferId], quote.Currency))
            .ToList();

        var acknowledged = state.Lines
            .Select(line => quoted.TryGetValue(line.PublicOfferId, out var current) ? line with { SeenUnitPrice = current.UnitPrice } : line)
            .ToList();
        if (!acknowledged.SequenceEqual(state.Lines))
            cart.Replace(acknowledged);

        return changes;
    }

    private static CartPriceChange ToChange(CartLine line, CatalogQuoteLine current, string? currency) =>
        new(current.Presentation ?? "", line.SeenUnitPrice!.Value, current.UnitPrice!.Value, current.Currency ?? currency);
}
