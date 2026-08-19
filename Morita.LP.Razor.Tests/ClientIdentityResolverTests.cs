using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class ClientIdentityResolverTests
{
    [Fact]
    public void Production_uses_the_canonical_fly_client_ip()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        context.Request.Headers["Fly-Client-IP"] = "::ffff:203.0.113.8";

        var identity = ClientIdentityResolver.Resolve(context, new TestEnvironment(Environments.Production));

        Assert.Equal("203.0.113.8", identity);
    }

    [Fact]
    public void Non_production_ignores_fly_headers()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers["Fly-Client-IP"] = "203.0.113.8";

        var identity = ClientIdentityResolver.Resolve(context, new TestEnvironment("E2E"));

        Assert.Equal("127.0.0.1", identity);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Morita.LP.Razor.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
