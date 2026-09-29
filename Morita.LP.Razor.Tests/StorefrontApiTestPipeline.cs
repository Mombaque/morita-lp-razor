using System;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Tests;

internal static class StorefrontApiTestPipeline
{
    public static HttpClient CreateClient(HttpMessageHandler inner, CatalogApiOptions options, HttpContext? context, string baseAddress = "https://api.test/") =>
        new(CreateHandler(inner, options, context)) { BaseAddress = new Uri(baseAddress) };

    public static StorefrontApiHeadersHandler CreateHandler(HttpMessageHandler inner, CatalogApiOptions options, HttpContext? context, string environmentName = "E2E") =>
        new(new HttpContextAccessor { HttpContext = context }, Options.Create(options), new PipelineEnvironment(environmentName)) { InnerHandler = inner };

    private sealed class PipelineEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
