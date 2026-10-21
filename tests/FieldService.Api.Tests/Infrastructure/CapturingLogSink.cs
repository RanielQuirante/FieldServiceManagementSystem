using System.Collections.Concurrent;
using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace FieldService.Api.Tests.Infrastructure;

public sealed class CapturingLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Events => [.. _events];

    public IReadOnlyList<string> RenderedMessages =>
        [.. _events.Select(logEvent => logEvent.RenderMessage(CultureInfo.InvariantCulture))];

    public static bool HasEventName(LogEvent logEvent, string eventName)
    {
        return logEvent.Properties.TryGetValue("EventId", out var eventId)
            && eventId is StructureValue structure
            && structure.Properties.Any(property =>
                property.Name == "Name" && Equals((property.Value as ScalarValue)?.Value, eventName));
    }

    public static string? Scalar(LogEvent logEvent, string property)
    {
        return logEvent.Properties.TryGetValue(property, out var value) && value is ScalarValue scalar
            ? Convert.ToString(scalar.Value, CultureInfo.InvariantCulture)
            : null;
    }

    public static string Text(LogEvent logEvent)
    {
        return $"{logEvent.RenderMessage(CultureInfo.InvariantCulture)} {string.Join(' ', logEvent.Properties.Values)} {logEvent.Exception}";
    }

    public IReadOnlyList<LogEvent> Named(string eventName)
    {
        return [.. _events.Where(logEvent => HasEventName(logEvent, eventName))];
    }

    public void Emit(LogEvent logEvent)
    {
        _events.Enqueue(logEvent);
    }

    public void Clear()
    {
        _events.Clear();
    }
}
