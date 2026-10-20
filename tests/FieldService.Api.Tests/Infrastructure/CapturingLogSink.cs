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

    public void Emit(LogEvent logEvent)
    {
        _events.Enqueue(logEvent);
    }

    public void Clear()
    {
        _events.Clear();
    }
}
