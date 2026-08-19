using System.Net;

namespace Morita.LP.Razor.Services;

public static class ClientIdentityResolver
{
    public static string Resolve(HttpContext context, IHostEnvironment environment)
    {
        if (environment.IsProduction() && IPAddress.TryParse(context.Request.Headers["Fly-Client-IP"].FirstOrDefault(), out var flyIp))
            return Canonicalize(flyIp) ?? "unknown";

        return Canonicalize(context.Connection.RemoteIpAddress) ?? "unknown";
    }

    private static string? Canonicalize(IPAddress? address)
    {
        if (address is null)
            return null;
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
    }
}
