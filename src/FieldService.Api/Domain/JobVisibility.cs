namespace FieldService.Api.Domain;

public static class JobVisibility
{
    public static IQueryable<Job> VisibleTo(this IQueryable<Job> jobs, JobActor actor)
    {
        if (actor.Role == UserRole.Administrator)
        {
            return jobs;
        }

        var technicianId = actor.UserId;
        return jobs.Where(job => job.AssignedTechnicianId == technicianId);
    }
}
