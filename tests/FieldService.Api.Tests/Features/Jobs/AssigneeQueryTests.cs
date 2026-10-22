using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using FieldService.Api.Domain;
using FieldService.Api.Features;
using FieldService.Api.Features.Jobs;
using FieldService.Api.Persistence;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FieldService.Api.Tests.Features.Jobs;

[Collection(DatabaseCollection.Name)]
public sealed class AssigneeQueryTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string PasswordHashColumn = "password_hash";

    private static readonly Guid _templateId = ChecklistTemplateSeed.TemplateId(1);

    private static readonly string[] _assigneeColumns = ["user_id", "is_active", "is_on_shift"];

    private static readonly string[] _unreadColumns = [PasswordHashColumn, "email", "display_name", "phone_number", "trade", "last_signed_in_at"];

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task TheAssigneeQueryReadsOnlyTheTechnicianIdActiveStateAndOnShiftStateInOneCommand(bool isOnShift, bool isActive)
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var technician = await data.CreateTechnicianAsync(Trade.Electrical, CancellationToken, isOnShift, isActive);
        var recorder = new CommandRecorder();
        await using var db = CreateDbContext(recorder);

        var assignee = await AssigneeQuery.FindAsync(db, technician.UserId, CancellationToken);

        Assert.Equal(new JobAssignee(technician.UserId, isActive, isOnShift), assignee);
        Assert.Equal(isActive && isOnShift, assignee!.IsAvailableForAssignment);
        Assert.Equal(JobAssignee.Of(technician).IsAvailableForAssignment, assignee.IsAvailableForAssignment);
        var command = Assert.Single(recorder.Commands);
        Assert.Equal(_assigneeColumns, command.Columns);
        Assert.All(_unreadColumns, column => Assert.DoesNotContain(column, command.Text, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheAssigneeQueryFindsNothingForAnUnknownIdOrAUserWhoIsNotATechnician()
    {
        var administrator = await new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow()).CreateAdministratorAsync(CancellationToken);
        var recorder = new CommandRecorder();
        await using var db = CreateDbContext(recorder);

        foreach (var technicianId in new[] { Guid.CreateVersion7(), administrator.Id, Guid.Empty })
        {
            Assert.Null(await AssigneeQuery.FindAsync(db, technicianId, CancellationToken));
        }

        Assert.Equal(3, recorder.Commands.Count);
        Assert.All(recorder.Commands, command => Assert.Equal(_assigneeColumns, command.Columns));
        Assert.All(recorder.Commands, command => Assert.DoesNotContain(PasswordHashColumn, command.Text, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AssigningAJobReadsTheAssigneeWithoutThePasswordHashAndNoCommandOfTheRequestSelectsIt()
    {
        var recorder = new CommandRecorder();
        await using var factory = CreateFactory(recorder);
        var data = new TestData(Fixture, factory.Clock.GetUtcNow().AddMinutes(-30));
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, administrator);
        recorder.Clear();

        using var response = await client.PutJsonAsync(AssignJobTests.PathOf(job.Id), AssignJobTests.Body(technician.UserId, job.Version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = await response.Content.ReadFromJsonAsync<JobDetailsResponse>(ApiClient.Json, CancellationToken);
        Assert.Equal(new JobTechnicianReference(technician.UserId, technician.User.DisplayName, Trade.Electrical), details!.AssignedTechnician);
        Assert.Equal(new UserReference(technician.UserId, technician.User.DisplayName), details.History[^1].Technician);
        AssertAssigneeReadWithoutPasswordHash(recorder);
    }

    [Fact]
    public async Task CreatingAJobWithATechnicianReadsTheAssigneeWithoutThePasswordHashAndNoCommandOfTheRequestSelectsIt()
    {
        var recorder = new CommandRecorder();
        await using var factory = CreateFactory(recorder);
        var data = new TestData(Fixture, factory.Clock.GetUtcNow().AddMinutes(-30));
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, administrator);
        var scheduledStart = factory.Clock.GetUtcNow().AddDays(1);
        recorder.Clear();

        using var response = await client.PostJsonAsync("/api/jobs", new
        {
            title = "Rooftop unit not cooling",
            description = "The second-floor offices are warm by noon.",
            priority = "High",
            customerName = "Test Customer",
            siteAddress = "1 Test Way",
            scheduledStart,
            scheduledEnd = scheduledStart.AddHours(2),
            checklistTemplateId = TestData.HvacTemplateId,
            technicianId = technician.UserId,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var details = await response.Content.ReadFromJsonAsync<JobDetailsResponse>(ApiClient.Json, CancellationToken);
        Assert.Equal(JobStatus.Assigned, details!.Status);
        Assert.Equal(new JobTechnicianReference(technician.UserId, technician.User.DisplayName, Trade.Hvac), details.AssignedTechnician);
        AssertAssigneeReadWithoutPasswordHash(recorder);
    }

    private static void AssertAssigneeReadWithoutPasswordHash(CommandRecorder recorder)
    {
        var commands = recorder.Commands;
        var assigneeRead = Assert.Single(commands, command => command.Columns.SequenceEqual(_assigneeColumns));
        Assert.Contains("technicians", assigneeRead.Text, StringComparison.Ordinal);
        Assert.All(_unreadColumns, column => Assert.DoesNotContain(column, assigneeRead.Text, StringComparison.OrdinalIgnoreCase));
        Assert.All(commands, command =>
        {
            Assert.DoesNotContain(PasswordHashColumn, command.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(PasswordHashColumn, command.Columns);
        });
    }

    private ApiFactory CreateFactory(CommandRecorder recorder) =>
        Fixture.CreateFactory(factory => factory.TestServices = services =>
            services.ConfigureDbContext<FieldServiceDbContext>(options => options.AddInterceptors(recorder)));

    private FieldServiceDbContext CreateDbContext(CommandRecorder recorder)
    {
        var options = new DbContextOptionsBuilder<FieldServiceDbContext>();
        options.UseFieldServiceDatabase(Fixture.AppConnectionString).AddInterceptors(recorder);
        return new FieldServiceDbContext(options.Options);
    }

    private sealed record RecordedCommand(string Text, IReadOnlyList<string> Columns);

    private sealed class CommandRecorder : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<RecordedCommand> _commands = new();

        public IReadOnlyList<RecordedCommand> Commands => [.. _commands];

        public void Clear() => _commands.Clear();

        public override DbDataReader ReaderExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result)
        {
            Record(command, result);
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            Record(command, result);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            Record(command, reader: null);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(command, reader: null);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result)
        {
            Record(command, reader: null);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Record(command, reader: null);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command, DbDataReader? reader) =>
            _commands.Enqueue(new RecordedCommand(
                command.CommandText,
                reader is null ? [] : [.. Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)]));
    }
}
