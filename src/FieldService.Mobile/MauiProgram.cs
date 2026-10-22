using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Features.SignIn;
using FieldService.Mobile.Shared;
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
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Geist-Regular.ttf", "GeistRegular");
                fonts.AddFont("Geist-Medium.ttf", "GeistMedium");
                fonts.AddFont("Geist-SemiBold.ttf", "GeistSemiBold");
                fonts.AddFont("Geist-Bold.ttf", "GeistBold");
                fonts.AddFont("GeistMono-Medium.ttf", "GeistMonoMedium");
                fonts.AddFont("GeistMono-SemiBold.ttf", "GeistMonoSemiBold");
            });
        builder.AddMobileLogging(logger, MinimumLevel);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services
            .AddSharedServices(ApiBaseUrl.FromAssembly(typeof(MauiProgram).Assembly))
            .AddSessionFeature()
            .AddJobsFeature();
        builder.Services.AddSingleton<AppShell>();

        configurePlatform?.Invoke(builder);

        var app = builder.Build();
        logger.Information("app.started {AppVersion}", AppInfo.Current.VersionString);

        return app;
    }
}
