using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;

namespace FieldService.Api.Features.Inspections.SaveInspection;

public sealed record SaveInspectionRequest : IValidatableObject
{
    public const string DuplicateItemMessage = "Answer each checklist item only once.";

    public const string EmptyAnswerMessage = "Remove the empty answer.";

    public const string EmptyReadingMessage = "Remove the empty reading.";

    [Required(ErrorMessage = "Answer at least one checklist item.")]
    [MinLength(1, ErrorMessage = "Answer at least one checklist item.")]
    [MaxLength(InspectionRules.MaxAnswers, ErrorMessage = AnswersLimit.MessageFormat)]
    public LimitedList<InspectionAnswerInput, AnswersLimit> Answers { get; init; } = [];

    [Required(ErrorMessage = "Send the job version you last read.")]
    public uint? Version { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var answeredItems = new HashSet<Guid>();
        for (var index = 0; index < Answers.Count; index++)
        {
            var path = $"answers[{index}]";
            if (Answers[index] is not { } answer)
            {
                yield return new ValidationResult(EmptyAnswerMessage, [path]);
                continue;
            }

            var readings = answer.Readings ?? [];
            for (var position = 0; position < readings.Count; position++)
            {
                if (readings[position] is null)
                {
                    yield return new ValidationResult(EmptyReadingMessage, [$"{path}.readings[{position}]"]);
                }
            }

            if (answer.ChecklistItemId is { } itemId && !answeredItems.Add(itemId))
            {
                yield return new ValidationResult(DuplicateItemMessage, [$"{path}.checklistItemId"]);
            }
        }
    }
}

public sealed record InspectionAnswerInput
{
    [Required(ErrorMessage = "Choose the checklist item.")]
    public Guid? ChecklistItemId { get; init; }

    [Required(ErrorMessage = "Choose Pass, Fail or NotApplicable.")]
    public InspectionResult? Result { get; init; }

    [StringLength(InspectionRules.MaxNoteLength, ErrorMessage = "Enter at most {1} characters.")]
    [MultiLine]
    public string? Note { get; init; }

    [MaxLength(InspectionRules.MaxReadingsPerAnswer, ErrorMessage = ReadingsLimit.MessageFormat)]
    public LimitedList<InspectionReadingInput, ReadingsLimit>? Readings { get; init; }
}

public sealed class AnswersLimit : ICollectionLimit
{
    public const string MessageFormat = "Send at most {1} answers.";

    public static int Max => InspectionRules.MaxAnswers;

    public static string Message { get; } =
        string.Format(CultureInfo.InvariantCulture, MessageFormat, nameof(SaveInspectionRequest.Answers), Max);
}

public sealed class ReadingsLimit : ICollectionLimit
{
    public const string MessageFormat = "Send at most {1} readings.";

    public static int Max => InspectionRules.MaxReadingsPerAnswer;

    public static string Message { get; } =
        string.Format(CultureInfo.InvariantCulture, MessageFormat, nameof(InspectionAnswerInput.Readings), Max);
}

public sealed record InspectionReadingInput
{
    public const string ValueMessage =
        "Enter a value from -99999999.99 to 99999999.99 with at most two decimal places.";

    [Required(ErrorMessage = "Choose the reading field.")]
    public Guid? ReadingFieldId { get; init; }

    [Required(ErrorMessage = "Enter the reading value.")]
    [Range(
        typeof(decimal),
        "-99999999.99",
        "99999999.99",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true,
        ErrorMessage = ValueMessage)]
    public decimal? Value { get; init; }
}
