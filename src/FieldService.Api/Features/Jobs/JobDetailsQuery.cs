using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Jobs;

public static class JobDetailsQuery
{
    public static async Task<JobDetailsResponse?> FindAsync(
        FieldServiceDbContext db,
        Guid jobId,
        JobActor actor,
        CancellationToken cancellationToken)
    {
        var row = await (
            from job in db.Jobs.AsNoTracking().VisibleTo(actor)
            where job.Id == jobId
            join template in db.ChecklistTemplates on job.ChecklistTemplateId equals template.Id
            join creator in db.Users on job.CreatedByUserId equals creator.Id
            join assignee in db.Technicians on job.AssignedTechnicianId equals (Guid?)assignee.UserId into assignees
            from assignee in assignees.DefaultIfEmpty()
            select new
            {
                Job = job,
                ChecklistTemplate = new ChecklistTemplateReference(template.Id, template.Name, template.Trade),
                ChecklistItemCount = template.Items.Count,
                AssignedTechnician = assignee == null
                    ? null
                    : new JobTechnicianReference(assignee.UserId, assignee.User.DisplayName, assignee.Trade),
                CreatedBy = new UserReference(creator.Id, creator.DisplayName),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var history = await (
            from entry in db.JobHistory.AsNoTracking()
            where entry.JobId == jobId
            join actorUser in db.Users on entry.ActorUserId equals actorUser.Id
            join technicianUser in db.Users on entry.TechnicianId equals (Guid?)technicianUser.Id into technicianUsers
            from technicianUser in technicianUsers.DefaultIfEmpty()
            orderby entry.OccurredAt, entry.Action == JobHistoryAction.Created ? 0 : 1, entry.Id
            select new JobHistoryEntryResponse(
                entry.Id,
                entry.Action,
                entry.FromStatus,
                entry.ToStatus,
                new UserReference(actorUser.Id, actorUser.DisplayName),
                technicianUser == null ? null : new UserReference(technicianUser.Id, technicianUser.DisplayName),
                entry.Note,
                entry.OccurredAt))
            .ToListAsync(cancellationToken);

        var details = row.Job;
        var templateId = details.ChecklistTemplateId;
        var answeredResults = await (
            from inspection in db.Inspections.AsNoTracking()
            where inspection.JobId == jobId
            from answer in inspection.Answers
            join item in db.Set<ChecklistItem>() on answer.ChecklistItemId equals item.Id
            where item.TemplateId == templateId
            select answer.Result)
            .ToListAsync(cancellationToken);

        var completeness = InspectionCompleteness.Of(row.ChecklistItemCount, answeredResults);
        var capabilities = JobCapabilities.For(details, actor, completeness);
        var submit = capabilities.CanSubmit;
        var affordances = new JobAffordances(
            [
                .. capabilities.AllowedStatusChanges
                    .Select(option => new AllowedStatusChange(option.Status, option.RequiresNote)),
            ],
            new SubmitAffordance(submit.Allowed, submit.BlockingCode, submit.Answered, submit.Total),
            capabilities.CanAssign,
            capabilities.CanCancel);
        return new JobDetailsResponse(
            details.Id,
            details.Number,
            details.Title,
            details.Description,
            details.Priority,
            details.Status,
            details.CustomerName,
            details.SiteAddress,
            details.SiteAccessNotes,
            details.ContactName,
            details.ContactPhone,
            details.Equipment,
            details.ScheduledStart,
            details.ScheduledEnd,
            row.ChecklistTemplate,
            row.AssignedTechnician,
            row.CreatedBy,
            details.CreatedAt,
            details.UpdatedAt,
            details.AssignedAt,
            details.StartedAt,
            details.CompletedAt,
            details.CancelledAt,
            history,
            details.Version,
            affordances);
    }
}
