namespace FieldService.Api.Shared.Http;

public sealed class HttpOptions
{
    public string? AllowedHosts { get; set; }

    public CorsSection Cors { get; set; } = new();

    public ForwardedHeadersSection ForwardedHeaders { get; set; } = new();

    public sealed class CorsSection
    {
        public string[] AllowedOrigins { get; set; } = [];
    }

    public sealed class ForwardedHeadersSection
    {
        public string[] KnownProxies { get; set; } = [];

        public string[] KnownNetworks { get; set; } = [];
    }
}
