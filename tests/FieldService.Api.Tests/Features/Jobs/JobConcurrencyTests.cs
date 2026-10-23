using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Features.Inspections;
using FieldService.Api.Features.Jobs;
using FieldService.Api.Persistence;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Tests.Features.Inspections;
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

    private static readonly Guid _hvacTemplateId = ChecklistTemplateSeed.TemplateId(2);

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

    [Fact]
    public async Task TwoFirstSaveInspectionCallsThatBothReachSaveChangesGiveOne200AndOne409WithOneInspectionRow()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var barrier = new SaveChangesBarrier(participants: 2);
            await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services =>
                services.ConfigureDbContext<FieldServiceDbContext>(options => options.AddInterceptors(barrier)));

            await RaceFirstSavesAsync(factory);

            Assert.Equal(2, barrier.Arrived);
        }
    }

    [Fact]
    public async Task TwoFirstSaveInspectionCallsReleasedTogetherWithDifferentAnswersGiveOne200AndOne409WithOneInspectionRow()
    {
        for (var round = 0; round < Rounds; round++)
        {
            Fixture.Factory.Events.Clear();

            await RaceFirstSavesAsync(Fixture.Factory);
        }
    }

    [Fact]
    public async Task TwoSavesThatAddTheSameNewAnswersInOppositeOrdersGiveOne200AndOne409AndNeverA500()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var barrier = new SaveChangesBarrier(participants: 2);
            await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services =>
                services.ConfigureDbContext<FieldServiceDbContext>(options => options.AddInterceptors(barrier)));
            var (technician, job) = await ArrangeInspectionAsync(JobStatus.InProgress, answered: 6);
            var supplyAir = ChecklistTemplateSeed.ReadingFieldId(2, 8, 1);
            var forward = SaveInspectionTests.Body(
                job.Version,
                SaveInspectionTests.Answer(7, "Pass"),
                SaveInspectionTests.Answer(8, "Pass", null, SaveInspectionTests.Reading(supplyAir, 54m)));
            var backward = SaveInspectionTests.Body(
                job.Version,
                SaveInspectionTests.Answer(8, "Fail", "Supply air too warm", SaveInspectionTests.Reading(supplyAir, 71m)),
                SaveInspectionTests.Answer(7, "NotApplicable", "No condensate line"));

            using var firstDevice = DeviceOf(factory, technician.User);
            using var secondDevice = DeviceOf(factory, technician.User);
            var responses = await SendTogetherAsync(
                () => firstDevice.PutJsonAsync(SaveInspectionTests.PathOf(job.Id), forward),
                () => secondDevice.PutJsonAsync(SaveInspectionTests.PathOf(job.Id), backward));

            try
            {
                Assert.Equal(2, barrier.Arrived);
                Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).OrderBy(status => (int)status));
                await ApiClient.AssertProblemAsync(
                    responses.Single(response => response.StatusCode == HttpStatusCode.Conflict),
                    HttpStatusCode.Conflict,
                    "concurrency.conflict");
                await using var db = Fixture.CreateAppDbContext();
                var stored = await db.Inspections.AsNoTracking().WithAnswersAndReadings().SingleAsync(entry => entry.JobId == job.Id, CancellationToken);
                Assert.Equal(8, stored.Answers.Count);
                var forwardWon = responses[0].StatusCode == HttpStatusCode.OK;
                var item8 = stored.Answers.Single(answer => answer.ChecklistItemId == SaveInspectionTests.Item(8));
                Assert.Equal(forwardWon ? InspectionResult.Pass : InspectionResult.Fail, item8.Result);
                Assert.Equal(forwardWon ? 54m : 71m, Assert.Single(item8.Readings).Value);
                factory.Events.AssertEmpty();
            }
            finally
            {
                DisposeAll(responses);
            }
        }
    }

    [Fact]
    public async Task ASaveAndASubmissionThatBothReachSaveChangesGiveOne200AndOne409AndASubmittedInspectionNeverChanges()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var barrier = new SaveChangesBarrier(participants: 2);
            await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services =>
                services.ConfigureDbContext<FieldServiceDbContext>(options => options.AddInterceptors(barrier)));

            await RaceSaveAgainstSubmitAsync(factory);

            Assert.Equal(2, barrier.Arrived);
        }
    }

    [Fact]
    public async Task ASaveAndASubmissionReleasedTogetherGiveOne200AndOne409AndASubmittedInspectionNeverChanges()
    {
        for (var round = 0; round < Rounds; round++)
        {
            Fixture.Factory.Events.Clear();

            await RaceSaveAgainstSubmitAsync(Fixture.Factory);
        }
    }

    [Fact]
    public async Task TwoSubmissionsThatBothReachSaveChangesGiveOne200AndOne409WithOneCompletedRowAndOneEvent()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var barrier = new SaveChangesBarrier(participants: 2);
            await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services =>
                services.ConfigureDbContext<FieldServiceDbContext>(options => options.AddInterceptors(barrier)));

            var statuses = await RaceSubmissionsAsync(factory);

            Assert.Equal(2, barrier.Arrived);
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses.OrderBy(status => (int)status));
        }
    }

    [Fact]
    public async Task TwoSubmissionsReleasedTogetherGiveOne200AndAnother200OrA409WithOneCompletedRowAndOneEvent()
    {
        for (var round = 0; round < Rounds; round++)
        {
            Fixture.Factory.Events.Clear();

            await RaceSubmissionsAsync(Fixture.Factory);
        }
    }

    private async Task RaceFirstSavesAsync(ApiFactory factory)
    {
        var (technician, job) = await ArrangeInspectionAsync(JobStatus.InProgress, answered: 0);

        using var firstDevice = DeviceOf(factory, technician.User);
        using var secondDevice = DeviceOf(factory, technician.User);
        var responses = await SendTogetherAsync(
            () => firstDevice.PutJsonAsync(SaveInspectionTests.PathOf(job.Id), SaveInspectionTests.Body(job.Version, SaveInspectionTests.Answer(1, "Pass"))),
            () => secondDevice.PutJsonAsync(SaveInspectionTests.PathOf(job.Id), SaveInspectionTests.Body(job.Version, SaveInspectionTests.Answer(2, "Pass"))));

        try
        {
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).OrderBy(status => (int)status));
            await ApiClient.AssertProblemAsync(
                responses.Single(response => response.StatusCode == HttpStatusCode.Conflict),
                HttpStatusCode.Conflict,
                "concurrency.conflict");
            var winner = await responses.Single(response => response.StatusCode == HttpStatusCode.OK)
                .Content.ReadFromJsonAsync<InspectionResponse>(ApiClient.Json, CancellationToken);
            await using var db = Fixture.CreateAppDbContext();
            var inspections = await db.Inspections.AsNoTracking().WithAnswersAndReadings().Where(entry => entry.JobId == job.Id).ToListAsync(CancellationToken);
            var stored = Assert.Single(inspections);
            var answer = Assert.Single(stored.Answers);
            Assert.Equal(responses[0].StatusCode == HttpStatusCode.OK ? SaveInspectionTests.Item(1) : SaveInspectionTests.Item(2), answer.ChecklistItemId);
            Assert.Equal(winner!.Version, (await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == job.Id, CancellationToken)).Version);
            factory.Events.AssertEmpty();
        }
        finally
        {
            DisposeAll(responses);
        }
    }

    private async Task RaceSaveAgainstSubmitAsync(ApiFactory factory)
    {
        var (technician, job) = await ArrangeInspectionAsync(JobStatus.InProgress, answered: 10);

        using var firstDevice = DeviceOf(factory, technician.User);
        using var secondDevice = DeviceOf(factory, technician.User);
        var responses = await SendTogetherAsync(
            () => firstDevice.PutJsonAsync(
                SaveInspectionTests.PathOf(job.Id),
                SaveInspectionTests.Body(job.Version, SaveInspectionTests.Answer(5, "Fail", "Bearing noise at start-up."))),
            () => secondDevice.PostJsonAsync(SubmitJobTests.PathOf(job.Id), SubmitJobTests.Body("Done", true, job.Version)));

        try
        {
            var (save, submit) = (responses[0], responses[1]);
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).OrderBy(status => (int)status));
            var saveWon = save.StatusCode == HttpStatusCode.OK;
            if (saveWon)
            {
                await ApiClient.AssertProblemAsync(submit, HttpStatusCode.Conflict, "concurrency.conflict");
            }
            else
            {
                Assert.Contains(await CodeOfAsync(save), new[] { "concurrency.conflict", "inspection.locked" });
            }

            await using var db = Fixture.CreateAppDbContext();
            var storedJob = await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == job.Id, CancellationToken);
            var stored = await db.Inspections.AsNoTracking().WithAnswersAndReadings().SingleAsync(entry => entry.JobId == job.Id, CancellationToken);
            var fanMotor = stored.Answers.Single(answer => answer.ChecklistItemId == SaveInspectionTests.Item(5));
            var completedEntries = await db.JobHistory.CountAsync(
                entry => entry.JobId == job.Id && entry.Action == JobHistoryAction.Completed,
                CancellationToken);
            Assert.Equal(saveWon ? JobStatus.InProgress : JobStatus.Completed, storedJob.Status);
            Assert.Equal(saveWon, stored.SubmittedAt is null);
            Assert.Equal(saveWon ? InspectionResult.Fail : InspectionResult.Pass, fanMotor.Result);
            Assert.Equal(saveWon ? 0 : 1, completedEntries);
            Assert.Equal(saveWon ? 0 : 1, factory.Events.Events.Count);
        }
        finally
        {
            DisposeAll(responses);
        }
    }

    private async Task<HttpStatusCode[]> RaceSubmissionsAsync(ApiFactory factory)
    {
        var (technician, job) = await ArrangeInspectionAsync(JobStatus.InProgress, answered: 10);

        using var firstDevice = DeviceOf(factory, technician.User);
        using var secondDevice = DeviceOf(factory, technician.User);
        var responses = await SendTogetherAsync(
            () => firstDevice.PostJsonAsync(SubmitJobTests.PathOf(job.Id), SubmitJobTests.Body("First device", true, job.Version)),
            () => secondDevice.PostJsonAsync(SubmitJobTests.PathOf(job.Id), SubmitJobTests.Body("Second device", true, job.Version)));

        try
        {
            Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
            foreach (var response in responses.Where(response => response.StatusCode != HttpStatusCode.OK))
            {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Contains(await CodeOfAsync(response), new[] { "concurrency.conflict", "inspection.locked" });
            }

            await using var db = Fixture.CreateAppDbContext();
            var storedJob = await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == job.Id, CancellationToken);
            var stored = await db.Inspections.AsNoTracking().SingleAsync(entry => entry.JobId == job.Id, CancellationToken);
            Assert.Equal(JobStatus.Completed, storedJob.Status);
            Assert.Equal(storedJob.CompletedAt, stored.SubmittedAt);
            Assert.Contains(stored.TechnicianNotes, new[] { "First device", "Second device" });
            Assert.Equal(
                1,
                await db.JobHistory.CountAsync(entry => entry.JobId == job.Id && entry.Action == JobHistoryAction.Completed, CancellationToken));
            var recorded = Assert.Single(factory.Events.Events);
            Assert.Equal(JobStatus.Completed, recorded.Status);
            Assert.Equal(JobStatus.InProgress, recorded.PreviousStatus);
            return [.. responses.Select(response => response.StatusCode)];
        }
        finally
        {
            DisposeAll(responses);
        }
    }

    private async Task<(Technician Technician, Job Job)> ArrangeInspectionAsync(JobStatus status, int answered)
    {
        var earlier = Now.AddMinutes(-30);
        var administrator = await new TestData(Fixture, earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await new TestData(Fixture, earlier).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await new TestData(Fixture, earlier.AddMinutes(5)).MoveToAsync(
            await new TestData(Fixture, earlier).CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken),
            technician,
            status,
            CancellationToken);
        if (answered > 0)
        {
            await new TestData(Fixture, earlier.AddMinutes(10)).SaveInspectionAsync(
                job,
                TestData.PassingAnswers(TestData.ChecklistOf(_hvacTemplateId), answered),
                CancellationToken);
        }

        await using var db = Fixture.CreateAppDbContext();
        return (technician, await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == job.Id, CancellationToken));
    }

    private static ApiClient DeviceOf(ApiFactory factory, User user)
    {
        var device = factory.CreateApiClient();
        device.AccessToken = TestTokens.For(factory, user);
        return device;
    }

    private static async Task<HttpResponseMessage[]> SendTogetherAsync(
        Func<Task<HttpResponseMessage>> first,
        Func<Task<HttpResponseMessage>> second)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstCall = AfterAsync(gate.Task, first);
        var secondCall = AfterAsync(gate.Task, second);
        gate.SetResult();
        return await Task.WhenAll(firstCall, secondCall);
    }

    private static async Task<HttpResponseMessage> AfterAsync(Task gate, Func<Task<HttpResponseMessage>> send)
    {
        await gate;
        return await send();
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return problem.RootElement.GetProperty("code").GetString();
    }

    private static void DisposeAll(IEnumerable<HttpResponseMessage> responses)
    {
        foreach (var response in responses)
        {
            response.Dispose();
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
