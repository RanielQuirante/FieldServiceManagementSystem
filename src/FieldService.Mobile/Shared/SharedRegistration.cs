using FieldService.Mobile.Shared.Connectivity;
using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Screens;

namespace FieldService.Mobile.Shared;

public static class SharedRegistration
{
    public static IServiceCollection AddSharedServices(this IServiceCollection services, ApiBaseUrl apiBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(apiBaseUrl);

        services.AddSingleton(apiBaseUrl);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => SecureStorage.Default);
        services.AddSingleton(_ => Microsoft.Maui.Networking.Connectivity.Current);
        services.AddSingleton(_ => FileSystem.Current);
        services.AddSingleton(_ => Map.Default);
        services.AddSingleton(_ => PhoneDialer.Default);
        services.AddSingleton(_ => Launcher.Default);
        services.AddSingleton(_ => DeviceInfo.Current);
        services.AddSingleton<INavigator, ShellNavigator>();
        services.AddSingleton<NetworkStatus>();
        services.AddSingleton<AppStartup>();
        services.AddSingleton<WindowLayout>();
        services.AddSingleton<IWindowLayout>(provider => provider.GetRequiredService<WindowLayout>());
        services.AddTransient<AuthorizationHandler>();
        services.AddTransient<CorrelationIdHandler>();
        services.AddTransient<RequestLoggingHandler>();
        return services;
    }
}
