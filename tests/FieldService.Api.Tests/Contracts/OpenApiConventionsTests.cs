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
