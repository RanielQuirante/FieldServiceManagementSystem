using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FieldService.Api.Shared.OpenApi;

public sealed class NullabilitySchemaTransformer : IOpenApiSchemaTransformer
{
    private const string NullablePropertyMarker = "x-is-nullable-property";

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        var underlyingType = Nullable.GetUnderlyingType(type);

        if ((underlyingType ?? type).IsEnum && schema.Enum is { Count: > 0 } values)
        {
            schema.Enum = [.. values.Where(value => value is not null && value.GetValueKind() != JsonValueKind.Null)];
        }

        if (underlyingType is not null
            && context.JsonPropertyInfo?.AttributeProvider?.IsDefined(typeof(RequiredAttribute), inherit: false) == true)
        {
            if (schema.Type is { } schemaType)
            {
                schema.Type = schemaType & ~JsonSchemaType.Null;
            }

            schema.Metadata?.Remove(NullablePropertyMarker);
        }

        return Task.CompletedTask;
    }
}
