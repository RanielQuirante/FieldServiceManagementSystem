using FieldService.Api.Domain;

namespace FieldService.Api.Features.Jobs.GetJobs;

public sealed record GetJobsResponse(
    IReadOnlyList<JobSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<JobStatusCount> StatusCounts);

public sealed record JobStatusCount(JobStatus Status, int Count);
