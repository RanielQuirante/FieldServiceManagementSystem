using System.ComponentModel.DataAnnotations;

namespace FieldService.Api.Shared.Errors;

[AttributeUsage(AttributeTargets.Property)]
public sealed class MultiLineAttribute : ValidationAttribute
{
    public const string Message = "Remove control characters other than line breaks and tabs.";

    public MultiLineAttribute()
        : base(Message)
    {
    }

    public override bool IsValid(object? value) => ControlCharacters.AreAllowedIn(value, allowLineBreaks: true);
}
