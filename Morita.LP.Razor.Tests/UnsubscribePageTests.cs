using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class UnsubscribePageTests
{
    [Fact]
    public async Task Opening_the_link_only_asks_for_confirmation()
    {
        var account = new UnsubscribeAccount(AccountLoadState.Success);
        using var factory = Create(account);
        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync("/descadastrar?token=abc.def")).Content.ReadAsStringAsync());

        Assert.Contains("Confirmar descadastro", html);
        Assert.Contains("name=\"robots\" content=\"noindex,nofollow\"", html);
        Assert.Null(account.LastToken);
    }

    [Theory]
    [InlineData(AccountLoadState.Success, "Pronto. Você não vai mais receber")]
    [InlineData(AccountLoadState.Validation, "Este link de descadastro é inválido.")]
    public async Task Confirming_calls_the_api_and_reports_the_outcome(AccountLoadState state, string message)
    {
        var account = new UnsubscribeAccount(state);
        using var factory = Create(account);
        using var client = factory.CreateClient();
        var page = await (await client.GetAsync("/descadastrar?token=abc.def")).Content.ReadAsStringAsync();
        var antiforgery = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

        var response = await client.PostAsync("/descadastrar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiforgery,
            ["Token"] = "abc.def"
        }));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal("abc.def", account.LastToken);
        Assert.Contains(message, html);
    }

    [Fact]
    public async Task A_link_without_token_explains_and_offers_the_account_page()
    {
        using var factory = Create(new UnsubscribeAccount(AccountLoadState.Success));
        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync("/descadastrar")).Content.ReadAsStringAsync());

        Assert.Contains("está incompleto", html);
        Assert.DoesNotContain("Confirmar descadastro", html);
        Assert.Contains("/conta#comunicacoes", html);
    }

    private static WebApplicationFactory<Program> Create(UnsubscribeAccount account) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICustomerAccountClient>();
                services.AddScoped<ICustomerAccountClient>(_ => account);
            });
        });

    private sealed class UnsubscribeAccount(AccountLoadState state) : ICustomerAccountClient
    {
        public string? LastToken { get; private set; }

        public Task<AccountResult<bool>> UnsubscribeMarketingAsync(string unsubscribeToken, CancellationToken cancellationToken = default)
        {
            LastToken = unsubscribeToken;
            return Task.FromResult(new AccountResult<bool>(state, state == AccountLoadState.Success));
        }

        public Task<AccountResult<AccountCodeChallenge>> RegisterAsync(string email, string name, string phone, string password, bool acceptedPrivacyPolicy, string privacyPolicyVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<(CustomerAccountSession Session, CustomerAccountProfile Profile)>> VerifyEmailAsync(Guid challengeId, string code, bool acceptedPrivacyPolicy, string privacyPolicyVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<(CustomerAccountSession Session, CustomerAccountProfile Profile)>> LoginAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<AccountCodeChallenge>> RequestPasswordCodeAsync(string? token, string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<(CustomerAccountSession Session, CustomerAccountProfile Profile)>> ResetPasswordAsync(string? token, Guid challengeId, string code, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<CustomerAccountProfile>> GetProfileAsync(string token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<bool>> UpdateProfileAsync(string token, string? name, string? phone, string? document, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<IReadOnlyList<CustomerAccountAddress>>> GetAddressesAsync(string token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<CustomerAccountAddress>> CreateAddressAsync(string token, CustomerAccountAddress address, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<CustomerAccountAddress>> UpdateAddressAsync(string token, Guid id, CustomerAccountAddress address, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<bool>> DeleteAddressAsync(string token, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<bool>> SetDefaultAddressAsync(string token, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<bool>> CloseAsync(string token, string currentPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<bool>> LogoutAsync(string token, bool all, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<StorefrontAccountOrderPage>> GetOrdersAsync(string token, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<PublicOrder>> GetOrderAsync(string token, string number, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountResult<PublicCustomerOrderProblem>> ReportProblemAsync(string token, string number, string reason, string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
