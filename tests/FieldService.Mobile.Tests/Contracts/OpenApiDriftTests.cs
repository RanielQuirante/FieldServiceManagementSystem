using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Contracts;

public sealed class OpenApiDriftTests(ITestOutputHelper output)
{
    private static readonly Lazy<JsonDocument> _snapshot = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "openapi.v1.json"))));

    public static TheoryData<string> MappedDtos()
    {
        var data = new TheoryData<string>();
        foreach (var entry in DtoSchemaMap.Entries)
        {
            data.Add(entry.Dto.Name);
        }

        return data;
    }

    public static TheoryData<string> MappedEnums()
    {
        var data = new TheoryData<string>();
        foreach (var type in DtoSchemaMap.Enums.Keys)
        {
            data.Add(type.Name);
        }

        return data;
    }

    [Fact]
    public void TheLinkedSnapshotIsTheRepositoryContract()
    {
        var linked = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Contracts", "openapi.v1.json"));
        var repository = File.ReadAllBytes(Path.Combine(RepositoryPaths.RepositoryRoot, "docs", "contracts", "openapi.v1.json"));

        Assert.Equal(repository, linked);
    }

    [Theory]
    [MemberData(nameof(MappedDtos))]
    public void EveryMappedDtoMatchesItsSchema(string dtoName)
    {
        var entry = DtoSchemaMap.Entries.Single(candidate => candidate.Dto.Name == dtoName);

        var result = DriftChecker.Check(Schemas(), entry, DtoSchemaMap.SchemaFor);

        foreach (var info in result.Infos)
        {
            output.WriteLine(info);
        }

        Assert.Empty(result.Failures);
    }

    [Theory]
    [MemberData(nameof(MappedEnums))]
    public void EveryEnumHasUnknownFirstAndEveryValueOfItsSchema(string enumName)
    {
        var (type, schemaName) = DtoSchemaMap.Enums.Single(pair => pair.Key.Name == enumName);

        var result = DriftChecker.CheckEnum(Schemas(), type, schemaName);

        foreach (var info in result.Infos)
        {
            output.WriteLine(info);
        }

        Assert.Empty(result.Failures);
        Assert.Equal("Unknown", Enum.GetName(type, 0));
    }

    [Fact]
    public void EverySchemaTheAppSendsOrReadsIsMapped()
    {
        var mapped = DtoSchemaMap.Entries.Select(entry => entry.Schema).ToHashSet(StringComparer.Ordinal);

        Assert.All(DtoSchemaMap.RequiredSchemas, schema => Assert.Contains(schema, mapped));
        Assert.All(mapped, schema => Assert.True(Schemas().TryGetProperty(schema, out _), $"{schema} is not in the snapshot"));
    }

    [Fact]
    public void EveryNestedDtoIsMappedToTheSchemaItsParentReferences()
    {
        var failures = new List<string>();
        foreach (var entry in DtoSchemaMap.Entries)
        {
            foreach (var property in JsonProperties(entry.Dto))
            {
                var nested = DriftChecker.ElementType(property.PropertyType);
                if (nested.IsEnum || !nested.IsClass || nested == typeof(string) || nested.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
                {
                    continue;
                }

                if (DtoSchemaMap.SchemaFor(nested) is null)
                {
                    failures.Add($"{entry.Dto.Name}.{property.Name}: {nested.Name} has no schema in the map");
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void TheJobsClientUsesTheContractRoutesAndViewValues()
    {
        var paths = _snapshot.Value.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty(JobsApiClient.AssignedJobsRoute, out var assigned));
        Assert.True(paths.TryGetProperty(JobsApiClient.JobRoute, out _));

        var view = assigned.GetProperty("get").GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == JobsApiClient.ViewParameter);
        var pattern = view.GetProperty("schema").GetProperty("pattern").GetString();

        Assert.Equal("^(active|completed)$", pattern);
        Assert.Equal("active", JobsApiClient.ViewValue(AssignedJobsView.Active));
        Assert.Equal("completed", JobsApiClient.ViewValue(AssignedJobsView.Completed));
    }

    [Fact]
    public void TheCheckerCatchesAWrongTypeAMissingMemberAndANullabilityMismatch()
    {
        var entry = new DtoSchema(typeof(DriftedUserReference), "UserReference", DtoDirection.Response);
        var request = new DtoSchema(typeof(DriftedSignInRequest), "SignInRequest", DtoDirection.Request);

        var response = DriftChecker.Check(Schemas(), entry, _ => null);
        var sent = DriftChecker.Check(Schemas(), request, _ => null);

        Assert.Contains(response.Failures, failure => failure.Contains("id", StringComparison.Ordinal) && failure.Contains("uuid", StringComparison.Ordinal));
        Assert.Contains(response.Failures, failure => failure.Contains("displayName", StringComparison.Ordinal) && failure.Contains("nullable", StringComparison.Ordinal));
        Assert.Contains(response.Failures, failure => failure.Contains("nickname", StringComparison.Ordinal));
        Assert.Contains(sent.Failures, failure => failure.Contains("client", StringComparison.Ordinal) && failure.Contains("missing", StringComparison.Ordinal));
    }

    private static JsonElement Schemas() => _snapshot.Value.RootElement.GetProperty("components").GetProperty("schemas");

    private static IEnumerable<JsonPropertyInfo> JsonProperties(Type type) => MobileJsonContext.Default.GetTypeInfo(type)?.Properties ?? [];

    private sealed record DriftedUserReference(string Id, string? DisplayName, string Nickname);

    private sealed record DriftedSignInRequest(string Email, string Password);
}

public sealed record DriftResult(IReadOnlyList<string> Failures, IReadOnlyList<string> Infos);

public static class DriftChecker
{
    private static readonly NullabilityInfoContext _nullability = new();
    private static readonly JsonSerializerOptions _reflection = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    public static DriftResult Check(JsonElement schemas, DtoSchema entry, Func<Type, string?> schemaFor)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(schemaFor);

        var failures = new List<string>();
        var infos = new List<string>();
        var schema = schemas.GetProperty(entry.Schema);
        var properties = schema.TryGetProperty("properties", out var found) ? found : default;
        var required = schema.TryGetProperty("required", out var list)
            ? list.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal)
            : [];
        var dtoProperties = Properties(entry.Dto).ToList();

        foreach (var property in dtoProperties)
        {
            var where = $"{entry.Dto.Name}.{property.Name}";
            if (properties.ValueKind != JsonValueKind.Object || !properties.TryGetProperty(property.Name, out var node))
            {
                failures.Add($"{where}: the schema {entry.Schema} has no property {property.Name}");
                continue;
            }

            var schemaNullable = IsNullable(node);
            var expectedNullable = entry.Direction == DtoDirection.Response
                ? schemaNullable || !required.Contains(property.Name)
                : schemaNullable;
            var dtoNullable = IsNullable(property);
            if (dtoNullable != expectedNullable)
            {
                failures.Add($"{where}: DTO nullable {dtoNullable}, schema nullable {expectedNullable} (required {required.Contains(property.Name)})");
            }

            var mismatch = Compare(schemas, node, Underlying(property.PropertyType), schemaFor);
            if (mismatch is not null)
            {
                failures.Add($"{where}: {mismatch}");
            }
        }

        if (properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var schemaProperty in properties.EnumerateObject())
            {
                if (dtoProperties.Any(property => property.Name == schemaProperty.Name))
                {
                    continue;
                }

                if (entry.Direction == DtoDirection.Request && required.Contains(schemaProperty.Name))
                {
                    failures.Add($"{entry.Dto.Name}: required property {schemaProperty.Name} is missing");
                }
                else
                {
                    infos.Add($"{entry.Dto.Name}: schema property {schemaProperty.Name} is not read by the app");
                }
            }
        }

        return new DriftResult(failures, infos);
    }

    public static DriftResult CheckEnum(JsonElement schemas, Type type, string schemaName)
    {
        ArgumentNullException.ThrowIfNull(type);

        var failures = new List<string>();
        var infos = new List<string>();
        var values = schemas.GetProperty(schemaName).GetProperty("enum").EnumerateArray().Select(item => item.GetString()!).ToList();
        var members = Enum.GetNames(type);
        if (!members.Contains("Unknown", StringComparer.Ordinal))
        {
            failures.Add($"{type.Name}: no Unknown member");
        }

        foreach (var value in values.Where(value => !members.Contains(value, StringComparer.Ordinal)))
        {
            failures.Add($"{type.Name}: missing {value} of {schemaName}");
        }

        foreach (var member in members.Where(member => member != "Unknown" && !values.Contains(member, StringComparer.Ordinal)))
        {
            infos.Add($"{type.Name}: member {member} is not listed by {schemaName}");
        }

        return new DriftResult(failures, infos);
    }

    public static Type ElementType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var underlying = Underlying(type);
        return underlying.IsArray
            ? underlying.GetElementType()!
            : underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                ? underlying.GetGenericArguments()[0]
                : underlying;
    }

    private static IEnumerable<JsonPropertyInfo> Properties(Type type) =>
        (MobileJsonContext.Default.GetTypeInfo(type) ?? _reflection.GetTypeInfo(type)).Properties;

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static bool IsNullable(JsonPropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
        {
            return true;
        }

        if (property.PropertyType.IsValueType)
        {
            return false;
        }

        return property.AttributeProvider is PropertyInfo info && _nullability.Create(info).ReadState == NullabilityState.Nullable;
    }

    private static bool IsNullable(JsonElement node)
    {
        if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Array)
        {
            return type.EnumerateArray().Any(item => item.GetString() == "null");
        }

        return node.TryGetProperty("oneOf", out var oneOf)
            && oneOf.EnumerateArray().Any(item => item.TryGetProperty("type", out var inner) && inner.GetString() == "null");
    }

    private static string? Compare(JsonElement schemas, JsonElement node, Type type, Func<Type, string?> schemaFor)
    {
        var reference = Reference(node);
        if (reference is not null)
        {
            var target = schemas.GetProperty(reference);
            if (target.TryGetProperty("enum", out _))
            {
                return type.IsEnum && schemaFor(type) == reference ? null : $"expected the enum {reference}, found {type.Name}";
            }

            return schemaFor(type) == reference ? null : $"expected an object mapped to {reference}, found {type.Name}";
        }

        var (jsonType, format) = TypeOf(node);
        if (jsonType == "array")
        {
            var element = ElementType(type);
            if (element == type)
            {
                return $"expected an array, found {type.Name}";
            }

            return Compare(schemas, node.GetProperty("items"), Underlying(element), schemaFor);
        }

        if (jsonType == "object")
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                && type.GetGenericArguments()[0] == typeof(string)
                && type.GetGenericArguments()[1] == typeof(string[])
                ? null
                : $"expected a map of string arrays, found {type.Name}";
        }

        var expected = (jsonType, format) switch
        {
            ("string", "uuid") => typeof(Guid),
            ("string", "date-time") => typeof(DateTimeOffset),
            ("string", _) => typeof(string),
            ("integer", "int64") => typeof(long),
            ("integer", "uint32") => typeof(uint),
            ("integer", _) => typeof(int),
            ("number", _) => typeof(decimal),
            ("boolean", _) => typeof(bool),
            _ => null,
        };

        return expected == type ? null : $"schema {jsonType}/{format ?? "-"} expects {expected?.Name ?? "an unknown type"}, found {type.Name}";
    }

    private static string? Reference(JsonElement node)
    {
        if (node.TryGetProperty("$ref", out var direct))
        {
            return direct.GetString()!.Split('/')[^1];
        }

        if (node.TryGetProperty("oneOf", out var oneOf))
        {
            foreach (var item in oneOf.EnumerateArray())
            {
                if (item.TryGetProperty("$ref", out var inner))
                {
                    return inner.GetString()!.Split('/')[^1];
                }
            }
        }

        return null;
    }

    private static (string? Type, string? Format) TypeOf(JsonElement node)
    {
        var format = node.TryGetProperty("format", out var value) ? value.GetString() : null;
        if (!node.TryGetProperty("type", out var type))
        {
            return (null, format);
        }

        var name = type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(item => item.GetString()).FirstOrDefault(item => item != "null")
            : type.GetString();
        return (name, format);
    }
}
