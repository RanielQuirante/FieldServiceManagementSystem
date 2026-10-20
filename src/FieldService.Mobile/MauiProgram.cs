using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Shared.Logging;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace FieldService.Mobile;

public static class MauiProgram
{
#if DEBUG
    private const LogEventLevel MinimumLevel = LogEventLevel.Debug;
#else
    private const LogEventLevel MinimumLevel = LogEventLevel.Information;
#endif

    public static MauiApp CreateMauiApp(Action<MauiAppBuilder>? configurePlatform = null)
    {
        var logger = MobileLogging.CreateLogger(FileSystem.Current.AppDataDirectory, MinimumLevel);

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.AddMobileLogging(logger, MinimumLevel);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton(ApiBaseUrl.FromAssembly(typeof(MauiProgram).Assembly));

        configurePlatform?.Invoke(builder);

        var app = builder.Build();
        logger.Information("app.started {AppVersion}", AppInfo.Current.VersionString);

        return app;
    }
}
