using System.ComponentModel.DataAnnotations;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Errors;

namespace FieldService.Api.Features.Jobs.CreateJob;

public sealed record CreateJobRequest
{
    private const string LengthMessage = "Enter at most {1} characters.";

    private const string PhoneMessage =
        "Enter 3 to 31 digits, spaces, parentheses, dots or hyphens, with an optional leading +.";

    [Required(ErrorMessage = "Enter the title.")]
    [StringLength(120, ErrorMessage = LengthMessage)]
    [SingleLine]
    public string Title { get; init; } = string.Empty;

    [Required(ErrorMessage = "Enter the description.")]
    [StringLength(2000, ErrorMessage = LengthMessage)]
    [MultiLine]
    public string Description { get; init; } = string.Empty;

    [Required(ErrorMessage = "Choose the priority.")]
    public JobPriority? Priority { get; init; }

    [Required(ErrorMessage = "Enter the customer name.")]
    [StringLength(120, ErrorMessage = LengthMessage)]
    [SingleLine]
    public string CustomerName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Enter the site address.")]
    [StringLength(300, ErrorMessage = LengthMessage)]
    [SingleLine]
    public string SiteAddress { get; init; } = string.Empty;

    [StringLength(500, ErrorMessage = LengthMessage)]
    [MultiLine]
    public string? SiteAccessNotes { get; init; }

    [StringLength(100, ErrorMessage = LengthMessage)]
    [SingleLine]
    public string? ContactName { get; init; }

    [StringLength(32, ErrorMessage = LengthMessage)]
    [RegularExpression(@"^\+?[0-9 ().-]{3,31}$", ErrorMessage = PhoneMessage)]
    public string? ContactPhone { get; init; }

    [StringLength(200, ErrorMessage = LengthMessage)]
    [SingleLine]
    public string? Equipment { get; init; }

    [Required(ErrorMessage = "Enter the scheduled start.")]
    public DateTimeOffset? ScheduledStart { get; init; }

    [Required(ErrorMessage = "Enter the scheduled end.")]
    [AfterScheduledStart]
    public DateTimeOffset? ScheduledEnd { get; init; }

    [Required(ErrorMessage = "Choose a checklist template.")]
    public Guid? ChecklistTemplateId { get; init; }
}
