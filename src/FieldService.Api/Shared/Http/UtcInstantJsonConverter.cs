using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FieldService.Api.Shared.Http;

public sealed partial class UtcInstantJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string WireFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException();
        }

        var text = reader.GetString();
        if (text is null || !Rfc3339WithOffset().IsMatch(text))
        {
            throw new JsonException();
        }

        if (!DateTimeOffset.TryParse(
            text.ToUpperInvariant(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            throw new JsonException();
        }

        var ticks = parsed.UtcTicks;
        return new DateTimeOffset(ticks - (ticks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.UtcDateTime.ToString(WireFormat, CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(
        @"^[0-9]{4}-[0-9]{2}-[0-9]{2}[Tt][0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?([Zz]|[+-][0-9]{2}:[0-9]{2})\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex Rfc3339WithOffset();
}
