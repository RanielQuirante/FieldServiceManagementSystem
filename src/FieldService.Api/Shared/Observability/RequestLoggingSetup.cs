using System.Security.Claims;
using FieldService.Api.Shared.Errors;
using Serilog;
using Serilog.AspNetCore;
using Serilog.Core;
using Serilog.Events;

namespace FieldService.Api.Shared.Observability;

public static class RequestLoggingSetup
{
    public const string CorrelationIdProperty = "CorrelationId";

    public const string UserIdProperty = "UserId";

    public const string RoleProperty = "Role";

    private const string HealthPathPrefix = "/health";

    private const string SubjectClaim = "sub";

    private const string RoleClaim = "role";

    private static readonly object _effectiveStatusKey = new();

    public static void Configure(RequestLoggingOptions options, Serilog.ILogger logger)
    {
        options.Logger = new ExceptionFreeLogger(logger);
        options.IncludeQueryInRequestPath = false;
        options.GetLevel = GetLevel;
        options.EnrichDiagnosticContext = EnrichDiagnosticContext;
        options.GetMessageTemplateProperties = GetMessageTemplateProperties;
    }

    public static LogEventLevel GetLevel(HttpContext context, double elapsedMilliseconds, Exception? exception)
    {
        var status = context.Response.StatusCode;
        if (exception is not null)
        {
            status = ExceptionStatus.For(exception, context);
            context.Items[_effectiveStatusKey] = status;
        }

        if (context.Request.Path.StartsWithSegments(HealthPathPrefix))
        {
            return LogEventLevel.Verbose;
        }

        return status >= StatusCodes.Status500InternalServerError ? LogEventLevel.Warning : LogEventLevel.Information;
    }

    private static void EnrichDiagnosticContext(IDiagnosticContext diagnosticContext, HttpContext context)
    {
        diagnosticContext.Set(CorrelationIdProperty, CorrelationId.From(context).Value);

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (context.User.FindFirstValue(SubjectClaim) is { } userId)
        {
            diagnosticContext.Set(UserIdProperty, userId);
        }

        if (context.User.FindFirstValue(RoleClaim) is { } role)
        {
            diagnosticContext.Set(RoleProperty, role);
        }
    }

    private static IEnumerable<LogEventProperty> GetMessageTemplateProperties(
        HttpContext context,
        string requestPath,
        double elapsedMilliseconds,
        int statusCode)
    {
        var status = context.Items.TryGetValue(_effectiveStatusKey, out var stored) && stored is int effective
            ? effective
            : statusCode;

        return
        [
            new LogEventProperty("RequestMethod", new ScalarValue(context.Request.Method)),
            new LogEventProperty("RequestPath", new ScalarValue(requestPath)),
            new LogEventProperty("StatusCode", new ScalarValue(status)),
            new LogEventProperty("Elapsed", new ScalarValue(elapsedMilliseconds)),
        ];
    }

    private sealed class ExceptionFreeLogger(Serilog.ILogger inner) : Serilog.ILogger
    {
        public Serilog.ILogger ForContext(ILogEventEnricher enricher) =>
            new ExceptionFreeLogger(inner.ForContext(enricher));

        public Serilog.ILogger ForContext(IEnumerable<ILogEventEnricher> enrichers) =>
            new ExceptionFreeLogger(inner.ForContext(enrichers));

        public Serilog.ILogger ForContext(string propertyName, object? value, bool destructureObjects = false) =>
            new ExceptionFreeLogger(inner.ForContext(propertyName, value, destructureObjects));

        public Serilog.ILogger ForContext<TSource>() =>
            new ExceptionFreeLogger(inner.ForContext<TSource>());

        public Serilog.ILogger ForContext(Type source) =>
            new ExceptionFreeLogger(inner.ForContext(source));

        public bool IsEnabled(LogEventLevel level) => inner.IsEnabled(level);

        public void Write(LogEvent logEvent)
        {
            if (logEvent.Exception is null)
            {
                inner.Write(logEvent);
                return;
            }

            inner.Write(new LogEvent(
                logEvent.Timestamp,
                logEvent.Level,
                null,
                logEvent.MessageTemplate,
                logEvent.Properties.Select(property => new LogEventProperty(property.Key, property.Value)),
                logEvent.TraceId ?? default,
                logEvent.SpanId ?? default));
        }
    }
}
