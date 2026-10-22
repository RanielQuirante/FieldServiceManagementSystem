using System.ComponentModel.DataAnnotations;

namespace FieldService.Api.Features.Jobs.AssignJob;

public sealed record AssignJobRequest
{
    [Required(ErrorMessage = "Choose the technician.")]
    public Guid? TechnicianId { get; init; }

    [Required(ErrorMessage = "Send the job version you last read.")]
    public uint? Version { get; init; }
}
