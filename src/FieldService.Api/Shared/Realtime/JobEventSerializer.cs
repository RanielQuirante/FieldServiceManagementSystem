using System.Text.Json;
using System.Text.Json.Serialization;
using FieldService.Api.Shared.Http;

namespace FieldService.Api.Shared.Realtime;

public static class JobEventSerializer
{
    public static byte[] Serialize(JobEvent jobEvent) =>
        JsonSerializer.SerializeToUtf8Bytes(
            JobEventEnvelope.From(jobEvent),
            JobEventJsonContext.Default.JobEventEnvelope);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    Converters = [typeof(UtcInstantJsonConverter)])]
[JsonSerializable(typeof(JobEventEnvelope))]
internal sealed partial class JobEventJsonContext : JsonSerializerContext
{
}
