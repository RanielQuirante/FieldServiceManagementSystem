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
