using System.Diagnostics;
using FieldService.Api.Shared.Observability;
using Microsoft.AspNetCore.Http;

namespace FieldService.Api.Tests.Shared.Observability;

public sealed class CorrelationIdTests
{
    public static TheoryData<string> AcceptedValues =>
    [
        "a",
        "f0766650-82a1-49f8-b5cc-f3103d346544",
        "Release_1.2-rc.3",
        new string('x', 64),
    ];

    public static TheoryData<string> ReplacedValues =>
    [
        string.Empty,
        new string('x', 65),
        "has space",
        "line\r\nbreak",
        "trailing-line-feed\n",
        "tab\there",
        "café",
        "semi;colon",
        "slash/value",
    ];

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void AValidInboundValueIsEchoed(string value)
    {
        var context = ContextWith(value);

        Assert.True(CorrelationId.TryParse(value, out var parsed));
        Assert.Equal(value, parsed.Value);
        Assert.Equal(value, CorrelationId.From(context).Value);
    }

    [Theory]
    [MemberData(nameof(ReplacedValues))]
    public void AnInvalidInboundValueIsReplacedByTheW3CTraceIdOfTheRequest(string value)
    {
        using var activity = new Activity("correlation-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var context = ContextWith(value);

        var correlationId = CorrelationId.From(context);

        Assert.False(CorrelationId.TryParse(value, out _));
        Assert.Equal(activity.TraceId.ToHexString(), correlationId.Value);
        Assert.Matches("^[0-9a-f]{32}$", correlationId.Value);
    }

    [Fact]
    public void TwoInboundValuesAreIgnoredEvenWhenEachIsValid()
    {
        using var activity = new Activity("correlation-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationId.HeaderName] = new[] { "first", "second" };

        Assert.Equal(activity.TraceId.ToHexString(), CorrelationId.From(context).Value);
    }

    [Fact]
    public void WithoutAnInboundValueOrAnActivityARandomTraceIdIsUsedAndKeptForTheRequest()
    {
        Activity.Current = null;
        var context = new DefaultHttpContext();

        var first = CorrelationId.From(context);
        var second = CorrelationId.From(context);

        Assert.Matches("^[0-9a-f]{32}$", first.Value);
        Assert.Same(first, second);
        Assert.NotEqual(first.Value, CorrelationId.From(new DefaultHttpContext()).Value);
    }

    private static DefaultHttpContext ContextWith(string value)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationId.HeaderName] = value;
        return context;
    }
}
