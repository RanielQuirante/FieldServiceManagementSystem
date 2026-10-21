using FieldService.Api.Shared.Observability;
using Serilog.Events;
using Serilog.Parsing;

namespace FieldService.Api.Tests.Shared.Observability;

public sealed class EntityFrameworkErrorFilterTests
{
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Query", LogEventLevel.Error, true)]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Connection", LogEventLevel.Error, true)]
    [InlineData("Microsoft.EntityFrameworkCore.Update", LogEventLevel.Fatal, true)]
    [InlineData("Microsoft.EntityFrameworkCore", LogEventLevel.Error, true)]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning, false)]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Information, false)]
    [InlineData("Microsoft.EntityFrameworkCoreExtensions", LogEventLevel.Error, false)]
    [InlineData("FieldService.Api.Shared.Errors.FallbackExceptionHandler", LogEventLevel.Error, false)]
    [InlineData("Npgsql.Connection", LogEventLevel.Error, false)]
    public void OnlyEntityFrameworkCoreEventsAtErrorOrAboveAreExcluded(string category, LogEventLevel level, bool excluded)
    {
        var logEvent = new LogEvent(
            DateTimeOffset.UnixEpoch,
            level,
            exception: null,
            new MessageTemplateParser().Parse("A message"),
            [new LogEventProperty("SourceContext", new ScalarValue(category))]);

        Assert.Equal(excluded, EntityFrameworkErrorFilter.IsExcluded(logEvent));
    }

    [Fact]
    public void AnEventWithoutACategoryIsKept()
    {
        var logEvent = new LogEvent(
            DateTimeOffset.UnixEpoch,
            LogEventLevel.Error,
            exception: null,
            new MessageTemplateParser().Parse("A message"),
            []);

        Assert.False(EntityFrameworkErrorFilter.IsExcluded(logEvent));
    }
}
