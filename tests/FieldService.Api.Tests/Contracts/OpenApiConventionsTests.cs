using System.Text.Json;
using System.Text.RegularExpressions;
using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Contracts;

[Collection(DatabaseCollection.Name)]
public sealed partial class OpenApiConventionsTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string FormatMember = "format";

    private const string TypeMember = "type";

    [Fact]
    public async Task NoSchemaInTheSnapshotOrTheServedDocumentHasAFormatWithoutAType()
    {
        var documents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"served {OpenApiSnapshotTests.DocumentPath}"] =
                await OpenApiSnapshotTests.ReadServedDocumentAsync(Fixture.Factory),
        };
        if (File.Exists(OpenApiSnapshotTests.SnapshotPath))
        {
            documents["docs/contracts/openapi.v1.json"] = File.ReadAllText(OpenApiSnapshotTests.SnapshotPath);
        }

        var findings = documents
            .SelectMany(document => FormatsWithoutType(document.Value).Select(path => $"{document.Key}: {path}"))
            .ToList();

        Assert.True(
            findings.Count == 0,
            "Each schema with a format needs a type (the date-time schema transformer, docs/contracts/README.md). Schemas with a format and no type:\n"
                + string.Join("\n", findings));
    }

    [Fact]
    public void FormatWithoutTypeFinderReportsEveryUntypedFormatInACraftedDocument()
    {
        const string Document = """
            {
              "openapi": "3.1.1",
              "paths": {
                "/api/probe": {
                  "get": {
                    "parameters": [
                      { "name": "format", "in": "query", "schema": { "format": "uuid" } },
                      { "name": "since", "in": "query", "schema": { "type": "string", "format": "date-time" } }
                    ],
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              },
              "components": {
                "schemas": {
                  "Probe": {
                    "type": "object",
                    "properties": {
                      "format": { "type": "string" },
                      "scheduledStart": { "format": "date-time" },
                      "completedAt": { "type": ["null", "string"], "format": "date-time" },
                      "window": { "anyOf": [ { "type": "null" }, { "format": "date-time" } ] },
                      "readings": { "type": "array", "items": { "format": "double" } }
                    }
                  }
                },
                "securitySchemes": { "Bearer": { "type": "http", "scheme": "bearer", "bearerFormat": "JWT" } }
              }
            }
            """;
        string[] expected =
        [
            "$.paths['/api/probe'].get.parameters[0].schema",
            "$.components.schemas.Probe.properties.scheduledStart",
            "$.components.schemas.Probe.properties.window.anyOf[1]",
            "$.components.schemas.Probe.properties.readings.items",
        ];

        var findings = FormatsWithoutType(Document);

        Assert.Equal(expected, findings);
    }

    [Fact]
    public void EveryOperationHasAnOperationIdASummaryAndATagAndOperationIdsAreUnique()
    {
        var operations = Operations();

        var findings = operations
            .Where(operation => string.IsNullOrWhiteSpace(StringOf(operation.Body, "operationId"))
                || string.IsNullOrWhiteSpace(StringOf(operation.Body, "summary"))
                || !operation.Body.TryGetProperty("tags", out var tags)
                || tags.GetArrayLength() == 0)
            .Select(operation => operation.Name)
            .ToList();
        var ids = operations.Select(operation => StringOf(operation.Body, "operationId")).ToList();

        Assert.NotEmpty(operations);
        Assert.True(findings.Count == 0, "Operations without operationId, summary or tag:\n" + string.Join("\n", findings));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryErrorResponseIsDocumentedAsProblemJsonOnly()
    {
        var findings = Operations()
            .SelectMany(operation => Responses(operation)
                .Where(response => response.Status >= 400)
                .Where(response => !response.Body.TryGetProperty("content", out var content)
                    || !content.EnumerateObject().Select(media => media.Name).SequenceEqual([ProblemDetailsShapeTests.ProblemMediaType]))
                .Select(response => $"{operation.Name} {response.Status}"))
            .ToList();

        Assert.True(findings.Count == 0, "Error responses not documented as application/problem+json only:\n" + string.Join("\n", findings));
    }

    [Fact]
    public void WriteOperationsDocument400ProtectedOperations401And403EveryOperation429AndJobRoutes404()
    {
        var findings = new List<string>();
        foreach (var operation in Operations())
        {
            var statuses = Responses(operation).Select(response => response.Status).ToHashSet();
            if (operation.Method is "post" or "put" or "patch" or "delete" && !statuses.Contains(400))
            {
                findings.Add($"{operation.Name}: a write operation without 400");
            }

            if (IsProtected(operation) && !(statuses.Contains(401) && statuses.Contains(403)))
            {
                findings.Add($"{operation.Name}: a protected operation without 401 and 403");
            }

            if (operation.Path.StartsWith("/api/", StringComparison.Ordinal) && !statuses.Contains(429))
            {
                findings.Add($"{operation.Name}: an /api operation without 429");
            }

            if (operation.Path.Contains("{jobId}", StringComparison.Ordinal) && !statuses.Contains(404))
            {
                findings.Add($"{operation.Name}: a job route without 404");
            }

            if (RequestSchemaHasVersion(operation) && !statuses.Contains(409))
            {
                findings.Add($"{operation.Name}: a versioned command without 409");
            }
        }

        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    [Fact]
    public void TheBearerSchemeIsDeclaredAndOnlyTheThreeAuthOperationsAreAnonymous()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(OpenApiSnapshotTests.SnapshotPath));
        var scheme = document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");

        var anonymous = Operations().Where(operation => !IsProtected(operation)).Select(operation => StringOf(operation.Body, "operationId"));
        var misdeclared = Operations()
            .Where(IsProtected)
            .Where(operation => !operation.Body.GetProperty("security").EnumerateArray().Any(requirement => requirement.TryGetProperty("Bearer", out _)))
            .Select(operation => operation.Name)
            .ToList();

        Assert.Equal("http", StringOf(scheme, "type"));
        Assert.Equal("bearer", StringOf(scheme, "scheme"));
        Assert.Equal(["RefreshSession", "SignIn", "SignOut"], anonymous.Order(StringComparer.Ordinal));
        Assert.Empty(misdeclared);
        Assert.False(document.RootElement.TryGetProperty("security", out _), "A document-wide security requirement would hide anonymous operations.");
    }

    [Fact]
    public void EveryPathStartsWithApiAndNoInternalOrHealthPathIsDocumented()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(OpenApiSnapshotTests.SnapshotPath));

        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToList();

        Assert.NotEmpty(paths);
        Assert.All(paths, path => Assert.StartsWith("/api/", path, StringComparison.Ordinal));
        Assert.DoesNotContain(paths, path => path.Contains("/internal", StringComparison.Ordinal) || path.Contains("/health", StringComparison.Ordinal));
    }

    [Fact]
    public void SchemaNamesAreUniqueIgnoringCaseAndEnumerationsAreStrings()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(OpenApiSnapshotTests.SnapshotPath));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject().ToList();

        var names = schemas.Select(schema => schema.Name).ToList();
        var enumerations = schemas.Where(schema => schema.Value.TryGetProperty("enum", out _)).ToList();
        var nonString = enumerations
            .Where(schema => schema.Value.GetProperty("enum").EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String)
                || (schema.Value.TryGetProperty(TypeMember, out var type) && type.ValueKind == JsonValueKind.String && type.GetString() != "string"))
            .Select(schema => schema.Name)
            .ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.NotEmpty(enumerations);
        Assert.Empty(nonString);
    }

    [Fact]
    public void InstantsAreDateTimeIdsAreUuidAndJobResponsesCarryAnIntegerVersion()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(OpenApiSnapshotTests.SnapshotPath));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var findings = new List<string>();

        foreach (var schema in schemas.EnumerateObject().Where(schema => !schema.Name.Contains("Problem", StringComparison.Ordinal)))
        {
            if (!schema.Value.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            foreach (var member in properties.EnumerateObject())
            {
                var format = StringOf(member.Value, FormatMember);
                if (IdPattern().IsMatch(member.Name) && format != "uuid")
                {
                    findings.Add($"{schema.Name}.{member.Name}: an id without format uuid");
                }

                if (InstantPattern().IsMatch(member.Name) && format != "date-time")
                {
                    findings.Add($"{schema.Name}.{member.Name}: an instant without format date-time");
                }
            }
        }

        foreach (var parameter in Operations().SelectMany(Parameters))
        {
            if (IdPattern().IsMatch(parameter.Name) && StringOf(parameter.Schema, FormatMember) != "uuid")
            {
                findings.Add($"parameter {parameter.Name}: an id without format uuid");
            }
        }

        foreach (var name in new[] { "JobDetailsResponse", "JobSummaryResponse" })
        {
            var version = schemas.GetProperty(name).GetProperty("properties").GetProperty("version");
            if (StringOf(version, TypeMember) != "integer")
            {
                findings.Add($"{name}.version: not an integer");
            }
        }

        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    private static List<Operation> Operations()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(OpenApiSnapshotTests.SnapshotPath));
        return
        [
            .. document.RootElement.GetProperty("paths").EnumerateObject()
                .SelectMany(path => path.Value.EnumerateObject()
                    .Where(method => method.Name is "get" or "put" or "post" or "delete" or "patch")
                    .Select(method => new Operation(path.Name, method.Name, method.Value.Clone()))),
        ];
    }

    private static IEnumerable<(int Status, JsonElement Body)> Responses(Operation operation) =>
        operation.Body.GetProperty("responses").EnumerateObject()
            .Select(response => (int.Parse(response.Name, System.Globalization.CultureInfo.InvariantCulture), response.Value));

    private static IEnumerable<(string Name, JsonElement Schema)> Parameters(Operation operation) =>
        operation.Body.TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray().Select(parameter => (StringOf(parameter, "name") ?? string.Empty, parameter.GetProperty("schema")))
            : [];

    private static bool IsProtected(Operation operation) =>
        operation.Body.TryGetProperty("security", out var security) && security.GetArrayLength() > 0;

    private static bool RequestSchemaHasVersion(Operation operation)
    {
        if (!operation.Body.TryGetProperty("requestBody", out var body))
        {
            return false;
        }

        var reference = body.GetProperty("content").EnumerateObject()
            .Select(media => media.Value.GetProperty("schema"))
            .Select(schema => StringOf(schema, "$ref"))
            .FirstOrDefault(value => value is not null);
        if (reference is null)
        {
            return false;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(OpenApiSnapshotTests.SnapshotPath));
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(reference.Split('/')[^1]);
        return schema.TryGetProperty("properties", out var properties) && properties.TryGetProperty("version", out _);
    }

    private static string? StringOf(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    [GeneratedRegex("^(id|[a-z][A-Za-z]*Id)$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^([a-z][A-Za-z]*At|scheduledStart|scheduledEnd)$")]
    private static partial Regex InstantPattern();

    private sealed record Operation(string Path, string Method, JsonElement Body)
    {
        public string Name => $"{Method.ToUpperInvariant()} {Path}";
    }

    public static List<string> FormatsWithoutType(string json)
    {
        using var document = JsonDocument.Parse(json);
        var findings = new List<string>();
        Visit(document.RootElement, "$", findings);
        return findings;
    }

    private static void Visit(JsonElement element, string path, List<string> findings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty(FormatMember, out var format)
                    && format.ValueKind == JsonValueKind.String
                    && !element.TryGetProperty(TypeMember, out _))
                {
                    findings.Add(path);
                }

                foreach (var member in element.EnumerateObject())
                {
                    Visit(member.Value, MemberPath(path, member.Name), findings);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Visit(item, $"{path}[{index++}]", findings);
                }

                break;
            default:
                break;
        }
    }

    private static string MemberPath(string path, string name)
    {
        return IdentifierPattern().IsMatch(name) ? $"{path}.{name}" : $"{path}['{name}']";
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentifierPattern();
}
