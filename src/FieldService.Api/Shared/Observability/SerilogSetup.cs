using System.Globalization;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace FieldService.Api.Shared.Observability;

public static class SerilogSetup
{
    public const string FileNamePattern = "api-.log";

    public static Logger CreateBootstrapLogger()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();
    }

    public static void Configure(LoggerConfiguration logger, IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IHostEnvironment>();
        var logs = services.GetRequiredService<IOptions<LogsOptions>>().Value;

        logger
            .ReadFrom.Configuration(configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();

        if (logs.UsesJsonConsole)
        {
            logger.WriteTo.Console(new CompactJsonFormatter());
        }
        else
        {
            logger.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
        }

        if (logs.FileEnabled)
        {
            logger.WriteTo.File(
                new CompactJsonFormatter(),
                ResolveFilePath(environment.ContentRootPath, logs.Directory!),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: logs.RetainedFileCountLimit,
                fileSizeLimitBytes: logs.FileSizeLimitBytes,
                rollOnFileSizeLimit: true);
        }
    }

    public static string ResolveFilePath(string contentRootPath, string directory)
    {
        return Path.GetFullPath(Path.Combine(contentRootPath, directory, FileNamePattern));
    }
}
