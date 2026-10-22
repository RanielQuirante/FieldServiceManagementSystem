using System.ComponentModel.DataAnnotations;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Errors;

namespace FieldService.Api.Features.Jobs.UpdateJobStatus;

public sealed record UpdateJobStatusRequest
{
    public const string StatusMessage = "Choose InProgress or OnHold.";

    [Required(ErrorMessage = "Choose the status.")]
    [AllowedValues(JobStatus.InProgress, JobStatus.OnHold, ErrorMessage = StatusMessage)]
    public JobStatus? Status { get; init; }

    [StringLength(JobTransitions.MaxNoteLength, ErrorMessage = "Enter at most {1} characters.")]
    [MultiLine]
    public string? Note { get; init; }

    [Required(ErrorMessage = "Send the job version you last read.")]
    public uint? Version { get; init; }
}
