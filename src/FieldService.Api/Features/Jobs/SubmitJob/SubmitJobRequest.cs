using System.ComponentModel.DataAnnotations;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Errors;

namespace FieldService.Api.Features.Jobs.SubmitJob;

public sealed record SubmitJobRequest
{
    public const string SiteSafetyMessage = "Confirm that the site is safe.";

    [StringLength(Inspection.MaxTechnicianNotesLength, ErrorMessage = "Enter at most {1} characters.")]
    [MultiLine]
    public string? TechnicianNotes { get; init; }

    [Required(ErrorMessage = SiteSafetyMessage)]
    [AllowedValues(true, ErrorMessage = SiteSafetyMessage)]
    public bool? SiteSafetyConfirmed { get; init; }

    [Required(ErrorMessage = "Send the job version you last read.")]
    public uint? Version { get; init; }
}
