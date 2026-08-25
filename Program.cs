using System.Net.Http.Headers;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddSingleton<ProductService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddOptions<DeliveryTrackingOptions>()
    .Bind(builder.Configuration.GetSection(DeliveryTrackingOptions.Section))
    .PostConfigure(options =>
    {
        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
            options.ApiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(options.TimeZoneId))
            options.TimeZoneId = DeliveryTrackingOptions.DefaultTimeZoneId;
    })
    .Validate(options => DeliveryTrackingOptions.IsValidApiBaseUrl(options.ApiBaseUrl), "DeliveryTracking:ApiBaseUrl must be an absolute HTTP(S) URL.")
    .Validate(options => DeliveryTrackingOptions.IsValidPublicDeliveryPath(options.PublicDeliveryPath), "DeliveryTracking:PublicDeliveryPath must be a relative path containing exactly {publicToken}.")
    .Validate(options => DeliveryTrackingOptions.IsValidTimeZoneId(options.TimeZoneId), "DeliveryTracking:TimeZoneId must identify an installed time zone.")
    .Validate(options => builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(options.GoogleReviewUrl), "DeliveryTracking:GoogleReviewUrl is required outside Development.")
    .ValidateOnStart();
builder.Services.AddHttpClient("public-delivery", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DeliveryTrackingOptions>>().Value;
    client.BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});
builder.Services.AddScoped<IPublicDeliveryClient, PublicDeliveryClient>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();

public partial class Program { }
