using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
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

public sealed class PublicAssistantRelayTests
{
    [Fact]
    public async Task Disabled_assistant_does_not_render_or_accept_requests()
    {
        var assistant = new StubAssistantClient();
        var cookies = new StubCookieStore();
        using var factory = CreateFactory(false, assistant, cookies);
        using var client = factory.CreateClient();

        var page = await (await client.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("public-assistant-root", page);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/assistant/session")).StatusCode);
    }

    [Theory]
    [InlineData("/assistant/session")]
    [InlineData("/assistant/message")]
    [InlineData("/assistant/submit")]
    [InlineData("/assistant/reset")]
    public async Task Unsafe_assistant_endpoints_require_antiforgery_before_dispatch(string path)
    {
        var assistant = new StubAssistantClient();
        using var factory = CreateFactory(true, assistant, new StubCookieStore());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(path, ValidSessionPayload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, assistant.CreateCalls + assistant.MessageCalls + assistant.SubmitCalls + assistant.CloseCalls);
    }

    [Fact]
    public async Task Session_creation_stores_token_server_side_and_never_returns_it_to_browser()
    {
        var assistant = new StubAssistantClient();
        var cookies = new StubCookieStore();
        using var factory = CreateFactory(true, assistant, cookies);
        using var client = factory.CreateClient();
        using var request = JsonRequest(HttpMethod.Post, "/assistant/session", ValidSessionPayload, await GetToken(client));

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, assistant.CreateCalls);
        Assert.NotNull(cookies.Written);
        Assert.Equal(assistant.AccessToken, cookies.Written.AccessToken);
        Assert.DoesNotContain(assistant.AccessToken, body);
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_forwards_idempotency_key_and_refreshes_cookie()
    {
        var assistant = new StubAssistantClient();
        var cookies = new StubCookieStore();
        using var factory = CreateFactory(true, assistant, cookies);
        using var client = factory.CreateClient();
        var idempotencyKey = new string('a', 64);
        using var request = JsonRequest(HttpMethod.Post, "/assistant/submit", new
        {
            confirmationToken = new string('t', 40),
            expectedRevision = 2,
            customerName = "Maria",
            customerWhatsapp = "15999999999",
            acceptedPrivacyPolicy = true
        }, await GetToken(client));
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(idempotencyKey, assistant.SubmittedIdempotencyKey);
        Assert.Equal(1, cookies.RefreshCalls);
    }

    [Fact]
    public async Task Expired_message_clears_protected_cookie_and_returns_gone()
    {
        var assistant = new StubAssistantClient { MessageResult = new(PublicAssistantFailureKind.Expired, null) };
        var cookies = new StubCookieStore();
        using var factory = CreateFactory(true, assistant, cookies);
        using var client = factory.CreateClient();
        using var request = JsonRequest(HttpMethod.Post, "/assistant/message", new { clientMessageId = Guid.NewGuid(), expectedRevision = 0, text = "Preciso de um kimono" }, await GetToken(client));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal(1, cookies.ClearCalls);
    }

    [Fact]
    public async Task Reset_closes_api_session_and_clears_cookie()
    {
        var assistant = new StubAssistantClient();
        var cookies = new StubCookieStore();
        using var factory = CreateFactory(true, assistant, cookies);
        using var client = factory.CreateClient();
        using var request = JsonRequest(HttpMethod.Post, "/assistant/reset", new { }, await GetToken(client));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, assistant.CloseCalls);
        Assert.Equal(1, cookies.ClearCalls);
    }

    [Fact]
    public async Task Failed_reset_preserves_cookie_and_returns_retryable_failure()
    {
        var assistant = new StubAssistantClient { CloseResult = new(PublicAssistantFailureKind.Unavailable) };
        var cookies = new StubCookieStore();
        using var factory = CreateFactory(true, assistant, cookies);
        using var client = factory.CreateClient();
        using var request = JsonRequest(HttpMethod.Post, "/assistant/reset", new { }, await GetToken(client));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, assistant.CloseCalls);
        Assert.Equal(0, cookies.ClearCalls);
    }

    [Fact]
    public async Task Oversized_chunked_body_is_rejected_without_dispatch()
    {
        var assistant = new StubAssistantClient();
        using var factory = CreateFactory(true, assistant, new StubCookieStore());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/assistant/message")
        {
            Content = new ChunkedContent(new byte[(16 * 1024) + 1])
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("RequestVerificationToken", await GetToken(client));

        var response = await client.SendAsync(request);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
        Assert.Equal(0, assistant.MessageCalls);
    }

    private static WebApplicationFactory<Program> CreateFactory(bool enabled, StubAssistantClient assistant, StubCookieStore cookies) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.UseSetting("Storefront:PublicAssistantEnabled", enabled.ToString());
            builder.UseSetting("CatalogApi:ProxySecret", "relay-secret");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPublicAssistantClient>();
                services.RemoveAll<IPublicAssistantCookieStore>();
                services.AddSingleton<IPublicAssistantClient>(assistant);
                services.AddSingleton<IPublicAssistantCookieStore>(cookies);
            });
        });

    private static async Task<string> GetToken(HttpClient client)
    {
        var body = await (await client.GetAsync("/")).Content.ReadAsStringAsync();
        return WebUtility.HtmlDecode(Regex.Match(body, "name=\"request-verification-token\" content=\"([^\"]+)\"").Groups[1].Value);
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, object payload, string token)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("RequestVerificationToken", token);
        return request;
    }

    private static readonly object ValidSessionPayload = new
    {
        acceptedAiNotice = true,
        aiNoticeVersion = "public-assistant-v1",
        landingPage = "/products",
        campaign = "test",
        initialProductSlug = (string?)null,
        website = ""
    };

    private sealed class StubAssistantClient : IPublicAssistantClient
    {
        public string AccessToken { get; } = new('x', 64);
        public int CreateCalls { get; private set; }
        public int MessageCalls { get; private set; }
        public int SubmitCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public string? SubmittedIdempotencyKey { get; private set; }
        public PublicAssistantResult<PublicAssistantTurn> MessageResult { get; set; } = new(PublicAssistantFailureKind.None, new PublicAssistantTurn
        {
            Message = new() { Role = "assistant", Content = "Como posso ajudar?" }
        });
        public PublicAssistantActionResult CloseResult { get; set; } = new(PublicAssistantFailureKind.None);

        public Task<PublicAssistantResult<(PublicAssistantSession Session, string AccessToken)>> CreateSessionAsync(CreatePublicAssistantSessionRequest request, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            var session = new PublicAssistantSession
            {
                PublicId = Guid.NewGuid(),
                ExpiresAt = DateTime.UtcNow.AddDays(30),
                Status = "Active",
                Messages = [new() { Role = "assistant", Content = "Olá" }]
            };
            return Task.FromResult(new PublicAssistantResult<(PublicAssistantSession, string)>(PublicAssistantFailureKind.None, (session, AccessToken)));
        }

        public Task<PublicAssistantResult<PublicAssistantSession>> GetSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PublicAssistantResult<PublicAssistantSession>(PublicAssistantFailureKind.NotFound, null));

        public Task<PublicAssistantResult<PublicAssistantTurn>> SendMessageAsync(PublicAssistantMessageRequest request, CancellationToken cancellationToken = default)
        {
            MessageCalls++;
            return Task.FromResult(MessageResult);
        }

        public Task<PublicAssistantResult<PublicAssistantSubmission>> SubmitAsync(PublicAssistantSubmitRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            SubmitCalls++;
            SubmittedIdempotencyKey = idempotencyKey;
            return Task.FromResult(new PublicAssistantResult<PublicAssistantSubmission>(PublicAssistantFailureKind.None, new() { CustomerProductRequestId = 42, Received = true }));
        }

        public Task<PublicAssistantActionResult> CloseAsync(CancellationToken cancellationToken = default)
        {
            CloseCalls++;
            return Task.FromResult(CloseResult);
        }
    }

    private sealed class StubCookieStore : IPublicAssistantCookieStore
    {
        public PublicAssistantCredentials? Written { get; private set; }
        public int RefreshCalls { get; private set; }
        public int ClearCalls { get; private set; }
        public PublicAssistantCredentials? Read() => Written;
        public bool Write(PublicAssistantCredentials credentials) { Written = credentials; return true; }
        public bool Refresh(DateTimeOffset expiresAt) { RefreshCalls++; return true; }
        public void Clear() => ClearCalls++;
    }

    private sealed class ChunkedContent(byte[] content) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(content).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
