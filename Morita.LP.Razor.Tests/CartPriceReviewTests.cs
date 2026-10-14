using System;
using System.Collections.Generic;
using System.Linq;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class CartPriceReviewTests
{
    private static readonly Guid Rashguard = Guid.NewGuid();
    private static readonly Guid Belt = Guid.NewGuid();

    [Fact]
    public void Changed_price_is_reported_and_the_current_price_is_acknowledged()
    {
        var cart = new RecordingCart();
        var state = State(new CartLine(Rashguard, 1, 189m), new CartLine(Belt, 1, 79m));

        var changes = new CartPriceReview(cart).Acknowledge(state, Quote((Rashguard, 259m), (Belt, 79m)));

        Assert.Equal([new CartPriceChange("Rashguard · Preta · A1", 189m, 259m, "BRL")], changes);
        Assert.Equal([new CartLine(Rashguard, 1, 259m), new CartLine(Belt, 1, 79m)], cart.Replaced);
    }

    [Fact]
    public void Unchanged_prices_report_nothing_and_leave_the_cookie_alone()
    {
        var cart = new RecordingCart();

        var changes = new CartPriceReview(cart).Acknowledge(State(new CartLine(Rashguard, 2, 189.00m)), Quote((Rashguard, 189m)));

        Assert.Empty(changes);
        Assert.Null(cart.Replaced);
    }

    [Fact]
    public void Lines_without_a_seen_price_are_backfilled_without_a_notice()
    {
        var cart = new RecordingCart();

        var changes = new CartPriceReview(cart).Acknowledge(State(new CartLine(Rashguard, 1)), Quote((Rashguard, 259m)));

        Assert.Empty(changes);
        Assert.Equal([new CartLine(Rashguard, 1, 259m)], cart.Replaced);
    }

    [Fact]
    public void Unavailable_quote_and_unpriced_lines_keep_the_seen_price()
    {
        var cart = new RecordingCart();
        var state = State(new CartLine(Rashguard, 1, 189m));
        var unpriced = new CatalogQuoteResult(CatalogLoadState.Partial, "BRL", 0, [new CatalogQuoteLine { PublicOfferId = Rashguard, Availability = "removed" }]);

        Assert.Empty(new CartPriceReview(cart).Acknowledge(state, CatalogQuoteResult.Unavailable()));
        Assert.Empty(new CartPriceReview(cart).Acknowledge(state, unpriced));
        Assert.Null(cart.Replaced);
    }

    private static CartState State(params CartLine[] lines) => new(DateTimeOffset.UtcNow, lines);

    private static CatalogQuoteResult Quote(params (Guid Offer, decimal Price)[] lines) =>
        new(CatalogLoadState.Success, "BRL", lines.Sum(line => line.Price), lines
            .Select(line => new CatalogQuoteLine
            {
                PublicOfferId = line.Offer,
                Quantity = 1,
                Availability = "available",
                Presentation = line.Offer == Rashguard ? "Rashguard · Preta · A1" : "Faixa · Azul · A2",
                Currency = "BRL",
                UnitPrice = line.Price,
                LinePrice = line.Price
            })
            .ToList());

    private sealed class RecordingCart : ICartCookieStore
    {
        public IReadOnlyList<CartLine>? Replaced { get; private set; }
        public CartState Read() => new(DateTimeOffset.UtcNow, []);
        public bool Add(Guid offerId, int quantity, decimal? seenUnitPrice = null) => true;
        public bool Update(Guid offerId, int quantity) => true;
        public bool Remove(Guid offerId) => true;
        public bool Replace(IReadOnlyList<CartLine> lines) { Replaced = lines; return true; }
        public void Clear() { }
    }
}
