namespace FieldService.Mobile.Shared.Http;

public static class ApiClientRegistration
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    public static IHttpClientBuilder AddApiClient<TClient>(this IServiceCollection services, bool authorize = true)
        where TClient : class
    {
        var builder = services.AddHttpClient<TClient>((provider, client) =>
        {
            client.BaseAddress = provider.GetRequiredService<ApiBaseUrl>().Value;
            client.Timeout = RequestTimeout;
        });

        builder.RemoveAllLoggers();
        if (authorize)
        {
            builder.AddHttpMessageHandler<AuthorizationHandler>();
        }

        return builder
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddHttpMessageHandler<RequestLoggingHandler>();
    }
}
