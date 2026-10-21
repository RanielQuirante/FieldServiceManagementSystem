using Microsoft.Extensions.Options;
using Serilog;

namespace FieldService.Api.Shared.Observability;

public static class ObservabilityRegistration
{
    public static IHostApplicationBuilder AddObservability(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IValidateOptions<LogsOptions>, LogsOptionsValidator>();
        builder.Services.AddOptions<LogsOptions>()
            .Bind(builder.Configuration.GetSection(LogsOptions.SectionName))
            .ValidateOnStart();

        builder.Services.AddSerilog(
            static (services, logger) => SerilogSetup.Configure(logger, services),
            preserveStaticLogger: true);

        return builder;
    }

    public static WebApplication ValidateConfiguration(this WebApplication app)
    {
        app.Services.GetService<IStartupValidator>()?.Validate();
        return app;
    }

    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }

    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
    {
        var logger = app.ApplicationServices.GetRequiredService<Serilog.ILogger>();
        return app.UseSerilogRequestLogging(options => RequestLoggingSetup.Configure(options, logger));
    }
}
