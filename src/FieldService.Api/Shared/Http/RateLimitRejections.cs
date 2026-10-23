using System.Globalization;
using System.Threading.RateLimiting;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldService.Api.Shared.Http;

public sealed class RateLimitRejections(TimeProvider time, ILogger<RateLimitRejections> logger)
{
    public const int DefaultRetryAfterSeconds = 60;

    private const int PruneThreshold = 10_000;

    private readonly Lock _gate = new();

    private readonly Dictionary<string, DateTimeOffset> _loggedUntil = new(StringComparer.Ordinal);

    public static async ValueTask WriteAsync(OnRejectedContext context, string policy, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var retryAfter = RetryAfterSeconds(context.Lease);
        http.RequestServices.GetRequiredService<RateLimitRejections>().Record(http, policy, retryAfter);

        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
        var problem = Problems.Create(
            http,
            ErrorCodes.RateLimitExceeded,
            string.Create(CultureInfo.InvariantCulture, $"Try again in {retryAfter} seconds."));
        await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
        });
    }

    public static int RetryAfterSeconds(RateLimitLease lease) =>
        lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) && retryAfter > TimeSpan.Zero
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : DefaultRetryAfterSeconds;

    private void Record(HttpContext http, string policy, int windowSeconds)
    {
        var userId = RateLimitPolicies.PartitionsByUser(policy) ? RateLimitPolicies.AuthenticatedUserId(http) : null;
        var partitionKind = userId is null ? RateLimitPolicies.AddressPartitionKind : RateLimitPolicies.UserPartitionKind;
        var partition = userId ?? RateLimitPolicies.ClientAddress(http);
        if (!IsFirstInWindow($"{policy}|{partitionKind}:{partition}", windowSeconds))
        {
            return;
        }

        SecurityLog.RateLimitRejected(
            logger,
            policy,
            partitionKind,
            userId,
            userId is null ? partition : null,
            RouteTemplates.Of(http.GetEndpoint() as RouteEndpoint));
    }

    private bool IsFirstInWindow(string key, int windowSeconds)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (_loggedUntil.TryGetValue(key, out var until) && until > now)
            {
                return false;
            }

            if (_loggedUntil.Count >= PruneThreshold)
            {
                foreach (var expired in _loggedUntil.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToList())
                {
                    _loggedUntil.Remove(expired);
                }
            }

            _loggedUntil[key] = now.AddSeconds(windowSeconds);
            return true;
        }
    }
}
