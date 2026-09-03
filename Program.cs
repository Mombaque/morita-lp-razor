using System.Text.Json;
using System.Text;
using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<StorefrontOptions>()
    .BindConfiguration(StorefrontOptions.SectionName)
    .Validate(options =>
        string.Equals(options.ProductSource, "Legacy", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(options.ProductSource, "Api", StringComparison.OrdinalIgnoreCase),
        "Storefront:ProductSource must be Legacy or Api.")
    .Validate(options =>
        !options.UseRelayForCustomerRequests && !options.PublicAssistantEnabled ||
        !string.IsNullOrWhiteSpace(builder.Configuration[$"{CatalogApiOptions.SectionName}:ProxySecret"]),
        "CatalogApi:ProxySecret is required when a public relay is enabled.")
    .Validate(options => options.PublicAssistantTimeoutSeconds is >= 5 and <= 60,
        "Storefront:PublicAssistantTimeoutSeconds must be between 5 and 60.")
    .Validate(options =>
        !options.PublicAssistantEnabled ||
        builder.Environment.IsDevelopment() ||
        builder.Environment.IsEnvironment("E2E") ||
        !string.IsNullOrWhiteSpace(options.DataProtectionKeyDirectory) && Path.IsPathRooted(options.DataProtectionKeyDirectory),
        "Storefront:DataProtectionKeyDirectory must be an absolute durable-storage path when the public assistant is enabled.")
    .ValidateOnStart();
builder.Services.AddOptions<CatalogApiOptions>().BindConfiguration(CatalogApiOptions.SectionName).PostConfigure(options =>
{
    if (string.IsNullOrWhiteSpace(options.BaseUrl))
        options.BaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://morita-api.fly.dev";
    options.BaseUrl = options.BaseUrl.Trim();
}).Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
    "CatalogApi:BaseUrl must be an absolute URL.")
    .Validate(options =>
        builder.Environment.IsDevelopment() ||
        builder.Environment.IsEnvironment("E2E") ||
        Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
        "CatalogApi:BaseUrl must use HTTPS outside Development and E2E.")
    .ValidateOnStart();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("fdaa::"), 16));
});
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Morita.LP.Razor");
var keyDirectory = builder.Configuration[$"{StorefrontOptions.SectionName}:DataProtectionKeyDirectory"];
if (!string.IsNullOrWhiteSpace(keyDirectory))
    dataProtection.PersistKeysToFileSystem(Directory.CreateDirectory(keyDirectory));
builder.Services.AddSingleton<ProductService>();
builder.Services.AddScoped<ICartCookieStore, CartCookieStore>();
builder.Services.AddScoped<ICheckoutDraftCookieStore, CheckoutDraftCookieStore>();
builder.Services.AddScoped<ICheckoutAccessCookieStore, CheckoutAccessCookieStore>();
builder.Services.AddScoped<IPaymentAttemptCookieStore, PaymentAttemptCookieStore>();
builder.Services.AddScoped<IOrderAccessCookieStore, OrderAccessCookieStore>();
builder.Services.AddScoped<ICustomerAccountCookieStore, CustomerAccountCookieStore>();
builder.Services.AddScoped<IPublicAssistantCookieStore, PublicAssistantCookieStore>();
builder.Services.AddSingleton<CheckoutRateLimiter>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddHttpClient<ICatalogClient, CatalogClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<CatalogApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient("catalog-image-proxy", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<CatalogApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 30));
});
builder.Services.AddHttpClient<ICheckoutClient, CheckoutClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<CatalogApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<IPublicAssistantClient, PublicAssistantClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<CatalogApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<IOrderClient, OrderClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<CatalogApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<ICustomerAccountClient, CustomerAccountClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<CatalogApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient("customer-request", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<CatalogApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("customer-request-relay", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientIdentityResolver.Resolve(httpContext, builder.Environment),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 12,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.AddPolicy("public-assistant-relay", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientIdentityResolver.Resolve(httpContext, builder.Environment),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (!app.Environment.IsEnvironment("E2E"))
    app.UseWhen(context => !context.Request.Path.Equals("/health"), branch => branch.UseHttpsRedirection());
app.UseStaticFiles();

app.UseRouting();
app.UseAntiforgery();
app.UseRateLimiter();

app.UseAuthorization();

app.MapGet("/JiuJitsu", (HttpContext context) => Results.Redirect("/jiu-jitsu" + context.Request.QueryString, permanent: true));
app.MapGet("/MuayThai", (HttpContext context) => Results.Redirect("/muay-thai" + context.Request.QueryString, permanent: true));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/v1/storefront/catalog/images/{imageId:guid}", async (Guid imageId, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    try
    {
        using var response = await clients.CreateClient("catalog-image-proxy").GetAsync(
            $"v1/storefront/catalog/images/{imageId}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return Results.NotFound();
        if (!response.IsSuccessStatusCode)
            return Results.StatusCode(StatusCodes.Status502BadGateway);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType is not ("image/jpeg" or "image/png" or "image/webp"))
            return Results.StatusCode(StatusCodes.Status502BadGateway);

        return Results.File(await response.Content.ReadAsByteArrayAsync(cancellationToken), contentType);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException)
    {
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }
});
app.MapPost("/customer-product-request", async (HttpContext context, IHttpClientFactory clients, IOptions<CatalogApiOptions> catalogOptions, IOptions<StorefrontOptions> storefrontOptions, IAntiforgery antiforgery, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("CustomerProductRequestRelay");
    if (!storefrontOptions.Value.UseRelayForCustomerRequests)
        return Results.NotFound();

    try
    {
        await antiforgery.ValidateRequestAsync(context);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest(new { error = "invalid_request" });
    }

    const int maximumRequestBytes = 32 * 1024;
    if (context.Request.ContentLength is > maximumRequestBytes)
        return Results.BadRequest(new { error = "request_too_large" });

    var bodyBytes = new byte[maximumRequestBytes + 1];
    var bytesRead = 0;
    while (bytesRead < bodyBytes.Length)
    {
        var read = await context.Request.Body.ReadAsync(bodyBytes.AsMemory(bytesRead), cancellationToken);
        if (read == 0)
            break;
        bytesRead += read;
    }

    if (bytesRead > maximumRequestBytes)
        return Results.BadRequest(new { error = "request_too_large" });

    var body = Encoding.UTF8.GetString(bodyBytes, 0, bytesRead);
    if (string.IsNullOrWhiteSpace(body))
        return Results.BadRequest(new { error = "invalid_request" });

    try
    {
        var payload = JsonSerializer.Deserialize<CustomerProductRequestPayload>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (payload is null || !payload.AcceptedPrivacyPolicy || payload.Items is null || payload.Items.Count == 0)
            return Results.BadRequest(new { error = "invalid_request" });
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "invalid_request" });
    }

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(8));
    using var request = new HttpRequestMessage(HttpMethod.Post, "v1/CustomerProductRequest")
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };
    request.Headers.TryAddWithoutValidation("X-Morita-Client-IP", ClientIdentityResolver.Resolve(context, app.Environment));
    if (!string.IsNullOrWhiteSpace(catalogOptions.Value.ProxySecret))
        request.Headers.TryAddWithoutValidation("X-Morita-Proxy-Secret", catalogOptions.Value.ProxySecret);

    try
    {
        using var response = await clients.CreateClient("customer-request").SendAsync(request, timeout.Token);
        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
            return Results.StatusCode((int)response.StatusCode);
        if (!response.IsSuccessStatusCode)
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        return Results.Content(await response.Content.ReadAsStringAsync(timeout.Token), "application/json", System.Text.Encoding.UTF8, (int)response.StatusCode);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        logger.LogWarning("Customer product request relay timed out");
        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException exception)
    {
        logger.LogWarning(exception, "Customer product request relay unavailable");
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }
}).RequireRateLimiting("customer-request-relay").WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(32 * 1024));

app.MapPost("/assistant/session", async (HttpContext context, IPublicAssistantClient assistant, IPublicAssistantCookieStore cookies, IOptions<StorefrontOptions> storefrontOptions, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
{
    if (!storefrontOptions.Value.PublicAssistantEnabled) return Results.NotFound();
    if (!await ValidateAssistantRequest(context, antiforgery)) return Results.BadRequest(new { error = "invalid_request" });
    var payload = await ReadAssistantPayload<CreatePublicAssistantSessionRequest>(context, cancellationToken);
    if (payload is null || !ValidSessionPayload(payload)) return Results.BadRequest(new { error = "invalid_request" });
    var result = await assistant.CreateSessionAsync(payload, cancellationToken);
    if (!result.IsSuccess) return AssistantFailure(result.Failure, result.Message);
    var created = result.Value!;
    if (!cookies.Write(new PublicAssistantCredentials(created.Session.PublicId, created.AccessToken, DateTimeOffset.UtcNow, created.Session.ExpiresAt))) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    return Results.Json(created.Session);
}).RequireRateLimiting("public-assistant-relay").WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(16 * 1024));

app.MapGet("/assistant/session", async (IPublicAssistantClient assistant, IPublicAssistantCookieStore cookies, IOptions<StorefrontOptions> storefrontOptions, CancellationToken cancellationToken) =>
{
    if (!storefrontOptions.Value.PublicAssistantEnabled) return Results.NotFound();
    var result = await assistant.GetSessionAsync(cancellationToken);
    if (result.Failure is PublicAssistantFailureKind.NotFound or PublicAssistantFailureKind.Expired) cookies.Clear();
    if (result.IsSuccess && result.Value is not null) cookies.Refresh(result.Value.ExpiresAt);
    return result.IsSuccess ? Results.Json(result.Value) : AssistantFailure(result.Failure, result.Message);
}).RequireRateLimiting("public-assistant-relay");

app.MapPost("/assistant/message", async (HttpContext context, IPublicAssistantClient assistant, IPublicAssistantCookieStore cookies, IOptions<StorefrontOptions> storefrontOptions, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
{
    if (!storefrontOptions.Value.PublicAssistantEnabled) return Results.NotFound();
    if (!await ValidateAssistantRequest(context, antiforgery)) return Results.BadRequest(new { error = "invalid_request" });
    var payload = await ReadAssistantPayload<PublicAssistantMessageRequest>(context, cancellationToken);
    if (payload is null || payload.ClientMessageId == Guid.Empty || payload.ExpectedRevision < 0 || string.IsNullOrWhiteSpace(payload.Text) || payload.Text.Length > 1000) return Results.BadRequest(new { error = "invalid_request" });
    var result = await assistant.SendMessageAsync(payload, cancellationToken);
    if (result.Failure is PublicAssistantFailureKind.NotFound or PublicAssistantFailureKind.Expired) cookies.Clear();
    if (result.IsSuccess) cookies.Refresh(DateTimeOffset.UtcNow.AddDays(30));
    return result.IsSuccess ? Results.Json(result.Value) : AssistantFailure(result.Failure, result.Message);
}).RequireRateLimiting("public-assistant-relay").WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(16 * 1024));

app.MapPost("/assistant/submit", async (HttpContext context, IPublicAssistantClient assistant, IPublicAssistantCookieStore cookies, IOptions<StorefrontOptions> storefrontOptions, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
{
    if (!storefrontOptions.Value.PublicAssistantEnabled) return Results.NotFound();
    if (!await ValidateAssistantRequest(context, antiforgery)) return Results.BadRequest(new { error = "invalid_request" });
    var payload = await ReadAssistantPayload<PublicAssistantSubmitRequest>(context, cancellationToken);
    if (payload is null || payload.ExpectedRevision < 0 || string.IsNullOrWhiteSpace(payload.ConfirmationToken) || payload.ConfirmationToken.Length > 500 || string.IsNullOrWhiteSpace(payload.CustomerName) || payload.CustomerName.Length > 120 || string.IsNullOrWhiteSpace(payload.CustomerWhatsapp) || payload.CustomerWhatsapp.Length > 40 || !payload.AcceptedPrivacyPolicy) return Results.BadRequest(new { error = "invalid_request" });
    var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
    if (idempotencyKey.Length is < 32 or > 200) return Results.UnprocessableEntity(new[] { "A chave de idempotência é inválida." });
    var result = await assistant.SubmitAsync(payload, idempotencyKey, cancellationToken);
    if (result.Failure is PublicAssistantFailureKind.NotFound or PublicAssistantFailureKind.Expired) cookies.Clear();
    if (result.IsSuccess) cookies.Refresh(DateTimeOffset.UtcNow.AddDays(30));
    return result.IsSuccess ? Results.Json(result.Value) : AssistantFailure(result.Failure, result.Message);
}).RequireRateLimiting("public-assistant-relay").WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(16 * 1024));

app.MapPost("/assistant/reset", async (HttpContext context, IPublicAssistantClient assistant, IPublicAssistantCookieStore cookies, IOptions<StorefrontOptions> storefrontOptions, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
{
    if (!await ValidateAssistantRequest(context, antiforgery)) return Results.BadRequest(new { error = "invalid_request" });
    if (!storefrontOptions.Value.PublicAssistantEnabled)
    {
        cookies.Clear();
        return Results.NoContent();
    }
    var result = await assistant.CloseAsync(cancellationToken);
    if (result.IsSuccess || result.Failure is PublicAssistantFailureKind.NotFound or PublicAssistantFailureKind.Expired)
    {
        cookies.Clear();
        return Results.NoContent();
    }
    return AssistantFailure(result.Failure, result.Message);
}).RequireRateLimiting("public-assistant-relay").WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(1024));

static async Task<bool> ValidateAssistantRequest(HttpContext context, IAntiforgery antiforgery)
{
    try { await antiforgery.ValidateRequestAsync(context); return true; }
    catch (AntiforgeryValidationException) { return false; }
}

static async Task<T?> ReadAssistantPayload<T>(HttpContext context, CancellationToken cancellationToken) where T : class
{
    const int maximumRequestBytes = 16 * 1024;
    if (context.Request.ContentLength is 0 or > maximumRequestBytes) return null;
    var bodyBytes = new byte[maximumRequestBytes + 1];
    var bytesRead = 0;
    while (bytesRead < bodyBytes.Length)
    {
        var read = await context.Request.Body.ReadAsync(bodyBytes.AsMemory(bytesRead), cancellationToken);
        if (read == 0) break;
        bytesRead += read;
    }
    if (bytesRead == 0 || bytesRead > maximumRequestBytes) return null;
    try { return JsonSerializer.Deserialize<T>(bodyBytes.AsSpan(0, bytesRead), new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
    catch (JsonException) { return null; }
}

static bool ValidSessionPayload(CreatePublicAssistantSessionRequest payload) =>
    payload.AcceptedAiNotice &&
    string.Equals(payload.AiNoticeVersion, "public-assistant-v1", StringComparison.Ordinal) &&
    (payload.LandingPage is null || payload.LandingPage.Length <= 500) &&
    (payload.Campaign is null || payload.Campaign.Length <= 150) &&
    (payload.InitialProductSlug is null || payload.InitialProductSlug.Length <= 200) &&
    (payload.Website is null || payload.Website.Length <= 200);

static IResult AssistantFailure(PublicAssistantFailureKind failure, string? message) => failure switch
{
    PublicAssistantFailureKind.NotFound => Results.NotFound(),
    PublicAssistantFailureKind.Conflict => Results.Conflict(new[] { message ?? "A conversa foi atualizada." }),
    PublicAssistantFailureKind.Expired => Results.StatusCode(StatusCodes.Status410Gone),
    PublicAssistantFailureKind.Validation => Results.UnprocessableEntity(new[] { message ?? "Revise os dados informados." }),
    PublicAssistantFailureKind.RateLimited => Results.StatusCode(StatusCodes.Status429TooManyRequests),
    PublicAssistantFailureKind.Unavailable or PublicAssistantFailureKind.Timeout => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
    _ => Results.StatusCode(StatusCodes.Status502BadGateway)
};

app.MapRazorPages();

app.Run();

public partial class Program { }
