using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class Phase03CatalogClientTests
{
    [Fact]
    public async Task Maps_realistic_numeric_lookup_guid_offer_and_details()
    {
        const string offer = "a3b7d8aa-1f05-4b7c-8f33-7fc9c0a9ef11";
        var json = $$"""{"items":[{"slug":"kimono-a1","name":"Kimono A1","description":"Leve","details":["Algodão","Gramatura 400"],"category":{"id":7,"slug":"kimonos","label":"Kimonos"},"modality":{"id":2,"slug":"jiu-jitsu","label":"Jiu-Jitsu"},"brand":{"id":4,"slug":"itg","label":"In The Guard"},"audience":"kids","price":249.9,"currency":"BRL","availability":"available","variants":[{"colorId":3,"colorLabel":"Azul","images":["/v1/storefront/catalog/images/3a82ed3f-a5f8-4088-9874-9e511c41f14b","/catalog/a.jpg","//bad.test/a.jpg","javascript:bad"],"offers":[{"publicOfferId":"{{offer}}","sizeId":11,"sizeLabel":"A1","unitPrice":249.9,"currency":"BRL","availability":"available"}]}]}],"page":1,"pageSize":24,"totalCount":1,"totalPages":1}""";
        var client = Create(HttpStatusCode.OK, json);
        var result = await client.GetCatalogAsync(new CatalogQuery("kimono azul", 7, 2, 4, 11, 3, true, 1));
        var product = Assert.Single(result.Items);
        Assert.Equal(7, product.Category!.Id);
        Assert.Equal(Guid.Parse(offer), Assert.Single(product.Variants[0].Offers).PublicOfferId);
        Assert.Equal(["Algodão", "Gramatura 400"], product.Details);
        Assert.Equal(PublicCatalogAudience.Kids, product.Audience);
        Assert.Equal(2, product.Imagens.Count);
        Assert.Equal("/v1/storefront/catalog/images/3a82ed3f-a5f8-4088-9874-9e511c41f14b", product.Imagens[0]);
        Assert.Contains("catalog/a.jpg", product.Imagens[1]);
    }

    [Fact]
    public async Task Encodes_listing_query_and_slug_path()
    {
        var handler = new RecordingHandler("[]", HttpStatusCode.OK);
        var client = Create(handler);
        await client.GetCatalogAsync(new CatalogQuery("a b&c", 12, null, null, null, null, null, 2, "price-desc", "kimonos", "jiu-jitsu", "in-the-guard", PublicCatalogAudience.Kids, 100, 500));
        await client.GetProductAsync("slug with space");
        Assert.Contains("search=a%20b%26c", handler.Paths[0]);
        Assert.Contains("categoryId=12", handler.Paths[0]);
        Assert.Contains("pageSize=24", handler.Paths[0]);
        Assert.Contains("category=kimonos", handler.Paths[0]);
        Assert.Contains("modality=jiu-jitsu", handler.Paths[0]);
        Assert.Contains("brand=in-the-guard", handler.Paths[0]);
        Assert.Contains("audience=kids", handler.Paths[0]);
        Assert.Contains("minimumPrice=100", handler.Paths[0]);
        Assert.Contains("maximumPrice=500", handler.Paths[0]);
        Assert.Contains("slug%20with%20space", handler.Paths[1]);
    }

    [Fact]
    public async Task Maps_audience_filters()
    {
        var result = await Create("{\"audiences\":[{\"id\":0,\"slug\":\"kids\",\"label\":\"Infantil\"}]}").GetFiltersAsync();

        var audience = Assert.Single(result!.Audiences);
        Assert.Equal("kids", audience.Slug);
        Assert.Equal("Infantil", audience.Label);
    }

    [Fact]
    public async Task Malformed_null_items_and_delayed_body_are_unavailable()
    {
        Assert.Equal(CatalogLoadState.Unavailable, (await Create("{\"items\":[null]}").GetCatalogAsync(new CatalogQuery(null, null, null, null, null, null, null, 1))).State);
        Assert.Equal(CatalogLoadState.Unavailable, (await Create(new DelayedHandler()).GetCatalogAsync(new CatalogQuery(null, null, null, null, null, null, null, 1))).State);
        Assert.Equal(CatalogLoadState.Unavailable, (await Create(new StalledBodyHandler()).GetCatalogAsync(new CatalogQuery(null, null, null, null, null, null, null, 1))).State);
    }

    [Fact]
    public async Task Quote_serializes_lines_and_maps_response()
    {
        var id = Guid.NewGuid();
        var handler = new RecordingHandler($"{{\"lines\":[{{\"publicOfferId\":\"{id}\",\"quantity\":2,\"slug\":\"kimono\",\"presentation\":\"Azul / A1\",\"colorId\":3,\"colorLabel\":\"Azul\",\"sizeId\":11,\"sizeLabel\":\"A1\",\"unitPrice\":49.75,\"linePrice\":99.5,\"currency\":\"BRL\",\"availability\":\"available\"}}],\"total\":99.5,\"currency\":\"BRL\"}}");
        var client = Create(handler);
        var result = await client.QuoteAsync(new CatalogQuoteRequest([new CatalogQuoteItem(id, 2)]));
        Assert.Equal(CatalogLoadState.Success, result.State);
        Assert.Equal(99.5m, result.Total);
        Assert.Contains("\"lines\"", handler.LastBody);
        Assert.Contains($"\"publicOfferId\":\"{id}\"", handler.LastBody);
        Assert.Contains("\"quantity\":2", handler.LastBody);
        Assert.DoesNotContain("\"items\"", handler.LastBody);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"lines\":[],\"total\":0,\"currency\":\"BRL\"}")]
    public async Task Quote_rejects_missing_or_mismatched_lines(string response)
    {
        var id = Guid.NewGuid();
        var result = await Create(response).QuoteAsync(new CatalogQuoteRequest([new CatalogQuoteItem(id, 1)]));
        Assert.Equal(CatalogLoadState.Unavailable, result.State);
    }

    [Fact]
    public async Task Quote_post_body_timeout_is_unavailable()
    {
        var result = await Create(new StalledBodyHandler()).QuoteAsync(new CatalogQuoteRequest([new CatalogQuoteItem(Guid.NewGuid(), 1)]));
        Assert.Equal(CatalogLoadState.Unavailable, result.State);
    }

    [Fact]
    public async Task Quote_rejects_duplicate_omitted_and_invalid_price_lines()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var duplicate = $$"""{"lines":[{"publicOfferId":"{{first}}","quantity":1,"unitPrice":10,"linePrice":10,"currency":"BRL","availability":"available"},{"publicOfferId":"{{first}}","quantity":1,"unitPrice":10,"linePrice":10,"currency":"BRL","availability":"available"}],"total":20,"currency":"BRL"}""";
        var request = new CatalogQuoteRequest([new CatalogQuoteItem(first, 1), new CatalogQuoteItem(second, 1)]);

        Assert.Equal(CatalogLoadState.Unavailable, (await Create(duplicate).QuoteAsync(request)).State);

        var zeroPrice = $$"""{"lines":[{"publicOfferId":"{{first}}","quantity":1,"unitPrice":0,"linePrice":0,"currency":"BRL","availability":"available"}],"total":0,"currency":"BRL"}""";
        Assert.Equal(CatalogLoadState.Unavailable, (await Create(zeroPrice).QuoteAsync(new CatalogQuoteRequest([new CatalogQuoteItem(first, 1)]))).State);
    }

    [Fact]
    public async Task Quote_accepts_partial_lines_and_totals_only_available_items()
    {
        var available = Guid.NewGuid();
        var removed = Guid.NewGuid();
        var body = $$"""{"lines":[{"publicOfferId":"{{available}}","quantity":2,"unitPrice":10,"linePrice":20,"currency":"BRL","availability":"available"},{"publicOfferId":"{{removed}}","quantity":3,"availability":"removed"}],"total":20,"currency":"BRL"}""";
        var result = await Create(body).QuoteAsync(new CatalogQuoteRequest([new(available, 2), new(removed, 3)]));
        Assert.Equal(CatalogLoadState.Partial, result.State);
        Assert.Equal(20m, result.Total);
        Assert.Null(result.Lines[1].UnitPrice);
    }

    [Fact]
    public async Task Keeps_catalog_image_paths_relative_for_same_origin_proxying()
    {
        var id = Guid.NewGuid();
        var image = $"/v1/storefront/catalog/images/{id}";
        var body = $$"""{"lines":[{"publicOfferId":"{{id}}","quantity":1,"unitPrice":10,"linePrice":10,"currency":"BRL","availability":"available","imageUrl":"{{image}}"}],"total":10,"currency":"BRL"}""";

        var result = await Create(body).QuoteAsync(new CatalogQuoteRequest([new(id, 1)]));

        Assert.Equal(image, result.Lines[0].ImageUrl);
    }

    [Fact]
    public async Task Quote_rejects_out_of_range_quantity_and_protocol_relative_image()
    {
        var id = Guid.NewGuid();
        var response = $$"""{"lines":[{"publicOfferId":"{{id}}","quantity":11,"unitPrice":10,"linePrice":110,"currency":"BRL","availability":"available","imageUrl":"//cdn.example/x.jpg"}],"total":110,"currency":"BRL"}""";
        Assert.Equal(CatalogLoadState.Unavailable, (await Create(response).QuoteAsync(new CatalogQuoteRequest([new(id, 11)]))).State);
    }

    [Fact]
    public async Task Legacy_images_are_safe_and_null_fields_do_not_throw()
    {
        var json = "[{\"name\":null,\"description\":null,\"colorVariants\":[{\"images\":[\"/img.jpg\",\"images/foo.jpg\",\"  //bad\",\"javascript:bad\",\"https://cdn.example/a.jpg\"]}]}]";
        var result = await Create(HttpStatusCode.OK, json).GetProductsAsync("jiu-jitsu");
        Assert.Equal(CatalogLoadState.Empty, result.State);

        json = "[{\"name\":\"Legacy\",\"colorVariants\":[{\"images\":[\"/img.jpg\",\"images/foo.jpg\",\"  //bad\",\"javascript:bad\",\"https://cdn.example/a.jpg\"]}]}]";
        result = await Create(HttpStatusCode.OK, json).GetProductsAsync("jiu-jitsu");
        Assert.Equal(3, result.Products[0].Imagens.Count);
        Assert.Contains("https://api.example/images/foo.jpg", result.Products[0].Imagens);
    }

    private static ICatalogClient Create(string body) => Create(new RecordingHandler(body, HttpStatusCode.OK));
    private static ICatalogClient Create(HttpStatusCode status, string body) => Create(new RecordingHandler(body, status));
    private static ICatalogClient Create(HttpMessageHandler handler) => new CatalogClient(new HttpClient(handler) { BaseAddress = new Uri("https://catalog.example/") }, Options.Create(new CatalogApiOptions { BaseUrl = "https://api.example", TimeoutSeconds = 1 }), NullLogger<CatalogClient>.Instance);

    private sealed class RecordingHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public string LastBody { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Paths.Add(request.RequestUri!.PathAndQuery); if (request.Content is not null) LastBody = await request.Content.ReadAsStringAsync(cancellationToken); return new HttpResponseMessage(status) { Content = new StringContent(body) }; }
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { await Task.Delay(1500, cancellationToken); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }; }
    }

    private sealed class StalledBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StalledContent() });
    }

    private sealed class StalledContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();
        protected override bool TryComputeLength(out long length) { length = -1; return false; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new StalledStream());
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => 1; public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(_ => 0, cancellationToken));
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
