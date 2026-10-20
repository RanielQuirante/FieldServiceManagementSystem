using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace FieldService.Api.Shared.Observability;

public sealed record CorrelationId
{
    public const string HeaderName = "X-Correlation-ID";

    public const int MaxLength = 64;

    private static readonly object _itemKey = new();

    private CorrelationId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out CorrelationId? correlationId)
    {
        if (IsValid(value))
        {
            correlationId = new CorrelationId(value);
            return true;
        }

        correlationId = null;
        return false;
    }

    public static CorrelationId From(HttpContext context)
    {
        if (context.Items.TryGetValue(_itemKey, out var stored) && stored is CorrelationId existing)
        {
            return existing;
        }

        var resolved = Resolve(context);
        context.Items[_itemKey] = resolved;
        return resolved;
    }

    private static CorrelationId Resolve(HttpContext context)
    {
        var inbound = context.Request.Headers[HeaderName];
        if (inbound.Count == 1 && TryParse(inbound[0], out var accepted))
        {
            return accepted;
        }

        var traceId = Activity.Current?.TraceId ?? default;
        if (traceId == default)
        {
            traceId = ActivityTraceId.CreateRandom();
        }

        return new CorrelationId(traceId.ToHexString());
    }

    private static bool IsValid([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
