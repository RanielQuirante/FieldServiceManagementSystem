using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace FieldService.Mobile.Shared.Logging;

public static class MobileLogging
{
    public const string DirectoryName = "logs";
    public const string FileNamePattern = "mobile-.log";
    public const int RetainedFileCountLimit = 7;
    public const long FileSizeLimitBytes = 5 * 1024 * 1024;

    public static string LogDirectory(string appDataDirectory) => Path.Combine(appDataDirectory, DirectoryName);

    public static Serilog.ILogger CreateLogger(string appDataDirectory, LogEventLevel minimumLevel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.FromLogContext()
            .WriteTo.File(
                new CompactJsonFormatter(),
                Path.Combine(LogDirectory(appDataDirectory), FileNamePattern),
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedFileCountLimit)
            .CreateLogger();
    }

    public static MauiAppBuilder AddMobileLogging(this MauiAppBuilder builder, Serilog.ILogger logger, LogEventLevel minimumLevel)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(logger);

        builder.Logging.SetMinimumLevel(ToLogLevel(minimumLevel));
        builder.Logging.AddSerilog(logger, dispose: true);
        UnhandledExceptionLogging.Register(logger);

        return builder;
    }

    public static LogLevel ToLogLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Information,
        LogEventLevel.Warning => LogLevel.Warning,
        LogEventLevel.Error => LogLevel.Error,
        _ => LogLevel.Critical,
    };
}
