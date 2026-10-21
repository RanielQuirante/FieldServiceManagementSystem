using FieldService.Api.Domain;
using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Domain;

public sealed class JobAssignmentRulesTests
{
    private static readonly DateTimeOffset _start = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AssigningAnUnassignedJobSetsTheAssigneeAndRecordsAssigned()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.Unassigned, _start);
        var job = arrangement.Job;

        var error = job.AssignTo(arrangement.Assignee, arrangement.AdministratorActor, arrangement.Later);

        Assert.Null(error);
        Assert.Equal(JobStatus.Assigned, job.Status);
        Assert.Equal(arrangement.Assignee.UserId, job.AssignedTechnicianId);
        Assert.Equal(arrangement.Later, job.AssignedAt);
        Assert.Equal(arrangement.Later, job.UpdatedAt);
        Assert.True(job.IsAssignedTo(arrangement.Assignee.UserId));
        var entry = job.History[^1];
        Assert.Equal(JobHistoryAction.Assigned, entry.Action);
        Assert.Equal(JobStatus.Unassigned, entry.FromStatus);
        Assert.Equal(JobStatus.Assigned, entry.ToStatus);
        Assert.Equal(arrangement.Administrator.Id, entry.ActorUserId);
        Assert.Equal(arrangement.Assignee.UserId, entry.TechnicianId);
        Assert.Null(entry.Note);
    }

    [Fact]
    public void ReassigningAnAssignedJobMovesItToTheNewTechnician()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.Assigned, _start);
        var job = arrangement.Job;

        var error = job.AssignTo(arrangement.OtherTechnician, arrangement.AdministratorActor, arrangement.Later);

        Assert.Null(error);
        Assert.Equal(JobStatus.Assigned, job.Status);
        Assert.Equal(arrangement.OtherTechnician.UserId, job.AssignedTechnicianId);
        Assert.Equal(arrangement.Later, job.AssignedAt);
        var entry = job.History[^1];
        Assert.Equal(JobStatus.Assigned, entry.FromStatus);
        Assert.Equal(arrangement.OtherTechnician.UserId, entry.TechnicianId);
        Assert.Equal(3, job.History.Count);
    }

    [Theory]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.OnHold)]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Cancelled)]
    public void AssigningAJobThatIsNeitherUnassignedNorAssignedReturnsNotAssignable(JobStatus status)
    {
        var arrangement = TestData.ArrangeJob(status, _start);
        var job = arrangement.Job;
        var assignedAt = job.AssignedAt;
        var historyCount = job.History.Count;

        var error = job.AssignTo(arrangement.OtherTechnician, arrangement.AdministratorActor, arrangement.Later);

        Assert.Equal(DomainErrors.NotAssignableCode, error?.Code);
        Assert.Equal(status, job.Status);
        Assert.Equal(arrangement.Assignee.UserId, job.AssignedTechnicianId);
        Assert.Equal(assignedAt, job.AssignedAt);
        Assert.Equal(historyCount, job.History.Count);
    }

    [Fact]
    public void AssigningTheSameTechnicianAgainToAnInProgressJobIsStillNotAssignable()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start);

        var error = arrangement.Job.AssignTo(arrangement.Assignee, arrangement.AdministratorActor, arrangement.Later);

        Assert.Equal(DomainErrors.NotAssignableCode, error?.Code);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void AssigningAnInactiveOrOffShiftTechnicianReturnsTechnicianUnavailable(bool isActive, bool isOnShift)
    {
        var arrangement = TestData.ArrangeJob(JobStatus.Unassigned, _start);
        var technician = TestData.NewTechnician(Trade.Hvac, _start, isOnShift, isActive);

        var error = arrangement.Job.AssignTo(technician, arrangement.AdministratorActor, arrangement.Later);

        Assert.Equal(DomainErrors.TechnicianUnavailableCode, error?.Code);
        Assert.False(technician.IsAvailableForAssignment);
        Assert.Equal(JobStatus.Unassigned, arrangement.Job.Status);
        Assert.Null(arrangement.Job.AssignedTechnicianId);
        Assert.Single(arrangement.Job.History);
    }

    [Fact]
    public void ReassigningToAnUnavailableTechnicianKeepsTheCurrentAssignee()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.Assigned, _start);
        var offShift = TestData.NewTechnician(Trade.Hvac, _start, isOnShift: false);

        var error = arrangement.Job.AssignTo(offShift, arrangement.AdministratorActor, arrangement.Later);

        Assert.Equal(DomainErrors.TechnicianUnavailableCode, error?.Code);
        Assert.Equal(arrangement.Assignee.UserId, arrangement.Job.AssignedTechnicianId);
    }

    [Fact]
    public void AssigningTheSameTechnicianToAnAssignedJobChangesNothing()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.Assigned, _start);
        var job = arrangement.Job;
        var assignedAt = job.AssignedAt;
        var updatedAt = job.UpdatedAt;
        var historyCount = job.History.Count;

        var error = job.AssignTo(arrangement.Assignee, arrangement.AdministratorActor, arrangement.Later);

        Assert.Null(error);
        Assert.Equal(JobStatus.Assigned, job.Status);
        Assert.Equal(assignedAt, job.AssignedAt);
        Assert.Equal(updatedAt, job.UpdatedAt);
        Assert.Equal(historyCount, job.History.Count);
    }

    [Fact]
    public void AssigningTheSameTechnicianIsANoOpEvenAfterTheTechnicianWasDeactivated()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.Assigned, _start);
        arrangement.Assignee.User.Deactivate(arrangement.Later);

        var error = arrangement.Job.AssignTo(arrangement.Assignee, arrangement.AdministratorActor, arrangement.Later);

        Assert.Null(error);
        Assert.Equal(2, arrangement.Job.History.Count);
    }

    [Theory]
    [InlineData(JobStatus.Unassigned)]
    [InlineData(JobStatus.Assigned)]
    public void ATechnicianCannotAssignAJob(JobStatus status)
    {
        var arrangement = TestData.ArrangeJob(status, _start);

        var error = arrangement.Job.AssignTo(arrangement.OtherTechnician, arrangement.AssigneeActor, arrangement.Later);

        Assert.Equal(DomainErrors.InvalidTransitionCode, error?.Code);
        Assert.Equal(status, arrangement.Job.Status);
    }

    [Fact]
    public void OnlyAUserWithTheTechnicianRoleHasATechnicianProfile()
    {
        var administrator = TestData.NewUser(UserRole.Administrator, _start);

        Assert.Throws<ArgumentException>(() => Technician.Create(administrator, Trade.Hvac, "(555) 010-0000", isOnShift: true));
    }
}
