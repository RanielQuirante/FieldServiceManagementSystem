using FieldService.Api.Domain;
using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Domain;

public sealed class JobTransitionMatrixTests
{
    public const string Admin = "Administrator";

    public const string Assignee = "AssignedTechnician";

    public const string Other = "OtherTechnician";

    private const string Invalid = DomainErrors.InvalidTransitionCode;

    private const string NotAssignable = DomainErrors.NotAssignableCode;

    private static readonly DateTimeOffset _start = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<JobStatus, JobStatus, string, string> Matrix => new()
    {
        { JobStatus.Unassigned, JobStatus.Unassigned, Admin, Invalid },
        { JobStatus.Unassigned, JobStatus.Assigned, Admin, "Assigned" },
        { JobStatus.Unassigned, JobStatus.InProgress, Admin, Invalid },
        { JobStatus.Unassigned, JobStatus.OnHold, Admin, Invalid },
        { JobStatus.Unassigned, JobStatus.Completed, Admin, Invalid },
        { JobStatus.Unassigned, JobStatus.Cancelled, Admin, "Cancelled" },
        { JobStatus.Assigned, JobStatus.Unassigned, Admin, Invalid },
        { JobStatus.Assigned, JobStatus.Assigned, Admin, "Assigned" },
        { JobStatus.Assigned, JobStatus.InProgress, Admin, Invalid },
        { JobStatus.Assigned, JobStatus.OnHold, Admin, Invalid },
        { JobStatus.Assigned, JobStatus.Completed, Admin, Invalid },
        { JobStatus.Assigned, JobStatus.Cancelled, Admin, "Cancelled" },
        { JobStatus.InProgress, JobStatus.Unassigned, Admin, Invalid },
        { JobStatus.InProgress, JobStatus.Assigned, Admin, NotAssignable },
        { JobStatus.InProgress, JobStatus.InProgress, Admin, Invalid },
        { JobStatus.InProgress, JobStatus.OnHold, Admin, Invalid },
        { JobStatus.InProgress, JobStatus.Completed, Admin, Invalid },
        { JobStatus.InProgress, JobStatus.Cancelled, Admin, "Cancelled" },
        { JobStatus.OnHold, JobStatus.Unassigned, Admin, Invalid },
        { JobStatus.OnHold, JobStatus.Assigned, Admin, NotAssignable },
        { JobStatus.OnHold, JobStatus.InProgress, Admin, Invalid },
        { JobStatus.OnHold, JobStatus.OnHold, Admin, Invalid },
        { JobStatus.OnHold, JobStatus.Completed, Admin, Invalid },
        { JobStatus.OnHold, JobStatus.Cancelled, Admin, "Cancelled" },
        { JobStatus.Completed, JobStatus.Unassigned, Admin, Invalid },
        { JobStatus.Completed, JobStatus.Assigned, Admin, NotAssignable },
        { JobStatus.Completed, JobStatus.InProgress, Admin, Invalid },
        { JobStatus.Completed, JobStatus.OnHold, Admin, Invalid },
        { JobStatus.Completed, JobStatus.Completed, Admin, Invalid },
        { JobStatus.Completed, JobStatus.Cancelled, Admin, Invalid },
        { JobStatus.Cancelled, JobStatus.Unassigned, Admin, Invalid },
        { JobStatus.Cancelled, JobStatus.Assigned, Admin, NotAssignable },
        { JobStatus.Cancelled, JobStatus.InProgress, Admin, Invalid },
        { JobStatus.Cancelled, JobStatus.OnHold, Admin, Invalid },
        { JobStatus.Cancelled, JobStatus.Completed, Admin, Invalid },
        { JobStatus.Cancelled, JobStatus.Cancelled, Admin, Invalid },
        { JobStatus.Unassigned, JobStatus.Unassigned, Assignee, Invalid },
        { JobStatus.Unassigned, JobStatus.Assigned, Assignee, Invalid },
        { JobStatus.Unassigned, JobStatus.InProgress, Assignee, Invalid },
        { JobStatus.Unassigned, JobStatus.OnHold, Assignee, Invalid },
        { JobStatus.Unassigned, JobStatus.Completed, Assignee, Invalid },
        { JobStatus.Unassigned, JobStatus.Cancelled, Assignee, Invalid },
        { JobStatus.Assigned, JobStatus.Unassigned, Assignee, Invalid },
        { JobStatus.Assigned, JobStatus.Assigned, Assignee, Invalid },
        { JobStatus.Assigned, JobStatus.InProgress, Assignee, "Started" },
        { JobStatus.Assigned, JobStatus.OnHold, Assignee, Invalid },
        { JobStatus.Assigned, JobStatus.Completed, Assignee, Invalid },
        { JobStatus.Assigned, JobStatus.Cancelled, Assignee, Invalid },
        { JobStatus.InProgress, JobStatus.Unassigned, Assignee, Invalid },
        { JobStatus.InProgress, JobStatus.Assigned, Assignee, Invalid },
        { JobStatus.InProgress, JobStatus.InProgress, Assignee, Invalid },
        { JobStatus.InProgress, JobStatus.OnHold, Assignee, "PutOnHold" },
        { JobStatus.InProgress, JobStatus.Completed, Assignee, "Completed" },
        { JobStatus.InProgress, JobStatus.Cancelled, Assignee, Invalid },
        { JobStatus.OnHold, JobStatus.Unassigned, Assignee, Invalid },
        { JobStatus.OnHold, JobStatus.Assigned, Assignee, Invalid },
        { JobStatus.OnHold, JobStatus.InProgress, Assignee, "Resumed" },
        { JobStatus.OnHold, JobStatus.OnHold, Assignee, Invalid },
        { JobStatus.OnHold, JobStatus.Completed, Assignee, Invalid },
        { JobStatus.OnHold, JobStatus.Cancelled, Assignee, Invalid },
        { JobStatus.Completed, JobStatus.Unassigned, Assignee, Invalid },
        { JobStatus.Completed, JobStatus.Assigned, Assignee, Invalid },
        { JobStatus.Completed, JobStatus.InProgress, Assignee, Invalid },
        { JobStatus.Completed, JobStatus.OnHold, Assignee, Invalid },
        { JobStatus.Completed, JobStatus.Completed, Assignee, Invalid },
        { JobStatus.Completed, JobStatus.Cancelled, Assignee, Invalid },
        { JobStatus.Cancelled, JobStatus.Unassigned, Assignee, Invalid },
        { JobStatus.Cancelled, JobStatus.Assigned, Assignee, Invalid },
        { JobStatus.Cancelled, JobStatus.InProgress, Assignee, Invalid },
        { JobStatus.Cancelled, JobStatus.OnHold, Assignee, Invalid },
        { JobStatus.Cancelled, JobStatus.Completed, Assignee, Invalid },
        { JobStatus.Cancelled, JobStatus.Cancelled, Assignee, Invalid },
        { JobStatus.Unassigned, JobStatus.Unassigned, Other, Invalid },
        { JobStatus.Unassigned, JobStatus.Assigned, Other, Invalid },
        { JobStatus.Unassigned, JobStatus.InProgress, Other, Invalid },
        { JobStatus.Unassigned, JobStatus.OnHold, Other, Invalid },
        { JobStatus.Unassigned, JobStatus.Completed, Other, Invalid },
        { JobStatus.Unassigned, JobStatus.Cancelled, Other, Invalid },
        { JobStatus.Assigned, JobStatus.Unassigned, Other, Invalid },
        { JobStatus.Assigned, JobStatus.Assigned, Other, Invalid },
        { JobStatus.Assigned, JobStatus.InProgress, Other, Invalid },
        { JobStatus.Assigned, JobStatus.OnHold, Other, Invalid },
        { JobStatus.Assigned, JobStatus.Completed, Other, Invalid },
        { JobStatus.Assigned, JobStatus.Cancelled, Other, Invalid },
        { JobStatus.InProgress, JobStatus.Unassigned, Other, Invalid },
        { JobStatus.InProgress, JobStatus.Assigned, Other, Invalid },
        { JobStatus.InProgress, JobStatus.InProgress, Other, Invalid },
        { JobStatus.InProgress, JobStatus.OnHold, Other, Invalid },
        { JobStatus.InProgress, JobStatus.Completed, Other, Invalid },
        { JobStatus.InProgress, JobStatus.Cancelled, Other, Invalid },
        { JobStatus.OnHold, JobStatus.Unassigned, Other, Invalid },
        { JobStatus.OnHold, JobStatus.Assigned, Other, Invalid },
        { JobStatus.OnHold, JobStatus.InProgress, Other, Invalid },
        { JobStatus.OnHold, JobStatus.OnHold, Other, Invalid },
        { JobStatus.OnHold, JobStatus.Completed, Other, Invalid },
        { JobStatus.OnHold, JobStatus.Cancelled, Other, Invalid },
        { JobStatus.Completed, JobStatus.Unassigned, Other, Invalid },
        { JobStatus.Completed, JobStatus.Assigned, Other, Invalid },
        { JobStatus.Completed, JobStatus.InProgress, Other, Invalid },
        { JobStatus.Completed, JobStatus.OnHold, Other, Invalid },
        { JobStatus.Completed, JobStatus.Completed, Other, Invalid },
        { JobStatus.Completed, JobStatus.Cancelled, Other, Invalid },
        { JobStatus.Cancelled, JobStatus.Unassigned, Other, Invalid },
        { JobStatus.Cancelled, JobStatus.Assigned, Other, Invalid },
        { JobStatus.Cancelled, JobStatus.InProgress, Other, Invalid },
        { JobStatus.Cancelled, JobStatus.OnHold, Other, Invalid },
        { JobStatus.Cancelled, JobStatus.Completed, Other, Invalid },
        { JobStatus.Cancelled, JobStatus.Cancelled, Other, Invalid },
    };

    [Fact]
    public void MatrixCoversEveryPairOfTheSixStatusesForEachOfTheThreeActors()
    {
        var rows = Matrix.Select(row => (row.Data.Item1, row.Data.Item2, row.Data.Item3)).ToList();

        Assert.Equal(108, rows.Count);
        Assert.Equal(108, rows.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void EachCommandFromEachStatusEitherRecordsItsHistoryActionOrReturnsItsErrorCode(
        JobStatus from,
        JobStatus to,
        string actorKind,
        string expected)
    {
        var arrangement = TestData.ArrangeJob(from, _start, answerChecklist: to == JobStatus.Completed);
        var job = arrangement.Job;
        var historyBefore = job.History.Count;
        var actor = actorKind switch
        {
            Admin => arrangement.AdministratorActor,
            Assignee => arrangement.AssigneeActor,
            _ => arrangement.OtherTechnicianActor,
        };

        var error = Apply(arrangement, to, actor);

        if (error is not null)
        {
            Assert.Equal(expected, error.Code);
            Assert.Null(error.Field);
            Assert.Equal(from, job.Status);
            Assert.Equal(historyBefore, job.History.Count);
            return;
        }

        var entry = job.History[^1];
        Assert.Equal(historyBefore + 1, job.History.Count);
        Assert.Equal(expected, entry.Action.ToString());
        Assert.Equal(to, job.Status);
        Assert.Equal(from, entry.FromStatus);
        Assert.Equal(to, entry.ToStatus);
        Assert.Equal(actor.UserId, entry.ActorUserId);
        Assert.Equal(arrangement.Later, entry.OccurredAt);
        Assert.Equal(arrangement.Later, job.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PuttingAJobOnHoldWithoutANoteFailsOnTheNoteField(string? note)
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start);

        var error = arrangement.Job.ChangeStatus(JobStatus.OnHold, note, arrangement.AssigneeActor, arrangement.Later);

        Assert.NotNull(error);
        Assert.Equal(DomainErrors.ValidationFailedCode, error.Code);
        Assert.Equal("note", error.Field);
        Assert.Equal(JobStatus.InProgress, arrangement.Job.Status);
    }

    [Fact]
    public void PuttingAJobOnHoldWithANoteOverFiveHundredCharactersFails()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start);

        var error = arrangement.Job.ChangeStatus(
            JobStatus.OnHold,
            new string('a', 501),
            arrangement.AssigneeActor,
            arrangement.Later);

        Assert.Equal("note", error?.Field);
        Assert.Equal(JobStatus.InProgress, arrangement.Job.Status);
    }

    [Fact]
    public void PuttingAJobOnHoldStoresTheTrimmedNoteOfUpToFiveHundredCharacters()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start);
        var note = new string('a', 500);

        var error = arrangement.Job.ChangeStatus(JobStatus.OnHold, $"  {note} ", arrangement.AssigneeActor, arrangement.Later);

        Assert.Null(error);
        Assert.Equal(note, arrangement.Job.History[^1].Note);
    }

    [Fact]
    public void ANoteSentWithAStartIsDiscarded()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.Assigned, _start);

        var error = arrangement.Job.ChangeStatus(
            JobStatus.InProgress,
            "On my way",
            arrangement.AssigneeActor,
            arrangement.Later);

        Assert.Null(error);
        Assert.Null(arrangement.Job.History[^1].Note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void CancellingWithoutAReasonFailsOnTheReasonField(string? reason)
    {
        var arrangement = TestData.ArrangeJob(JobStatus.Assigned, _start);

        var error = arrangement.Job.Cancel(reason, arrangement.AdministratorActor, arrangement.Later);

        Assert.Equal(DomainErrors.ValidationFailedCode, error?.Code);
        Assert.Equal("reason", error?.Field);
        Assert.Equal(JobStatus.Assigned, arrangement.Job.Status);
        Assert.Null(arrangement.Job.CancelledAt);
    }

    [Fact]
    public void CancellingWithAReasonOverFiveHundredCharactersFails()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.Unassigned, _start);

        var error = arrangement.Job.Cancel(new string('r', 501), arrangement.AdministratorActor, arrangement.Later);

        Assert.Equal("reason", error?.Field);
    }

    [Fact]
    public void CancellingKeepsTheAssigneeAndRecordsTheReason()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.OnHold, _start);

        var error = arrangement.Job.Cancel(" Customer closed the site. ", arrangement.AdministratorActor, arrangement.Later);

        Assert.Null(error);
        Assert.Equal(JobStatus.Cancelled, arrangement.Job.Status);
        Assert.Equal(arrangement.Assignee.UserId, arrangement.Job.AssignedTechnicianId);
        Assert.Equal(arrangement.Later, arrangement.Job.CancelledAt);
        Assert.Equal("Customer closed the site.", arrangement.Job.History[^1].Note);
    }

    [Fact]
    public void StartedAtIsSetOnTheFirstStartOnly()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.Assigned, _start);
        var job = arrangement.Job;
        var actor = arrangement.AssigneeActor;
        var firstStart = _start.AddHours(1);

        TestData.Require(job.ChangeStatus(JobStatus.InProgress, null, actor, firstStart));
        TestData.Require(job.ChangeStatus(JobStatus.OnHold, TestData.HoldNote, actor, firstStart.AddMinutes(30)));
        TestData.Require(job.ChangeStatus(JobStatus.InProgress, null, actor, firstStart.AddHours(1)));

        Assert.Equal(firstStart, job.StartedAt);
        Assert.Equal(firstStart.AddHours(1), job.UpdatedAt);
        Assert.Equal(
            [JobHistoryAction.Created, JobHistoryAction.Assigned, JobHistoryAction.Started, JobHistoryAction.PutOnHold, JobHistoryAction.Resumed],
            job.History.Select(entry => entry.Action));
    }

    [Fact]
    public void CreatingAJobLeavesTheNumberToTheIdentityAndRecordsCreated()
    {
        var administrator = TestData.NewUser(UserRole.Administrator, _start);

        var job = TestData.NewJob(administrator.Id, TestData.HvacTemplateId, _start.AddTicks(12_345));

        Assert.Equal(0, job.Number);
        Assert.Equal(JobStatus.Unassigned, job.Status);
        Assert.Equal(_start.AddTicks(10_000), job.CreatedAt);
        Assert.Equal(job.CreatedAt, job.UpdatedAt);
        var entry = Assert.Single(job.History);
        Assert.Equal(JobHistoryAction.Created, entry.Action);
        Assert.Null(entry.FromStatus);
        Assert.Equal(JobStatus.Unassigned, entry.ToStatus);
        Assert.Equal(administrator.Id, entry.ActorUserId);
        Assert.Equal(job.CreatedAt, entry.OccurredAt);
        Assert.Equal("JOB-1001", JobNumber.Format(JobNumber.First));
    }

    [Fact]
    public void CreatingAJobRefusesAnEndThatIsNotAfterTheStart()
    {
        var details = new JobDetails(
            "Title",
            "Description",
            JobPriority.Normal,
            "Customer",
            "Site",
            null,
            null,
            null,
            null,
            _start,
            _start,
            TestData.HvacTemplateId);

        Assert.Throws<ArgumentException>(() => Job.Create(details, Guid.CreateVersion7(), _start));
    }

    private static DomainError? Apply(TestData.JobArrangement arrangement, JobStatus to, JobActor actor)
    {
        var job = arrangement.Job;
        return to switch
        {
            JobStatus.Assigned => job.AssignTo(arrangement.OtherTechnician, actor, arrangement.Later),
            JobStatus.Completed => job.Submit(arrangement.Inspection, arrangement.Checklist, null, actor, arrangement.Later),
            JobStatus.Cancelled => job.Cancel(TestData.CancellationReason, actor, arrangement.Later),
            JobStatus.OnHold => job.ChangeStatus(to, TestData.HoldNote, actor, arrangement.Later),
            _ => job.ChangeStatus(to, null, actor, arrangement.Later),
        };
    }
}
