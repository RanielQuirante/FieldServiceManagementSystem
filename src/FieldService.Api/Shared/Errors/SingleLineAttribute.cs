using System.ComponentModel.DataAnnotations;

namespace FieldService.Api.Shared.Errors;

[AttributeUsage(AttributeTargets.Property)]
public sealed class SingleLineAttribute : ValidationAttribute
{
    public const string Message = "Remove line breaks, tabs and other control characters.";

    public SingleLineAttribute()
        : base(Message)
    {
    }

    public override bool IsValid(object? value) => ControlCharacters.AreAllowedIn(value, allowLineBreaks: false);
}
