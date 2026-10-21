using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using FieldService.Api.Domain;
using Microsoft.AspNetCore.Mvc;

namespace FieldService.Api.Features.Jobs.GetJobs;

public sealed record GetJobsRequest
{
    public const int DefaultPage = 1;

    public const int DefaultPageSize = 25;

    public const int MaxSearchLength = 100;

    [FromQuery(Name = "search")]
    public string? Search { get; init; }

    [FromQuery(Name = "status")]
    public JobStatus? Status { get; init; }

    [FromQuery(Name = "technicianId")]
    public Guid? TechnicianId { get; init; }

    [FromQuery(Name = "priority")]
    public JobPriority? Priority { get; init; }

    [FromQuery(Name = "page")]
    [Range(1, 10000, ErrorMessage = "Enter a page from {1} to {2}.")]
    [DefaultValue(DefaultPage)]
    public int? Page { get; init; }

    [FromQuery(Name = "pageSize")]
    [Range(1, 100, ErrorMessage = "Enter a page size from {1} to {2}.")]
    [DefaultValue(DefaultPageSize)]
    public int? PageSize { get; init; }
}
