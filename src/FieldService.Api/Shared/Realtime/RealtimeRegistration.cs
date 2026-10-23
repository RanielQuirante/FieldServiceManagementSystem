using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace FieldService.Api.Shared.Realtime;

public static class RealtimeRegistration
{
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan CircuitSamplingDuration = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan CircuitBreakDuration = TimeSpan.FromSeconds(15);

    public const int MaxRetryAttempts = 3;

    public const int CircuitMinimumThroughput = 4;

    public const double CircuitFailureRatio = 0.5;

    public static IServiceCollection AddRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<NotificationsOptions>, NotificationsOptionsValidator>();
        services.AddOptions<NotificationsOptions>()
            .Bind(configuration.GetSection(NotificationsOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<JobEventQueue>();
        services.TryAddSingleton<IJobEventQueue>(provider =>
            provider.GetRequiredService<IOptions<NotificationsOptions>>().Value.IsEnabled
                ? provider.GetRequiredService<JobEventQueue>()
                : new NullJobEventQueue());

        services.AddHttpClient<NotificationsClient>(client => client.Timeout = ClientTimeout)
            .RemoveAllLoggers()
            .AddStandardResilienceHandler(ConfigureResilience);
        services.AddHostedService<JobEventDispatcher>();

        return services;
    }

    private static void ConfigureResilience(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = AttemptTimeout;
        options.TotalRequestTimeout.Timeout = TotalTimeout;
        options.Retry.MaxRetryAttempts = MaxRetryAttempts;
        options.Retry.Delay = RetryDelay;
        options.Retry.UseJitter = true;
        options.CircuitBreaker.SamplingDuration = CircuitSamplingDuration;
        options.CircuitBreaker.MinimumThroughput = CircuitMinimumThroughput;
        options.CircuitBreaker.FailureRatio = CircuitFailureRatio;
        options.CircuitBreaker.BreakDuration = CircuitBreakDuration;
    }
}
