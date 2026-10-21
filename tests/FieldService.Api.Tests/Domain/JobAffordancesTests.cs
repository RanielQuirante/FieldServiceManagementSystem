using FieldService.Api.Domain;
using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Domain;

public sealed class JobAffordancesTests
{
    public const string Admin = "Administrator";

    public const string Assignee = "AssignedTechnician";

    public const string Other = "OtherTechnician";

    private const string None = "";

    private const string Forbidden = DomainErrors.ForbiddenCode;

    private const string Invalid = DomainErrors.InvalidTransitionCode;

    private const string Incomplete = DomainErrors.InspectionIncompleteCode;

    private const string Allowed = "allowed";

    private static readonly DateTimeOffset _start = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<JobStatus, string, bool, string, string, bool, bool> Matrix => new()
    {
        { JobStatus.Unassigned, Admin, false, None, $"{Forbidden} 0/10", true, true },
        { JobStatus.Assigned, Admin, false, None, $"{Forbidden} 0/10", true, true },
        { JobStatus.InProgress, Admin, false, None, $"{Forbidden} 0/10", false, true },
        { JobStatus.InProgress, Admin, true, None, $"{Forbidden} 10/10", false, true },
        { JobStatus.OnHold, Admin, false, None, $"{Forbidden} 0/10", false, true },
        { JobStatus.Completed, Admin, true, None, $"{Forbidden} 10/10", false, false },
        { JobStatus.Cancelled, Admin, false, None, $"{Forbidden} 0/10", false, false },
        { JobStatus.Unassigned, Assignee, false, None, $"{Invalid} 0/10", false, false },
        { JobStatus.Assigned, Assignee, false, "InProgress", $"{Invalid} 0/10", false, false },
        { JobStatus.InProgress, Assignee, false, "OnHold+note", $"{Incomplete} 0/10", false, false },
        { JobStatus.InProgress, Assignee, true, "OnHold+note", $"{Allowed} 10/10", false, false },
        { JobStatus.OnHold, Assignee, false, "InProgress", $"{Invalid} 0/10", false, false },
        { JobStatus.OnHold, Assignee, true, "InProgress", $"{Invalid} 10/10", false, false },
        { JobStatus.Completed, Assignee, true, None, $"{Invalid} 10/10", false, false },
        { JobStatus.Cancelled, Assignee, false, None, $"{Invalid} 0/10", false, false },
        { JobStatus.Unassigned, Other, false, None, $"{Invalid} 0/10", false, false },
        { JobStatus.Assigned, Other, false, None, $"{Invalid} 0/10", false, false },
        { JobStatus.InProgress, Other, false, None, $"{Invalid} 0/10", false, false },
        { JobStatus.InProgress, Other, true, None, $"{Invalid} 10/10", false, false },
        { JobStatus.OnHold, Other, false, None, $"{Invalid} 0/10", false, false },
        { JobStatus.Completed, Other, true, None, $"{Invalid} 10/10", false, false },
        { JobStatus.Cancelled, Other, false, None, $"{Invalid} 0/10", false, false },
    };

    public static TheoryData<JobStatus, string> StatusesAndActors
    {
        get
        {
            var data = new TheoryData<JobStatus, string>();
            foreach (var status in Enum.GetValues<JobStatus>())
            {
                data.Add(status, Admin);
                data.Add(status, Assignee);
                data.Add(status, Other);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void CapabilitiesMatchTheLiteralMatrixForEveryStatusAndActor(
        JobStatus status,
        string actorKind,
        bool completeInspection,
        string statusChanges,
        string submit,
        bool canAssign,
        bool canCancel)
    {
        var arrangement = TestData.ArrangeJob(status, _start, completeInspection);
        var completeness = InspectionCompleteness.Of(arrangement.Checklist, arrangement.Inspection);

        var capabilities = JobCapabilities.For(arrangement.Job, ActorOf(arrangement, actorKind), completeness);

        Assert.Equal(statusChanges, Describe(capabilities.AllowedStatusChanges));
        Assert.Equal(submit, Describe(capabilities.CanSubmit));
        Assert.Equal(capabilities.CanSubmit.BlockingCode is null, capabilities.CanSubmit.Allowed);
        Assert.Equal(canAssign, capabilities.CanAssign);
        Assert.Equal(canCancel, capabilities.CanCancel);
    }

    [Fact]
    public void APartlyAnsweredChecklistReportsItsProgressAndBlocksSubmission()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start);
        var inspection = Inspection.Start(arrangement.Job, arrangement.Later);
        TestData.Require(inspection.SaveAnswers(
            arrangement.Job,
            arrangement.Checklist,
            TestData.PassingAnswers(arrangement.Checklist, 6),
            arrangement.AssigneeActor,
            arrangement.Later));

        var capabilities = JobCapabilities.For(
            arrangement.Job,
            arrangement.AssigneeActor,
            InspectionCompleteness.Of(arrangement.Checklist, inspection));

        Assert.Equal(new SubmitCapability(false, Incomplete, 6, 10), capabilities.CanSubmit);
    }

    [Theory]
    [MemberData(nameof(StatusesAndActors))]
    public void EveryOfferedCommandSucceedsAndEveryWithheldCommandFails(JobStatus status, string actorKind)
    {
        var probe = TestData.ArrangeJob(status, _start, answerChecklist: true);
        var capabilities = JobCapabilities.For(
            probe.Job,
            ActorOf(probe, actorKind),
            InspectionCompleteness.Of(probe.Checklist, probe.Inspection));

        foreach (var target in new[] { JobStatus.InProgress, JobStatus.OnHold })
        {
            var arrangement = TestData.ArrangeJob(status, _start, answerChecklist: true);
            var offered = capabilities.AllowedStatusChanges.Any(option => option.Status == target);
            var error = arrangement.Job.ChangeStatus(target, TestData.HoldNote, ActorOf(arrangement, actorKind), arrangement.Later);
            Assert.Equal(offered, error is null);
        }

        var assign = TestData.ArrangeJob(status, _start, answerChecklist: true);
        Assert.Equal(
            capabilities.CanAssign,
            assign.Job.AssignTo(assign.OtherTechnician, ActorOf(assign, actorKind), assign.Later) is null);

        var cancel = TestData.ArrangeJob(status, _start, answerChecklist: true);
        Assert.Equal(
            capabilities.CanCancel,
            cancel.Job.Cancel(TestData.CancellationReason, ActorOf(cancel, actorKind), cancel.Later) is null);

        var submit = TestData.ArrangeJob(status, _start, answerChecklist: true);
        Assert.Equal(
            capabilities.CanSubmit.Allowed,
            submit.Job.Submit(submit.Inspection, submit.Checklist, null, ActorOf(submit, actorKind), submit.Later) is null);
    }

    private static JobActor ActorOf(TestData.JobArrangement arrangement, string actorKind) => actorKind switch
    {
        Admin => arrangement.AdministratorActor,
        Assignee => arrangement.AssigneeActor,
        _ => arrangement.OtherTechnicianActor,
    };

    private static string Describe(IReadOnlyList<StatusChangeOption> options) =>
        string.Join(", ", options.Select(option => option.RequiresNote ? $"{option.Status}+note" : option.Status.ToString()));

    private static string Describe(SubmitCapability submit) =>
        $"{submit.BlockingCode ?? Allowed} {submit.Answered}/{submit.Total}";
}
