using Microsoft.AspNetCore.Mvc.ModelBinding;
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
            schema.Type = IsParameterValue(context) ? JsonSchemaType.String : JsonSchemaType.String | JsonSchemaType.Null;
            schema.Format = DateTimeFormat;
        }

        return Task.CompletedTask;
    }

    private static bool IsParameterValue(OpenApiSchemaTransformerContext context) =>
        context.JsonPropertyInfo is null
        && context.ParameterDescription is { } parameter
        && parameter.Source != BindingSource.Body;
}
