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
        !options.UseRelayForCustomerRequests ||
        !string.IsNullOrWhiteSpace(builder.Configuration[$"{CatalogApiOptions.SectionName}:ProxySecret"]),
        "CatalogApi:ProxySecret is required when the customer-request relay is enabled.")
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
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
builder.Services.AddSingleton<ProductService>();
builder.Services.AddScoped<ICartCookieStore, CartCookieStore>();
builder.Services.AddScoped<ICheckoutDraftCookieStore, CheckoutDraftCookieStore>();
builder.Services.AddScoped<ICheckoutAccessCookieStore, CheckoutAccessCookieStore>();
builder.Services.AddScoped<IPaymentAttemptCookieStore, PaymentAttemptCookieStore>();
builder.Services.AddScoped<IOrderAccessCookieStore, OrderAccessCookieStore>();
builder.Services.AddSingleton<CheckoutRateLimiter>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddHttpClient<ICatalogClient, CatalogClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<CatalogApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<ICheckoutClient, CheckoutClient>((serviceProvider, client) =>
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

app.MapRazorPages();

app.Run();

public partial class Program { }
