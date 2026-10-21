using System.Net;
using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Serilog.Events;

namespace FieldService.Api.Tests.Shared.Errors;

[Collection(DatabaseCollection.Name)]
public sealed class UnhandledExceptionTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=fieldservice;Username=fieldservice_app;Timeout=2;Pooling=false";

    [Fact]
    public async Task AFailureReturnsAGenericServerErrorProblemAndIsLoggedOnceWithItsStackAndCorrelationId()
    {
        await using var factory = Fixture.CreateFactory(factory =>
            factory.Settings[$"ConnectionStrings:{PersistenceRegistration.ConnectionStringName}"] = UnreachableDatabase);
        var marker = $"query-{Guid.NewGuid():N}";
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, UserRole.Administrator);

        using var response = await client.GetAsync($"/api/jobs?search={marker}");

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.InternalServerError, "server.error");
        var root = problem.RootElement;
        Assert.Equal("An unexpected error occurred.", root.GetProperty("title").GetString());
        Assert.Equal(client.LastCorrelationId, root.GetProperty("correlationId").GetString());
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$", root.GetProperty("traceId").GetString());
        Assert.Equal("/api/jobs", root.GetProperty("instance").GetString());
        Assert.False(root.TryGetProperty("detail", out _));
        var body = root.GetRawText();
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain(marker, body, StringComparison.Ordinal);

        var error = Assert.Single(factory.LogSink.Events, logEvent => logEvent.Level >= LogEventLevel.Error);
        Assert.NotNull(error.Exception);
        Assert.False(string.IsNullOrEmpty(error.Exception.StackTrace));
        Assert.Equal(error.Exception.GetType().FullName, CapturingLogSink.Scalar(error, "ExceptionType"));
        Assert.Equal(client.LastCorrelationId, CapturingLogSink.Scalar(error, "CorrelationId"));
        Assert.Equal("/api/jobs", CapturingLogSink.Scalar(error, "RequestPath"));
        Assert.All(factory.LogSink.Events, logEvent =>
            Assert.DoesNotContain(marker, CapturingLogSink.Text(logEvent), StringComparison.Ordinal));
        Assert.DoesNotContain(factory.LogSink.Events, logEvent =>
            CapturingLogSink.Scalar(logEvent, "SourceContext") is { } source
            && source.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            && logEvent.Level >= LogEventLevel.Error);
    }

    [Fact]
    public async Task AWriteTheDatabaseRefusesIsLoggedOnceAndRecordsNoEvent()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.Settings["Serilog:MinimumLevel:Default"] = "Information");
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, UserRole.Administrator);
        var body = new
        {
            title = "Refused write",
            description = "The creator does not exist, so the foreign key refuses the row.",
            priority = JobPriority.Normal,
            customerName = "Test Customer",
            siteAddress = "1 Test Way",
            scheduledStart = factory.Clock.GetUtcNow().AddDays(1),
            scheduledEnd = factory.Clock.GetUtcNow().AddDays(1).AddHours(2),
            checklistTemplateId = TestData.HvacTemplateId,
        };

        using var response = await client.PostJsonAsync("/api/jobs", body);

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.InternalServerError, "server.error");
        var error = Assert.Single(factory.LogSink.Events, logEvent => logEvent.Level >= LogEventLevel.Error);
        Assert.Equal(typeof(DbUpdateException).FullName, CapturingLogSink.Scalar(error, "ExceptionType"));
        Assert.Equal(client.LastCorrelationId, CapturingLogSink.Scalar(error, "CorrelationId"));
        factory.Events.AssertEmpty();
        await using var db = Fixture.CreateAppDbContext();
        Assert.Equal(0, await db.Jobs.CountAsync(CancellationToken));
    }
}
