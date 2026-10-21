using System.Security.Claims;
using System.Threading.RateLimiting;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldService.Api.Shared.Http;

public static class RateLimitPolicies
{
    public const string SignIn = "sign-in";

    public const string Refresh = "refresh";

    public const string Global = "global";

    public const int SignInLimit = 10;

    public const int RefreshLimit = 30;

    public const int UserLimit = 300;

    public const int AnonymousLimit = 60;

    public const string UserPartitionKind = "user";

    public const string AddressPartitionKind = "ip";

    public const string UnknownAddress = "unknown";

    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private const string ApiPathPrefix = "/api";

    public static IServiceCollection AddRateLimits(this IServiceCollection services)
    {
        services.AddSingleton<RateLimitRejections>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(GlobalPartition);
            options.OnRejected = (context, cancellationToken) =>
                RateLimitRejections.WriteAsync(context, Global, cancellationToken);
            options.AddPolicy(SignIn, new ClientAddressPolicy(SignIn, SignInLimit));
            options.AddPolicy(Refresh, new ClientAddressPolicy(Refresh, RefreshLimit));
        });

        return services;
    }

    public static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? UnknownAddress;

    public static string? AuthenticatedUserId(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirstValue(AccessTokenClaims.Subject)
            : null;

    private static RateLimitPartition<string> GlobalPartition(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(ApiPathPrefix))
        {
            return RateLimitPartition.GetNoLimiter(string.Empty);
        }

        return AuthenticatedUserId(context) is { } userId
            ? RateLimitPartition.GetFixedWindowLimiter($"{UserPartitionKind}:{userId}", _ => FixedWindow(UserLimit))
            : RateLimitPartition.GetFixedWindowLimiter(
                $"{AddressPartitionKind}:{ClientAddress(context)}",
                _ => FixedWindow(AnonymousLimit));
    }

    private static FixedWindowRateLimiterOptions FixedWindow(int permitLimit) => new()
    {
        PermitLimit = permitLimit,
        Window = Window,
        QueueLimit = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true,
    };

    private sealed class ClientAddressPolicy(string name, int permitLimit) : IRateLimiterPolicy<string>
    {
        public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected =>
            (context, cancellationToken) => RateLimitRejections.WriteAsync(context, name, cancellationToken);

        public RateLimitPartition<string> GetPartition(HttpContext httpContext) =>
            RateLimitPartition.GetFixedWindowLimiter(ClientAddress(httpContext), _ => FixedWindow(permitLimit));
    }
}
