using Microsoft.OpenApi;

namespace FieldService.Api.Shared.OpenApi;

public static class OpenApiRegistration
{
    public const string DocumentName = "v1";

    public const string TestingEnvironment = "Testing";

    public static IServiceCollection AddApiDocument(this IServiceCollection services)
    {
        services.AddOpenApi(DocumentName, options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            options.AddDocumentTransformer<ApiDocumentTransformer>();
            options.AddSchemaTransformer<UtcInstantSchemaTransformer>();
        });

        return services;
    }

    public static WebApplication MapApiDocument(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment(TestingEnvironment))
        {
            app.MapOpenApi().AllowAnonymous();
        }

        return app;
    }
}
