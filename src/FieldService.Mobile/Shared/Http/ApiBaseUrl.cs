using System.Reflection;

namespace FieldService.Mobile.Shared.Http;

public sealed class ApiBaseUrl
{
    public const string MetadataKey = "ApiBaseUrl";

    private const string DebugConfiguration = "Debug";

    private ApiBaseUrl(Uri value)
    {
        Value = value;
    }

    public Uri Value { get; }

    public static ApiBaseUrl FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var value = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == MetadataKey)?
            .Value;
        var configuration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;

        return Create(value, configuration);
    }

    public static ApiBaseUrl Create(string? value, string? configuration)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("ApiBaseUrl is missing or is not an absolute URL.");
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return new ApiBaseUrl(uri);
        }

        var isDebug = string.Equals(configuration, DebugConfiguration, StringComparison.Ordinal);
        if (isDebug && uri.Scheme == Uri.UriSchemeHttp)
        {
            return new ApiBaseUrl(uri);
        }

        throw new InvalidOperationException(isDebug
            ? "ApiBaseUrl must use http or https."
            : "ApiBaseUrl must use https outside Debug builds.");
    }
}
