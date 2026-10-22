using System.Text.Json;
using System.Text.Json.Serialization;

namespace FieldService.Mobile.Shared.Http;

public sealed class TolerantEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> _members =
        Enum.GetValues<TEnum>().ToDictionary(value => value.ToString(), StringComparer.Ordinal);

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return default;
        }

        var text = reader.GetString();
        return text is not null && _members.TryGetValue(text, out var value) ? value : default;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}
