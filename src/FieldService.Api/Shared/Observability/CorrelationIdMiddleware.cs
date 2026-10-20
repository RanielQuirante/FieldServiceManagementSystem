using Serilog.Context;

namespace FieldService.Api.Shared.Observability;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string ApiPathPrefix = "/api";

    private const string HealthPathPrefix = "/health";

    private const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = CorrelationId.From(context);
        context.Response.OnStarting(WriteResponseHeaders, context);

        using (LogContext.PushProperty(RequestLoggingSetup.CorrelationIdProperty, correlationId.Value))
        {
            await next(context);
        }
    }

    private static Task WriteResponseHeaders(object state)
    {
        var context = (HttpContext)state;
        var path = context.Request.Path;
        var headers = context.Response.Headers;

        headers[CorrelationId.HeaderName] = CorrelationId.From(context).Value;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";

        var isApi = path.StartsWithSegments(ApiPathPrefix);
        if (isApi)
        {
            headers.CacheControl = "no-store";
        }

        if (isApi || path.StartsWithSegments(HealthPathPrefix))
        {
            headers.ContentSecurityPolicy = ApiContentSecurityPolicy;
        }

        return Task.CompletedTask;
    }
}
