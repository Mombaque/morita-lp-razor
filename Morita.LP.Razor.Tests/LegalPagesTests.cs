using System.Net;
using System.Net.Http;
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

public sealed class LegalPagesTests
{
    private static readonly StorefrontStore Store = new()
    {
        DisplayName = "Morita",
        LegalName = "Morita Artigos Esportivos LTDA",
        Cnpj = "11.222.333/0001-81",
        ContactEmail = "contato@morita.test",
        Address = new StorefrontStoreAddress { Street = "Rua Coronel Nogueira Padilha", Number = "429", City = "Sorocaba", State = "SP" }
    };

    [Theory]
    [InlineData("/privacidade", "Política de Privacidade")]
    [InlineData("/termos", "Termos de Uso e Compra")]
    [InlineData("/trocas-e-devolucoes", "artigo 49 do Código de Defesa do Consumidor")]
    public async Task Legal_pages_render_with_the_store_identity(string path, string expected)
    {
        using var factory = Create(Store);

        var html = WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync(path));

        Assert.Contains(expected, html);
        Assert.Contains("Morita Artigos Esportivos LTDA (CNPJ 11.222.333/0001-81)", html);
        Assert.Contains("mailto:contato@morita.test", html);
    }

    [Fact]
    public async Task Legal_pages_fall_back_to_the_static_store_details()
    {
        using var factory = Create(null);

        var html = WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync("/termos"));

        Assert.Contains("As vendas deste site são feitas por Morita, com loja física na Rua Coronel Nogueira Padilha, 429 - Sorocaba, SP.", html);
    }

    [Fact]
    public async Task Footer_links_to_the_legal_pages()
    {
        using var factory = Create(null);

        var html = await factory.CreateClient().GetStringAsync("/cart");

        Assert.Contains("href=\"/trocas-e-devolucoes\"", html);
        Assert.Contains("href=\"/termos\"", html);
        Assert.Contains("href=\"/privacidade\"", html);
    }

    [Fact]
    public async Task Unknown_page_returns_404_with_the_friendly_page_for_browsers()
    {
        using var factory = Create(null);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/pagina-que-nao-existe");
        request.Headers.Accept.ParseAdd("text/html");

        var response = await factory.CreateClient().SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Página não encontrada", html);
        Assert.Contains("noindex,nofollow", html);
    }

    [Fact]
    public async Task Unknown_path_without_html_accept_keeps_an_empty_404()
    {
        using var factory = Create(null);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/pagina-que-nao-existe");
        request.Headers.Accept.ParseAdd("application/json");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    private static WebApplicationFactory<Program> Create(StorefrontStore? store) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
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
