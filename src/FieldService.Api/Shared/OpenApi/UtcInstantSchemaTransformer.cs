using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FieldService.Api.Shared.OpenApi;

public sealed class UtcInstantSchemaTransformer : IOpenApiSchemaTransformer
{
    private const string DateTimeFormat = "date-time";

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (type == typeof(DateTimeOffset))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = DateTimeFormat;
        }
        else if (type == typeof(DateTimeOffset?))
        {
            schema.Type = JsonSchemaType.String | JsonSchemaType.Null;
            schema.Format = DateTimeFormat;
        }

        return Task.CompletedTask;
    }
}
