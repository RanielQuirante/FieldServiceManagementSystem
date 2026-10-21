using System.ComponentModel.DataAnnotations;
using FieldService.Api.Domain;

namespace FieldService.Api.Features.Jobs.CreateJob;

[AttributeUsage(AttributeTargets.Property)]
public sealed class AfterScheduledStartAttribute : ValidationAttribute
{
    public const string Message = "The scheduled end must be after the scheduled start.";

    public AfterScheduledStartAttribute()
        : base(Message)
    {
    }

    public override bool RequiresValidationContext => true;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is DateTimeOffset end
            && validationContext.ObjectInstance is CreateJobRequest { ScheduledStart: { } start }
            && UtcInstant.Truncate(end) <= UtcInstant.Truncate(start))
        {
            return new ValidationResult(
                ErrorMessageString,
                validationContext.MemberName is { } member ? [member] : null);
        }

        return ValidationResult.Success;
    }
}
