using System.ComponentModel.DataAnnotations;
using FieldService.Api.Shared.Errors;

namespace FieldService.Api.Features.Diagnostics.ReportClientError;

public sealed record ReportClientErrorRequest
{
    public const int MaxMessageLength = 500;

    public const int MaxSourceLength = 200;

    public const int MaxRouteLength = 200;

    public const int MaxStackLength = 2000;

    public const string RoutePattern = "^/[A-Za-z0-9/_.~%-]*$";

    public const string CorrelationIdPattern = "^[A-Za-z0-9._-]{1,64}$";

    public const string AppVersionPattern = "^[0-9A-Za-z.+-]{1,32}$";

    public const string RouteMessage = "Send the route path only, starting with / and without a query string or fragment.";

    public const string CorrelationIdMessage = "Send a correlation id of 1 to 64 letters, digits, dots, underscores or hyphens.";

    public const string AppVersionMessage = "Send an app version of 1 to 32 letters, digits, dots, plus signs or hyphens.";

    private const string LengthMessage = "Enter at most {1} characters.";

    [Required(ErrorMessage = "Enter the error message.")]
    [StringLength(MaxMessageLength, ErrorMessage = LengthMessage)]
    [MultiLine]
    public string Message { get; init; } = string.Empty;

    [Required(ErrorMessage = "Enter the error source.")]
    [StringLength(MaxSourceLength, ErrorMessage = LengthMessage)]
    [SingleLine]
    public string Source { get; init; } = string.Empty;

    [StringLength(MaxRouteLength, ErrorMessage = LengthMessage)]
    [RegularExpression(RoutePattern, ErrorMessage = RouteMessage)]
    public string? Route { get; init; }

    [RegularExpression(CorrelationIdPattern, ErrorMessage = CorrelationIdMessage)]
    public string? CorrelationId { get; init; }

    [StringLength(MaxStackLength, ErrorMessage = LengthMessage)]
    [MultiLine]
    public string? Stack { get; init; }

    [Required(ErrorMessage = "Send the time the error occurred.")]
    public DateTimeOffset? OccurredAt { get; init; }

    [RegularExpression(AppVersionPattern, ErrorMessage = AppVersionMessage)]
    public string? AppVersion { get; init; }
}
