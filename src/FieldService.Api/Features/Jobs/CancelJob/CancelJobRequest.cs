using System.ComponentModel.DataAnnotations;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Errors;

namespace FieldService.Api.Features.Jobs.CancelJob;

public sealed record CancelJobRequest
{
    public const string ReasonMessage = "Enter the reason for cancelling the job.";

    [Required(ErrorMessage = ReasonMessage)]
    [StringLength(JobTransitions.MaxNoteLength, ErrorMessage = "Enter at most {1} characters.")]
    [MultiLine]
    public string Reason { get; init; } = string.Empty;

    [Required(ErrorMessage = "Send the job version you last read.")]
    public uint? Version { get; init; }
}
