using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace FieldService.Api.Tests.Infrastructure;

public sealed class TestClientIpStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Remote-IP";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress =
                    context.Request.Headers.TryGetValue(HeaderName, out var values)
                    && IPAddress.TryParse(values.ToString(), out var address)
                        ? address
                        : IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
