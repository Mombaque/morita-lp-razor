using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class PublicAssistantClientTests
{
    [Fact]
    public async Task Create_extracts_credential_and_forwards_only_trusted_relay_headers()
    {
        var token = new string('x', 64);
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, ValidSession(token)));
        var cookies = new StubCookieStore(new(Guid.NewGuid(), new string('c', 64), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30)));
        var client = CreateClient(handler, cookies);

        var result = await client.CreateSessionAsync(new() { AcceptedAiNotice = true, AiNoticeVersion = "public-assistant-v1" });

        Assert.True(result.IsSuccess);
        Assert.Equal(token, result.Value.AccessToken);
        Assert.Null(result.Value.Session.AccessToken);
        Assert.False(handler.Request!.Headers.Contains("X-Assistant-Token"));
        Assert.Equal("proxy-secret", Assert.Single(handler.Request.Headers.GetValues("X-Morita-Proxy-Secret")));
        Assert.True(handler.Request.Headers.Contains("X-Morita-Client-IP"));
    }

    [Fact]
    public async Task Get_forwards_cookie_credential_but_rejects_unexpected_response_credential()
    {
        var credential = new string('c', 64);
        var response = ValidSession(new string('u', 64));
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, response));
        var client = CreateClient(handler, new StubCookieStore(new(Guid.NewGuid(), credential, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30))));

        var result = await client.GetSessionAsync();

        Assert.Equal(PublicAssistantFailureKind.Malformed, result.Failure);
        Assert.Equal(credential, Assert.Single(handler.Request!.Headers.GetValues("X-Assistant-Token")));
    }

    [Fact]
    public async Task Message_rejects_configured_card_with_published_only_fields()
    {
        var turn = new PublicAssistantTurn
        {
            Message = new() { Role = "assistant", Content = "Encontrei uma opção." },
            CatalogProducts = [new()
            {
                Slug = "produto",
                Name = "Produto",
                Source = "AssistantConfigured",
                Price = 10,
                Currency = "BRL"
            }]
        };
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, turn));
        var client = CreateClient(handler, ValidCookies());

        var result = await client.SendMessageAsync(new() { ClientMessageId = Guid.NewGuid(), Text = "produto" });

        Assert.Equal(PublicAssistantFailureKind.Malformed, result.Failure);
    }

    [Fact]
    public async Task Timeout_is_mapped_to_safe_retryable_failure()
    {
        var handler = new RecordingHandler(_ => throw new TaskCanceledException());
        var client = CreateClient(handler, ValidCookies());

        var result = await client.GetSessionAsync();

        Assert.Equal(PublicAssistantFailureKind.Timeout, result.Failure);
        Assert.DoesNotContain("exception", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Malformed_json_is_rejected()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{", Encoding.UTF8, "application/json") });
        var client = CreateClient(handler, ValidCookies());

        var result = await client.GetSessionAsync();

        Assert.Equal(PublicAssistantFailureKind.Malformed, result.Failure);
    }

    [Fact]
    public async Task Validation_errors_are_mapped_without_forwarding_raw_response_objects()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.UnprocessableEntity, new List<string> { "Revise o WhatsApp." }));
        var client = CreateClient(handler, ValidCookies());

        var result = await client.SubmitAsync(new(), new string('i', 64));

        Assert.Equal(PublicAssistantFailureKind.Validation, result.Failure);
        Assert.Equal("Revise o WhatsApp.", result.Message);
    }

    private static PublicAssistantClient CreateClient(HttpMessageHandler handler, IPublicAssistantCookieStore cookies)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        return new PublicAssistantClient(
            httpClient,
            Options.Create(new CatalogApiOptions { BaseUrl = "https://api.test", ProxySecret = "proxy-secret" }),
            Options.Create(new StorefrontOptions { PublicAssistantTimeoutSeconds = 25 }),
            new HttpContextAccessor { HttpContext = context },
            new TestEnvironment(),
            cookies,
            NullLogger<PublicAssistantClient>.Instance);
    }

    private static StubCookieStore ValidCookies()
    {
        var now = DateTimeOffset.UtcNow;
        return new(new(Guid.NewGuid(), new string('c', 64), now, now.AddDays(30)));
    }

    private static PublicAssistantSession ValidSession(string? accessToken = null) => new()
    {
        AccessToken = accessToken,
        PublicId = Guid.NewGuid(),
        ExpiresAt = DateTime.UtcNow.AddDays(30),
        Status = "Active",
        Messages = [new() { Role = "assistant", Content = "Olá" }]
    };

    private static HttpResponseMessage Json(HttpStatusCode statusCode, object value) => new(statusCode) { Content = JsonContent.Create(value) };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(response(request));
        }
    }

    private sealed class StubCookieStore(PublicAssistantCredentials? credentials) : IPublicAssistantCookieStore
    {
        public PublicAssistantCredentials? Read() => credentials;
        public bool Write(PublicAssistantCredentials value) => true;
        public bool Refresh(DateTimeOffset expiresAt) => true;
        public void Clear() { }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "E2E";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
