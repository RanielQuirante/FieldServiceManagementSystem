using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Jobs.GetJobs;

public static class GetJobsEndpoint
{
    public const string SearchTooLongMessage = "Enter a search of at most 100 characters.";

    public const string SearchControlCharacterMessage = "Remove line breaks, tabs and other control characters from the search.";

    private const string LikeEscape = "\\";

    public static void Map(RouteGroupBuilder jobs) =>
        jobs.MapGet(string.Empty, HandleAsync)
            .WithName("GetJobs")
            .WithSummary("List jobs")
            .WithDescription("Returns one page of jobs that match every filter, ordered by last update with the newest first, with the total count and the count for each status under every filter except status. The search matches the job number, title, customer and site address.")
            .RequireAuthorization(Policies.Administrator)
            .ValidateQueryParameters();

    public static async Task<Results<Ok<GetJobsResponse>, ValidationProblem>> HandleAsync(
        [AsParameters] GetJobsRequest request,
        HttpContext http,
        FieldServiceDbContext db,
        CancellationToken cancellationToken)
    {
        var search = request.Search?.Trim();
        if (search is { Length: > GetJobsRequest.MaxSearchLength })
        {
            return Problems.FieldError(http, "search", SearchTooLongMessage);
        }

        if (search is not null && search.Any(char.IsControl))
        {
            return Problems.FieldError(http, "search", SearchControlCharacterMessage);
        }

        var page = request.Page ?? GetJobsRequest.DefaultPage;
        var pageSize = request.PageSize ?? GetJobsRequest.DefaultPageSize;
        var matching = Filter(db.Jobs.AsNoTracking(), string.IsNullOrEmpty(search) ? null : search, request);

        var counts = await matching
            .GroupBy(job => job.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Status, entry => entry.Count, cancellationToken);
        var statusCounts = Enum.GetValues<JobStatus>()
            .Select(status => new JobStatusCount(status, counts.GetValueOrDefault(status)))
            .ToList();
        var totalCount = request.Status is { } selected ? counts.GetValueOrDefault(selected) : counts.Values.Sum();

        var offset = (page - 1) * pageSize;
        IReadOnlyList<JobSummaryResponse> items = offset < totalCount
            ? await Page(db, request.Status is { } status ? matching.Where(job => job.Status == status) : matching)
                .Skip(offset)
                .Take(pageSize)
                .ToListAsync(cancellationToken)
            : [];

        return TypedResults.Ok(new GetJobsResponse(items, page, pageSize, totalCount, statusCounts));
    }

    public static string LikePattern(string search) =>
        "%"
        + search.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal)
        + "%";

    private static IQueryable<Job> Filter(IQueryable<Job> jobs, string? search, GetJobsRequest request)
    {
        if (search is not null)
        {
            var pattern = LikePattern(search);
            jobs = jobs.Where(job =>
                EF.Functions.ILike(JobNumber.Prefix + job.Number.ToString(), pattern, LikeEscape)
                || EF.Functions.ILike(job.Title, pattern, LikeEscape)
                || EF.Functions.ILike(job.CustomerName, pattern, LikeEscape)
                || EF.Functions.ILike(job.SiteAddress, pattern, LikeEscape));
        }

        if (request.TechnicianId is { } technicianId)
        {
            jobs = jobs.Where(job => job.AssignedTechnicianId == technicianId);
        }

        if (request.Priority is { } priority)
        {
            jobs = jobs.Where(job => job.Priority == priority);
        }

        return jobs;
    }

    private static IQueryable<JobSummaryResponse> Page(FieldServiceDbContext db, IQueryable<Job> jobs) =>
        from job in jobs
        join assignee in db.Technicians on job.AssignedTechnicianId equals (Guid?)assignee.UserId into assignees
        from assignee in assignees.DefaultIfEmpty()
        orderby job.UpdatedAt descending, job.Id descending
        select new JobSummaryResponse(
            job.Id,
            job.Number,
            job.Title,
            job.Priority,
            job.Status,
            job.CustomerName,
            job.SiteAddress,
            job.ScheduledStart,
            job.ScheduledEnd,
            assignee == null
                ? null
                : new JobTechnicianReference(assignee.UserId, assignee.User.DisplayName, assignee.Trade),
            job.UpdatedAt,
            job.Version);
}
