using System.Net;
using System.Net.Http.Json;
using FieldService.Api.Domain;
using FieldService.Api.Features.Jobs;
using FieldService.Api.Persistence;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FieldService.Api.Tests.Features.Jobs;

[Collection(DatabaseCollection.Name)]
public sealed class JobConcurrencyTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const int Rounds = 5;

    private static readonly Guid _templateId = ChecklistTemplateSeed.TemplateId(1);

    private static readonly TimeSpan _barrierTimeout = TimeSpan.FromSeconds(10);

    private DateTimeOffset Now => UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());

    [Fact]
    public async Task TwoAssignJobCallsThatBothReachSaveChangesWithTheSameVersionGiveOne200AndOne409WithOneHistoryRowAndOneEvent()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var barrier = new SaveChangesBarrier(participants: 2);
            await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services =>
                services.ConfigureDbContext<FieldServiceDbContext>(options => options.AddInterceptors(barrier)));

            await RaceAsync(factory);

            Assert.Equal(2, barrier.Arrived);
        }
    }

    [Fact]
    public async Task TwoAssignJobCallsReleasedTogetherWithTheSameVersionGiveOne200AndOne409WithOneHistoryRowAndOneEvent()
    {
        for (var round = 0; round < Rounds; round++)
        {
            Fixture.Factory.Events.Clear();

            await RaceAsync(Fixture.Factory);
        }
    }

    [Fact]
    public async Task TwoStartsThatBothReachSaveChangesWithTheSameVersionGiveOne200AndOne409WithOneStartedRowAndOneEvent()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var barrier = new SaveChangesBarrier(participants: 2);
            await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services =>
                services.ConfigureDbContext<FieldServiceDbContext>(options => options.AddInterceptors(barrier)));

            var statuses = await RaceStartsAsync(factory);

            Assert.Equal(2, barrier.Arrived);
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses.OrderBy(status => (int)status));
        }
    }

    [Fact]
    public async Task TwoStartsReleasedTogetherWithTheSameVersionGiveOne200AndAnother200OrA409WithOneStartedRowAndOneEvent()
    {
        for (var round = 0; round < Rounds; round++)
        {
            Fixture.Factory.Events.Clear();

            await RaceStartsAsync(Fixture.Factory);
        }
    }

    private async Task<HttpStatusCode[]> RaceStartsAsync(ApiFactory factory)
    {
        var earlier = Now.AddMinutes(-30);
        var data = new TestData(Fixture, earlier);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await data.MoveToAsync(
            await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken),
            technician,
            JobStatus.Assigned,
            CancellationToken);
        using var firstDevice = factory.CreateApiClient();
        using var secondDevice = factory.CreateApiClient();
        firstDevice.AccessToken = TestTokens.For(factory, technician.User);
        secondDevice.AccessToken = TestTokens.For(factory, technician.User);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstCall = StartAfterAsync(gate.Task, firstDevice, job);
        var secondCall = StartAfterAsync(gate.Task, secondDevice, job);
        gate.SetResult();
        var responses = await Task.WhenAll(firstCall, secondCall);

        try
        {
            var successes = responses.Where(response => response.StatusCode == HttpStatusCode.OK).ToList();
            Assert.NotEmpty(successes);
            var others = responses.Where(response => response.StatusCode != HttpStatusCode.OK).ToList();
            foreach (var other in others)
            {
                await ApiClient.AssertProblemAsync(other, HttpStatusCode.Conflict, "concurrency.conflict");
            }

            var bodies = new List<JobDetailsResponse>();
            foreach (var success in successes)
            {
                bodies.Add((await success.Content.ReadFromJsonAsync<JobDetailsResponse>(ApiClient.Json, CancellationToken))!);
            }

            Assert.All(bodies, body => Assert.Equal(JobStatus.InProgress, body.Status));
            Assert.Single(bodies.Select(body => body.Version).Distinct());

            await using var db = Fixture.CreateAppDbContext();
            var stored = await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == job.Id, CancellationToken);
            Assert.Equal(JobStatus.InProgress, stored.Status);
            Assert.Equal(bodies[0].Version, stored.Version);
            Assert.Equal(bodies[0].StartedAt, stored.StartedAt);
            Assert.Equal(
                1,
                await db.JobHistory.CountAsync(
                    entry => entry.JobId == job.Id && entry.Action == JobHistoryAction.Started,
                    CancellationToken));
            var recorded = Assert.Single(factory.Events.Events);
            Assert.Equal(JobEventTypes.StatusChanged, recorded.Type);
            Assert.Equal(job.Id, recorded.JobId);
            Assert.Equal(JobStatus.Assigned, recorded.PreviousStatus);
            return [.. responses.Select(response => response.StatusCode)];
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private static async Task<HttpResponseMessage> StartAfterAsync(Task gate, ApiClient client, Job job)
    {
        await gate;
        return await client.PutJsonAsync(
            UpdateJobStatusTests.PathOf(job.Id),
            UpdateJobStatusTests.Body(JobStatus.InProgress, note: null, job.Version));
    }

    private async Task RaceAsync(ApiFactory factory)
    {
        var earlier = Now.AddMinutes(-30);
        var data = new TestData(Fixture, earlier);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var first = await data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var second = await data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var firstClient = factory.CreateApiClient();
        using var secondClient = factory.CreateApiClient();
        firstClient.AccessToken = TestTokens.For(factory, administrator);
        secondClient.AccessToken = TestTokens.For(factory, administrator);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstCall = SendAfterAsync(gate.Task, firstClient, job, first.UserId);
        var secondCall = SendAfterAsync(gate.Task, secondClient, job, second.UserId);
        gate.SetResult();
        var responses = await Task.WhenAll(firstCall, secondCall);

        try
        {
            var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var loser = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            await ApiClient.AssertProblemAsync(loser, HttpStatusCode.Conflict, "concurrency.conflict");
            var assigned = await winner.Content.ReadFromJsonAsync<JobDetailsResponse>(ApiClient.Json, CancellationToken);
            var winningTechnician = winner == responses[0] ? first.UserId : second.UserId;
            Assert.Equal(winningTechnician, assigned!.AssignedTechnician?.Id);

            await using var db = Fixture.CreateAppDbContext();
            var stored = await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == job.Id, CancellationToken);
            Assert.Equal(JobStatus.Assigned, stored.Status);
            Assert.Equal(winningTechnician, stored.AssignedTechnicianId);
            Assert.Equal(assigned.Version, stored.Version);
            var assignedEntries = await db.JobHistory.AsNoTracking()
                .Where(entry => entry.JobId == job.Id && entry.Action == JobHistoryAction.Assigned)
                .ToListAsync(CancellationToken);
            Assert.Equal(winningTechnician, Assert.Single(assignedEntries).TechnicianId);
            var recorded = Assert.Single(factory.Events.Events);
            Assert.Equal(JobEventTypes.Assigned, recorded.Type);
            Assert.Equal(job.Id, recorded.JobId);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private static async Task<HttpResponseMessage> SendAfterAsync(Task gate, ApiClient client, Job job, Guid technicianId)
    {
        await gate;
        return await client.PutJsonAsync(AssignJobTests.PathOf(job.Id), AssignJobTests.Body(technicianId, job.Version));
    }

    private sealed class SaveChangesBarrier(int participants) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _arrived;

        public int Arrived => Volatile.Read(ref _arrived);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrived) >= participants)
            {
                _allArrived.TrySetResult();
            }

            await _allArrived.Task.WaitAsync(_barrierTimeout, cancellationToken);
            return result;
        }
    }
}
