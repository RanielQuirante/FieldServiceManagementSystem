using System.Net;
using FieldService.Api.Shared.Observability;
using Microsoft.Extensions.Options;

namespace FieldService.Api.Shared.Http;

public sealed class HttpOptionsValidator(IHostEnvironment environment, ILogger<HttpOptionsValidator> logger)
    : IValidateOptions<HttpOptions>
{
    private const string AllowedOriginsKey = "Cors:AllowedOrigins";
    private const string KnownProxiesKey = "ForwardedHeaders:KnownProxies";
    private const string KnownNetworksKey = "ForwardedHeaders:KnownNetworks";

    public ValidateOptionsResult Validate(string? name, HttpOptions options)
    {
        var failures = new List<string>();

        if (options.Cors.AllowedOrigins.Any(origin => !IsOrigin(origin)))
        {
            Fail(failures, AllowedOriginsKey, "each entry must be an absolute http or https origin without a path, and * is not allowed");
        }
        else if (environment.IsProduction()
            && options.Cors.AllowedOrigins.Any(origin => !origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            Fail(failures, AllowedOriginsKey, "each entry must use https in Production");
        }

        if (options.ForwardedHeaders.KnownProxies.Any(proxy => !IPAddress.TryParse(proxy, out _)))
        {
            Fail(failures, KnownProxiesKey, "each entry must be an IP address");
        }

        if (options.ForwardedHeaders.KnownNetworks.Any(network => !System.Net.IPNetwork.TryParse(network, out _)))
        {
            Fail(failures, KnownNetworksKey, "each entry must be a network in CIDR notation");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
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
