using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FieldService.Api.Shared.Realtime;

public static class RealtimeRegistration
{
    public static IServiceCollection AddRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<NotificationsOptions>, NotificationsOptionsValidator>();
        services.AddOptions<NotificationsOptions>()
            .Bind(configuration.GetSection(NotificationsOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton<IJobEventQueue, NullJobEventQueue>();
        return services;
    }
}
