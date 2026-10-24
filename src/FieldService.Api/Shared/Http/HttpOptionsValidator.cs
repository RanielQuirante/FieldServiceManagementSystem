using System.Net;
using System.Net.Sockets;
using FieldService.Api.Shared.Observability;
using Microsoft.Extensions.Options;

namespace FieldService.Api.Shared.Http;

public sealed class HttpOptionsValidator(IHostEnvironment environment, ILogger<HttpOptionsValidator> logger)
    : IValidateOptions<HttpOptions>
{
    private const string AllowedOriginsKey = "Cors:AllowedOrigins";
    private const string KnownProxiesKey = "ForwardedHeaders:KnownProxies";
    private const string KnownNetworksKey = "ForwardedHeaders:KnownNetworks";

    private const string AllowedHostsKey = "AllowedHosts";

    private const string TestingEnvironment = "Testing";

    private const string AnyHost = "*";

    public ValidateOptionsResult Validate(string? name, HttpOptions options)
    {
        var failures = new List<string>();

        if (environment.IsProduction() && !ListsOnlyNamedHosts(options.AllowedHosts))
        {
            Fail(failures, AllowedHostsKey, "must list the public host names in Production, and * is not allowed");
        }

        if (options.Cors.AllowedOrigins.Any(origin => !IsOrigin(origin)))
        {
            Fail(failures, AllowedOriginsKey, "each entry must be an absolute http or https origin without a path, and * is not allowed");
        }
        else if (environment.IsProduction()
            && options.Cors.AllowedOrigins.Any(origin => !origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            Fail(failures, AllowedOriginsKey, "each entry must use https in Production");
        }

        if (options.ForwardedHeaders.KnownProxies.Any(proxy => !IsHostAddress(proxy)))
        {
            Fail(
                failures,
                KnownProxiesKey,
                "each entry must be an IP address of one host, with IPv4 in dotted decimal form; unspecified, broadcast and multicast addresses are refused");
        }
        else if (options.ForwardedHeaders.KnownProxies.Length == 0 && !IsLocalEnvironment())
        {
            Fail(
                failures,
                KnownProxiesKey,
                "must list at least one proxy address outside Development and Testing, because client addresses are taken only from a listed proxy");
        }

        if (options.ForwardedHeaders.KnownNetworks.Any(network => !System.Net.IPNetwork.TryParse(network, out _)))
        {
            Fail(failures, KnownNetworksKey, "each entry must be a network in CIDR notation");
        }
        else if (options.ForwardedHeaders.KnownNetworks.Length > 0 && !IsLocalEnvironment())
        {
            Fail(failures, KnownNetworksKey, "must be empty outside Development and Testing, because no network range is trusted");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private bool IsLocalEnvironment()
    {
        return environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironment);
    }

    private static bool IsHostAddress(string? value)
    {
        if (!IPAddress.TryParse(value, out var address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork
            && !string.Equals(address.ToString(), value, StringComparison.Ordinal))
        {
            return false;
        }

        var host = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (host.Equals(IPAddress.Any) || host.Equals(IPAddress.IPv6Any) || host.Equals(IPAddress.Broadcast))
        {
            return false;
        }

        return host.AddressFamily == AddressFamily.InterNetworkV6
            ? !host.IsIPv6Multicast
            : (host.GetAddressBytes()[0] & 0xF0) != 0xE0;
    }

    private static bool ListsOnlyNamedHosts(string? allowedHosts)
    {
        var hosts = (allowedHosts ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return hosts.Length > 0 && !hosts.Contains(AnyHost, StringComparer.Ordinal);
    }

    private void Fail(List<string> failures, string key, string rule)
    {
        ConfigurationLog.ConfigInvalid(logger, key, rule);
        failures.Add($"{key}: {rule}.");
    }

    private static bool IsOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return string.Equals(value, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }
}
