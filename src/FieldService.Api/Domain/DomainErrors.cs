using System.Globalization;

namespace FieldService.Api.Domain;

public static class DomainErrors
{
    public const string InvalidTransitionCode = "job.invalid-transition";

    public const string NotAssignableCode = "job.not-assignable";

    public const string TechnicianUnavailableCode = "technician.unavailable";

    public const string InspectionIncompleteCode = "inspection.incomplete";

    public const string InspectionLockedCode = "inspection.locked";

    public const string ValidationFailedCode = "request.validation-failed";

    public const string ForbiddenCode = "auth.forbidden";

    public static DomainError InvalidTransition(Job job, JobStatus target) =>
        new(InvalidTransitionCode, $"{Describe(job)} cannot move from {job.Status} to {target}.");

    public static DomainError NotAssignable(Job job) =>
        new(NotAssignableCode, $"{Describe(job)} is {job.Status} and can no longer be assigned.");

    public static DomainError TechnicianUnavailable() =>
        new(TechnicianUnavailableCode, "The technician is inactive or off shift.");

    public static DomainError InspectionIncomplete(InspectionCompleteness completeness) =>
        new(
            InspectionIncompleteCode,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{completeness.Unanswered} of {completeness.Total} checklist items are unanswered."));

    public static DomainError InspectionSubmitted() =>
        new(InspectionLockedCode, "The inspection was submitted and can no longer be changed.");

    public static DomainError InspectionNotEditable(Job job) =>
        new(InspectionLockedCode, $"{Describe(job)} is {job.Status}, so its inspection cannot be changed.");

    public static DomainError InspectionNotOwned() =>
        new(InspectionLockedCode, "Only the assigned technician can change the inspection.");

    public static DomainError Field(string field, string message) => new(ValidationFailedCode, message, field);

    private static string Describe(Job job) => job.Number > 0 ? JobNumber.Format(job.Number) : "The job";
}
