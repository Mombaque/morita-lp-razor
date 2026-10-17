using System.Net;
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

public sealed class StorefrontStoreTests
{
    private static readonly StorefrontStore OpenStore = new()
    {
        DisplayName = "Morita",
        LegalName = "Morita Artigos Esportivos LTDA",
        Cnpj = "11.222.333/0001-81",
        ContactEmail = "contato@morita.test",
        Address = new StorefrontStoreAddress { Street = "Rua Coronel Nogueira Padilha", Number = "429", Neighborhood = "Centro", City = "Sorocaba", State = "SP", PostalCode = "18000000" }
    };

    [Fact]
    public async Task Footer_shows_the_legal_identity_and_no_pause_banner_when_open()
    {
        using var factory = Create(OpenStore);

        var html = WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync("/cart"));

        Assert.Contains("Morita Artigos Esportivos LTDA · CNPJ 11.222.333/0001-81", html);
        Assert.Contains("Rua Coronel Nogueira Padilha, 429 · Centro · Sorocaba - SP · 18000-000", html);
        Assert.Contains("mailto:contato@morita.test", html);
        Assert.DoesNotContain("data-checkout-paused", html);
        Assert.Contains("href=\"/checkout\">Finalizar compra", html);
    }

    [Fact]
    public async Task Paused_store_shows_the_banner_and_hides_the_mini_cart_checkout()
    {
        using var factory = Create(Paused("Voltamos amanhã às 10h."));

        var html = WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync("/cart"));

        Assert.Contains("data-checkout-paused>Voltamos amanhã às 10h.</div>", html);
        Assert.DoesNotContain("href=\"/checkout\">Finalizar compra", html);
    }

    [Fact]
    public async Task Paused_checkout_shows_the_message_without_requiring_sign_in()
    {
        using var factory = Create(Paused(null));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/checkout");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-checkout=\"paused\"", html);
        Assert.Contains(StorefrontStore.DefaultCheckoutPausedMessage, html);
        Assert.Contains("<h1>Compras pausadas</h1>", html);
        Assert.DoesNotContain("Como você quer receber?", html);
    }

    [Fact]
    public async Task Unavailable_store_info_keeps_the_footer_and_checkout_working()
    {
        using var factory = Create(null);

        var html = WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync("/cart"));

        Assert.Contains("Rua Coronel Nogueira Padilha, 429", html);
        Assert.DoesNotContain("data-store-legal", html);
        Assert.DoesNotContain("data-checkout-paused", html);
    }

    private static StorefrontStore Paused(string? message) => new()
    {
        DisplayName = "Morita",
        CheckoutPaused = true,
        CheckoutPausedMessage = message
    };

    private static WebApplicationFactory<Program> Create(StorefrontStore? store) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.UseSetting("Storefront:CustomerAccountsEnabled", "true");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStoreInfoClient>();
                services.AddScoped<IStoreInfoClient>(_ => new FixedStoreInfo(store));
            });
        });

    private sealed class FixedStoreInfo(StorefrontStore? store) : IStoreInfoClient
    {
        public Task<StorefrontStore?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(store);
    }
}
