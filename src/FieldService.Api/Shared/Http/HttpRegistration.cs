using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FieldService.Api.Shared.Http;

public static class HttpRegistration
{
    public const string CorsPolicyName = "web-dashboard";

    public const long DefaultMaxRequestBodySize = 1048576;

    private const string MaxRequestBodySizeKey = "Kestrel:Limits:MaxRequestBodySize";

    public static IServiceCollection AddHttpConventions(this IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        services.Configure<KestrelServerOptions>(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize =
                configuration.GetValue<long?>(MaxRequestBodySizeKey) ?? DefaultMaxRequestBodySize;
        });
        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = false;
            options.Preload = false;
        });

        services.AddSingleton<IValidateOptions<HttpOptions>, HttpOptionsValidator>();
        services.AddOptions<HttpOptions>().Bind(configuration).ValidateOnStart();

        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IOptions<HttpOptions>>((options, http) => AddCorsPolicy(options, http.Value.Cors));
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<HttpOptions>>((options, http) => ConfigureForwardedHeaders(options, http.Value.ForwardedHeaders));
        services.AddRateLimits();

        return services;
    }

    public static void ConfigureJson(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        options.Converters.Add(new UtcInstantJsonConverter());
        options.NumberHandling = JsonNumberHandling.Strict;
        options.RespectNullableAnnotations = true;
        options.AllowDuplicateProperties = false;
        options.RespectRequiredConstructorParameters = true;
        options.MaxDepth = 32;
    }

    private static void AddCorsPolicy(CorsOptions options, HttpOptions.CorsSection cors)
    {
        options.AddPolicy(CorsPolicyName, policy => policy
            .WithOrigins(cors.AllowedOrigins)
            .WithMethods(HttpMethods.Get, HttpMethods.Post, HttpMethods.Put)
            .WithHeaders(HeaderNames.Authorization, HeaderNames.ContentType, CorrelationId.HeaderName, CsrfHeader.Name)
            .WithExposedHeaders(CorrelationId.HeaderName, HeaderNames.Location, HeaderNames.RetryAfter)
            .AllowCredentials()
            .SetPreflightMaxAge(TimeSpan.FromSeconds(600)));
    }

    private static void ConfigureForwardedHeaders(
        ForwardedHeadersOptions options,
        HttpOptions.ForwardedHeadersSection forwardedHeaders)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;

        if (forwardedHeaders.KnownProxies.Length == 0 && forwardedHeaders.KnownNetworks.Length == 0)
        {
            return;
        }

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in forwardedHeaders.KnownProxies)
        {
            if (IPAddress.TryParse(proxy, out var address))
            {
                options.KnownProxies.Add(address);
            }
        }

        foreach (var network in forwardedHeaders.KnownNetworks)
        {
            if (System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                options.KnownIPNetworks.Add(parsed);
            }
        }
    }
}
