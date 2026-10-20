using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.OpenApi;

namespace FieldService.Api.Shared.OpenApi;

public sealed class ApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public const string BearerSchemeName = "Bearer";

    private const string DocumentTitle = "FieldService API";

    private static readonly string[] _problemSchemaNames = ["ProblemDetails", "HttpValidationProblemDetails"];

    private static readonly string[] _problemExtensionMembers = ["code", "correlationId", "traceId"];

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = DocumentTitle,
            Version = context.DocumentName,
        };
        document.Servers = null;

        document.Components ??= new OpenApiComponents();
        AddBearerScheme(document.Components);
        RequireBearerOnProtectedOperations(document, context.DescriptionGroups);
        AddProblemExtensionMembers(document.Components);

        return Task.CompletedTask;
    }

    private static void AddBearerScheme(OpenApiComponents components)
    {
        components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        components.SecuritySchemes[BearerSchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
        };
    }

    private static void RequireBearerOnProtectedOperations(
        OpenApiDocument document,
        IReadOnlyList<ApiDescriptionGroup> descriptionGroups)
    {
        foreach (var description in descriptionGroups.SelectMany(group => group.Items))
        {
            if (description.RelativePath is null
                || description.HttpMethod is null
                || description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            {
                continue;
            }

            if (!document.Paths.TryGetValue(ToDocumentPath(description.RelativePath), out var pathItem)
                || pathItem.Operations is null
                || !pathItem.Operations.TryGetValue(new HttpMethod(description.HttpMethod), out var operation))
            {
                continue;
            }

            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerSchemeName, document)] = [],
            });
        }
    }

    private static void AddProblemExtensionMembers(OpenApiComponents components)
    {
        if (components.Schemas is null)
        {
            return;
        }

        foreach (var schemaName in _problemSchemaNames)
        {
            if (!components.Schemas.TryGetValue(schemaName, out var schema) || schema is not OpenApiSchema problemSchema)
            {
                continue;
            }

            problemSchema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
            foreach (var member in _problemExtensionMembers)
            {
                problemSchema.Properties.TryAdd(member, new OpenApiSchema { Type = JsonSchemaType.String });
            }
        }
    }

    private static string ToDocumentPath(string relativePath)
    {
        var pattern = RoutePatternFactory.Parse(relativePath);
        var path = new StringBuilder();
        foreach (var segment in pattern.PathSegments)
        {
            path.Append('/');
            foreach (var part in segment.Parts)
            {
                path.Append(part switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternParameterPart parameter => $"{{{parameter.Name}}}",
                    RoutePatternSeparatorPart separator => separator.Content,
                    _ => string.Empty,
                });
            }
        }

        return path.Length == 0 ? "/" : path.ToString();
    }
}
