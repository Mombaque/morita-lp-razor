using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Pages;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class OrderPageAccessTests
{
    private const string OrderNumber = "MF-DDDDDDDDDDDDDDDD";

    [Theory]
    [InlineData(OrderLoadState.Unavailable)]
    [InlineData(OrderLoadState.Timeout)]
    public async Task Temporary_order_failure_keeps_the_device_link_and_offers_a_retry(OrderLoadState state)
    {
        var access = new Access();
        var page = Create(new Orders(OrderResult.Failure(state)), access);

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(access.Cleared);
        Assert.True(page.CanRetry);
        Assert.Equal(OrderModel.TemporarilyUnavailable, page.Message);
    }

    [Fact]
    public async Task Malformed_order_keeps_the_device_link()
    {
        var access = new Access();
        var page = Create(new Orders(OrderResult.Failure(OrderLoadState.Malformed)), access);

        Assert.IsType<PageResult>(await page.OnGetAsync(CancellationToken.None));
        Assert.False(access.Cleared);
        Assert.True(page.CanRetry);
    }

    [Fact]
    public async Task Rejected_token_clears_the_device_link()
    {
        var access = new Access();
        var page = Create(new Orders(OrderResult.Failure(OrderLoadState.Unauthorized)), access);

        Assert.IsType<PageResult>(await page.OnGetAsync(CancellationToken.None));
        Assert.True(access.Cleared);
        Assert.Equal(OrderModel.NotOnThisDevice, page.Message);
        Assert.False(page.CanRetry);
    }

    [Fact]
    public async Task Signed_in_shopper_without_the_device_link_is_sent_to_the_account_order()
    {
        var orders = new Orders(OrderResult.Failure(OrderLoadState.Unavailable));
        var page = Create(orders, new Access(hasLink: false), signedIn: true);

        var redirect = Assert.IsType<RedirectToPageResult>(await page.OnGetAsync(CancellationToken.None));

        Assert.Equal("/Account", redirect.PageName);
        Assert.Equal(OrderNumber, redirect.RouteValues?["publicOrderNumber"]);
        Assert.Equal("pedidos", redirect.Fragment);
        Assert.Equal(0, orders.Calls);
    }

    [Theory]
    [InlineData(OrderLoadState.Unauthorized)]
    [InlineData(OrderLoadState.Malformed)]
    public async Task Signed_in_shopper_is_sent_to_the_account_order_when_this_page_cannot_show_it(OrderLoadState state)
    {
        var page = Create(new Orders(OrderResult.Failure(state)), new Access(), signedIn: true);

        var redirect = Assert.IsType<RedirectToPageResult>(await page.OnGetAsync(CancellationToken.None));

        Assert.Equal("/Account", redirect.PageName);
    }

    [Fact]
    public async Task Guest_without_the_device_link_sees_the_device_message_and_keeps_other_cookies()
    {
        var access = new Access(hasLink: false);
        var page = Create(new Orders(OrderResult.Failure(OrderLoadState.Unavailable)), access);

        Assert.IsType<PageResult>(await page.OnGetAsync(CancellationToken.None));
        Assert.False(access.Cleared);
        Assert.Equal(OrderModel.NotOnThisDevice, page.Message);
    }

    private static OrderModel Create(Orders orders, Access access, bool signedIn = false)
    {
        var page = new OrderModel(orders, access, new AccountCookies(signedIn)) { PublicOrderNumber = OrderNumber };
        page.PageContext = new PageContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new PageActionDescriptor()))
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        return page;
    }

    private sealed class Orders(OrderResult result) : IOrderClient
    {
        public int Calls { get; private set; }
        public Task<OrderResult> GetAsync(string publicOrderNumber, string accessToken, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class Access(bool hasLink = true) : IOrderAccessCookieStore
    {
        public bool Cleared { get; private set; }
        public OrderAccess? Read(string publicOrderNumber) => hasLink ? new(OrderNumber, new string('o', 32), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)) : null;
        public bool Write(string publicOrderNumber, string token) => true;
        public void Clear() => Cleared = true;
    }

    private sealed class AccountCookies(bool signedIn) : ICustomerAccountCookieStore
    {
        public CustomerAccountSession? Read() => signedIn ? new(new string('s', 32), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)) : null;
        public bool Write(string token, DateTimeOffset expiresAt) => true;
        public void Clear() { }
    }
}
