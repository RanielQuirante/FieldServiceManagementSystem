using System.ComponentModel.DataAnnotations;
using FieldService.Api.Domain;
using Microsoft.AspNetCore.Mvc;

namespace FieldService.Api.Features.Dashboard.GetDashboardSummary;

public sealed record GetDashboardSummaryRequest : IValidatableObject
{
    public const string FromParameter = "from";

    public const string ToParameter = "to";

    public const int MaxWindowDays = 31;

    public const string ToNotAfterFromMessage = "The end of the window must be after its start.";

    public const string WindowTooLongMessage = "The window may span at most 31 days.";

    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(MaxWindowDays);

    [FromQuery(Name = FromParameter)]
    [Required(ErrorMessage = "Send the start of the window.")]
    public DateTimeOffset? From { get; init; }

    [FromQuery(Name = ToParameter)]
    [Required(ErrorMessage = "Send the end of the window.")]
    public DateTimeOffset? To { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is not { } from || To is not { } to)
        {
            yield break;
        }

        var start = UtcInstant.Truncate(from);
        var end = UtcInstant.Truncate(to);
        if (end <= start)
        {
            yield return new ValidationResult(ToNotAfterFromMessage, [ToParameter]);
        }
        else if (end - start > MaxWindow)
        {
            yield return new ValidationResult(WindowTooLongMessage, [ToParameter]);
        }
    }
}
