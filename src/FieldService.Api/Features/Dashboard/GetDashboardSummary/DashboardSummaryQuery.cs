using System.Data;
using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Dashboard.GetDashboardSummary;

public static class DashboardSummaryQuery
{
    public const int NeedsTechnicianLimit = 10;

    public const int RecentActivityLimit = 6;

    private const string ReadOnlyTransaction = "SET TRANSACTION READ ONLY";

    public static async Task<DashboardSummaryResponse> ReadAsync(
        FieldServiceDbContext db,
        DateTimeOffset generatedAt,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(ReadOnlyTransaction, cancellationToken);

        var open = await OpenJobsAsync(db, generatedAt, from, to, cancellationToken) ?? OpenJobsRow.None;
        var terminal = await TerminalJobsAsync(db, from, to, cancellationToken) ?? TerminalJobsRow.None;
        var created = await db.JobHistory.AsNoTracking().CountAsync(
            entry => entry.Action == JobHistoryAction.Created && entry.OccurredAt >= from && entry.OccurredAt < to,
            cancellationToken);
        var needsTechnician = await NeedsTechnicianAsync(db, cancellationToken);
        var technicians = await TechniciansAsync(db, cancellationToken) ?? new DashboardTechnicianCounts(0, 0);
        var recentActivity = await RecentActivityAsync(db, from, to, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new DashboardSummaryResponse(
            generatedAt,
            new DashboardOpenJobs(
                open.Unassigned + open.Assigned + open.InProgress + open.OnHold,
                open.Unassigned,
                open.Assigned,
                open.InProgress,
                open.OnHold,
                open.Assigned + open.InProgress + open.OnHold,
                open.InProgressTechnicianCount,
                open.NextAssignedStart,
                open.OnHoldSince),
            new DashboardRange(
                from,
                to,
                created,
                terminal.Completed,
                terminal.Cancelled,
                open.UnassignedScheduled,
                terminal.LastCompletedAt),
            technicians,
            needsTechnician,
            recentActivity);
    }

    private static Task<OpenJobsRow?> OpenJobsAsync(
        FieldServiceDbContext db,
        DateTimeOffset generatedAt,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        db.Jobs.AsNoTracking()
            .Where(job => job.Status == JobStatus.Unassigned
                || job.Status == JobStatus.Assigned
                || job.Status == JobStatus.InProgress
                || job.Status == JobStatus.OnHold)
            .GroupBy(job => 1)
            .Select(jobs => new OpenJobsRow(
                jobs.Count(job => job.Status == JobStatus.Unassigned),
                jobs.Count(job => job.Status == JobStatus.Assigned),
                jobs.Count(job => job.Status == JobStatus.InProgress),
                jobs.Count(job => job.Status == JobStatus.OnHold),
                jobs.Where(job => job.Status == JobStatus.InProgress).Select(job => job.AssignedTechnicianId).Distinct().Count(),
                jobs.Where(job => job.Status == JobStatus.Assigned && job.ScheduledStart >= generatedAt)
                    .Min(job => (DateTimeOffset?)job.ScheduledStart),
                jobs.Where(job => job.Status == JobStatus.OnHold).Min(job => (DateTimeOffset?)job.UpdatedAt),
                jobs.Count(job => job.Status == JobStatus.Unassigned && job.ScheduledStart >= from && job.ScheduledStart < to)))
            .SingleOrDefaultAsync(cancellationToken);

    private static Task<TerminalJobsRow?> TerminalJobsAsync(
        FieldServiceDbContext db,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        db.Jobs.AsNoTracking()
            .Where(job => (job.Status == JobStatus.Completed || job.Status == JobStatus.Cancelled)
                && job.UpdatedAt >= from
                && job.UpdatedAt < to)
            .GroupBy(job => 1)
            .Select(jobs => new TerminalJobsRow(
                jobs.Count(job => job.Status == JobStatus.Completed),
                jobs.Count(job => job.Status == JobStatus.Cancelled),
                jobs.Where(job => job.Status == JobStatus.Completed).Max(job => (DateTimeOffset?)job.UpdatedAt)))
            .SingleOrDefaultAsync(cancellationToken);

    private static Task<List<DashboardUnassignedJob>> NeedsTechnicianAsync(
        FieldServiceDbContext db,
        CancellationToken cancellationToken) =>
        db.Jobs.AsNoTracking()
            .Where(job => job.Status == JobStatus.Unassigned)
            .OrderBy(job => job.ScheduledStart)
            .ThenBy(job => job.Number)
            .Take(NeedsTechnicianLimit)
            .Select(job => new DashboardUnassignedJob(
                job.Id,
                job.Number,
                job.Title,
                job.Priority,
                job.CustomerName,
                job.SiteAddress,
                job.ScheduledStart,
                job.ScheduledEnd))
            .ToListAsync(cancellationToken);

    private static Task<DashboardTechnicianCounts?> TechniciansAsync(
        FieldServiceDbContext db,
        CancellationToken cancellationToken) =>
        db.Technicians.AsNoTracking()
            .Where(technician => technician.User.IsActive)
            .GroupBy(technician => 1)
            .Select(technicians => new DashboardTechnicianCounts(
                technicians.Count(),
                technicians.Count(technician => technician.IsOnShift)))
            .SingleOrDefaultAsync(cancellationToken);

    private static Task<List<DashboardActivityEntry>> RecentActivityAsync(
        FieldServiceDbContext db,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var newest = db.JobHistory.AsNoTracking()
            .Where(entry => entry.OccurredAt >= from && entry.OccurredAt < to)
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Action == JobHistoryAction.Created)
            .ThenByDescending(entry => entry.Id)
            .Take(RecentActivityLimit);

        return (
            from entry in newest
            join job in db.Jobs on entry.JobId equals job.Id
            join actor in db.Users on entry.ActorUserId equals actor.Id
            join technician in db.Users on entry.TechnicianId equals (Guid?)technician.Id into technicians
            from technician in technicians.DefaultIfEmpty()
            orderby entry.OccurredAt descending, entry.Action == JobHistoryAction.Created, entry.Id descending
            select new DashboardActivityEntry(
                entry.Id,
                entry.OccurredAt,
                entry.Action,
                entry.JobId,
                job.Number,
                entry.FromStatus,
                entry.ToStatus,
                new UserReference(actor.Id, actor.DisplayName),
                technician == null ? null : new UserReference(technician.Id, technician.DisplayName),
                entry.Note))
            .ToListAsync(cancellationToken);
    }

    private sealed record OpenJobsRow(
        int Unassigned,
        int Assigned,
        int InProgress,
        int OnHold,
        int InProgressTechnicianCount,
        DateTimeOffset? NextAssignedStart,
        DateTimeOffset? OnHoldSince,
        int UnassignedScheduled)
    {
        public static readonly OpenJobsRow None = new(0, 0, 0, 0, 0, null, null, 0);
    }

    private sealed record TerminalJobsRow(int Completed, int Cancelled, DateTimeOffset? LastCompletedAt)
    {
        public static readonly TerminalJobsRow None = new(0, 0, null);
    }
}
