using System.Buffers;
using System.Globalization;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Contracts;

public sealed class EventFixtureTests
{
    public const string CreatedFixture = "job.created.v1.json";

    public const string AssignedFixture = "job.assigned.v1.json";

    public const string StatusChangedFixture = "job.status-changed.v1.json";

    private static readonly string[] _envelopeMembers = ["id", "type", "version", "occurredAt", "correlationId", "data"];

    private static readonly string[] _dataMembers = ["jobId", "jobNumber", "status", "previousStatus"];

    public static TheoryData<string> InternalFixtures => new() { CreatedFixture, AssignedFixture, StatusChangedFixture };

    [Fact]
    public void TheInternalFolderHoldsExactlyTheThreeVersionOneFixtures()
    {
        var names = Directory.GetFiles(InternalDirectory)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal([AssignedFixture, CreatedFixture, StatusChangedFixture], names);
    }

    [Theory]
    [MemberData(nameof(InternalFixtures))]
    public void TheApiSerializerWritesEachFixtureByteForByteFromTheFixturesOwnFieldValues(string name)
    {
        var fixture = Read(name);

        var serialized = JobEventSerializer.Serialize(fixture.ToJobEvent());

        Assert.Equal(Compact(name), serialized);
    }

    [Theory]
    [MemberData(nameof(InternalFixtures))]
    public void EachFixtureKeepsTheEnvelopeMemberOrderTheApisTypeAndVersionAndMillisecondInstants(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(PathOf(name)));
        var root = document.RootElement;
        var fixture = Read(name);

        Assert.Equal(_envelopeMembers, root.EnumerateObject().Select(member => member.Name));
        Assert.Equal(_dataMembers, root.GetProperty("data").EnumerateObject().Select(member => member.Name));
        Assert.Equal($"{fixture.Type}.v{fixture.Version}.json", name);
        Assert.Contains(fixture.Type, new[] { JobEventTypes.Created, JobEventTypes.Assigned, JobEventTypes.StatusChanged });
        Assert.Equal(JobEvent.CurrentVersion, fixture.Version);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", root.GetProperty("occurredAt").GetString());
        Assert.Equal(7, fixture.Id.Version);
        Assert.Equal(7, fixture.JobId.Version);
        Assert.Equal(fixture.OccurredAt, TimeOf(fixture.Id));
    }

    [Fact]
    public void TheApiEventFactoriesFillEveryFixtureMemberFromTheCommittedJobApartFromTheGeneratedIdentifiers()
    {
        var created = Read(CreatedFixture);
        var assigned = Read(AssignedFixture);
        var started = Read(StatusChangedFixture);
        var administrator = TestData.NewUser(UserRole.Administrator, created.OccurredAt);
        var technician = TestData.NewTechnician(Trade.Hvac, created.OccurredAt);
        var administratorActor = JobActor.Administrator(administrator.Id);

        var createdJob = TestData.NewJob(administrator.Id, TestData.HvacTemplateId, created.OccurredAt);
        var createdEvent = JobEvent.Created(createdJob, CorrelationOf(created));

        var assignedJob = TestData.NewJob(administrator.Id, TestData.HvacTemplateId, TimeOf(assigned.JobId));
        TestData.Require(assignedJob.AssignTo(technician, administratorActor, assigned.OccurredAt));
        var assignedEvent = JobEvent.Assigned(assignedJob, JobStatus.Unassigned, CorrelationOf(assigned));

        var startedJob = TestData.NewJob(administrator.Id, TestData.HvacTemplateId, TimeOf(started.JobId));
        TestData.Require(startedJob.AssignTo(technician, administratorActor, TimeOf(started.JobId).AddMinutes(5)));
        TestData.Require(startedJob.ChangeStatus(JobStatus.InProgress, note: null, JobActor.Technician(technician.UserId), started.OccurredAt));
        var startedEvent = JobEvent.StatusChanged(startedJob, JobStatus.Assigned, CorrelationOf(started));

        foreach (var (fixture, job, jobEvent) in new[] { (created, createdJob, createdEvent), (assigned, assignedJob, assignedEvent), (started, startedJob, startedEvent) })
        {
            Assert.Equal(7, jobEvent.Id.Version);
            Assert.Equal(job.UpdatedAt, TimeOf(jobEvent.Id));
            Assert.Equal(job.Id, jobEvent.JobId);
            Assert.Equal(TimeOf(fixture.JobId), TimeOf(jobEvent.JobId));
            var aligned = jobEvent with { Id = fixture.Id, JobId = fixture.JobId, JobNumber = fixture.JobNumber };
            Assert.Equal(Compact($"{fixture.Type}.v1.json"), JobEventSerializer.Serialize(aligned));
        }
    }

    public static string InternalDirectory => Path.Combine(RepositoryPaths.Root, "docs", "contracts", "events", "internal");

    public static string PathOf(string name) => Path.Combine(InternalDirectory, name);

    public static byte[] Compact(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(PathOf(name)));
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            document.RootElement.WriteTo(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static Fixture Read(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(PathOf(name)));
        var root = document.RootElement;
        var data = root.GetProperty("data");
        var previous = data.GetProperty("previousStatus");
        return new Fixture(
            Guid.Parse(root.GetProperty("id").GetString()!),
            root.GetProperty("type").GetString()!,
            root.GetProperty("version").GetInt32(),
            DateTimeOffset.ParseExact(root.GetProperty("occurredAt").GetString()!, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
            root.GetProperty("correlationId").GetString()!,
            Guid.Parse(data.GetProperty("jobId").GetString()!),
            data.GetProperty("jobNumber").GetInt64(),
            Enum.Parse<JobStatus>(data.GetProperty("status").GetString()!),
            previous.ValueKind == JsonValueKind.Null ? null : Enum.Parse<JobStatus>(previous.GetString()!));
    }

    public static DateTimeOffset TimeOf(Guid uuidV7)
    {
        var text = uuidV7.ToString("N");
        var milliseconds = long.Parse(text[..12], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    private static CorrelationId CorrelationOf(Fixture fixture)
    {
        Assert.True(CorrelationId.TryParse(fixture.CorrelationId, out var correlationId));
        return correlationId;
    }

    public sealed record Fixture(
        Guid Id,
        string Type,
        int Version,
        DateTimeOffset OccurredAt,
        string CorrelationId,
        Guid JobId,
        long JobNumber,
        JobStatus Status,
        JobStatus? PreviousStatus)
    {
        public JobEvent ToJobEvent() =>
            new(Id, Type, Version, OccurredAt, CorrelationId, JobId, JobNumber, Status, PreviousStatus);
    }
}
