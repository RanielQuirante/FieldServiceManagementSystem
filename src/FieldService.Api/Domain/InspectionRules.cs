namespace FieldService.Api.Domain;

public static class InspectionRules
{
    public const int MaxAnswers = 100;

    public const int MaxReadingsPerAnswer = 10;

    public const int MaxNoteLength = 1000;

    public const decimal MaxReadingValue = 99999999.99m;

    public const decimal MinReadingValue = -99999999.99m;

    public static bool IsValidReadingValue(decimal value) =>
        value is >= MinReadingValue and <= MaxReadingValue && decimal.Round(value, 2) == value;

    public static IReadOnlyList<DomainError> Validate(InspectionChecklist checklist, IReadOnlyList<ChecklistAnswer> answers)
    {
        var errors = new List<DomainError>();
        if (answers.Count == 0)
        {
            errors.Add(DomainErrors.Field("answers", "Answer at least one checklist item."));
            return errors;
        }

        if (answers.Count > MaxAnswers)
        {
            errors.Add(DomainErrors.Field("answers", $"Send at most {MaxAnswers} answers."));
            return errors;
        }

        var answeredItems = new HashSet<Guid>();
        for (var index = 0; index < answers.Count; index++)
        {
            var answer = answers[index];
            var path = $"answers[{index}]";
            var item = checklist.Find(answer.ChecklistItemId);
            if (item is null)
            {
                errors.Add(DomainErrors.Field($"{path}.checklistItemId", "Choose an item of this job's checklist."));
                continue;
            }

            if (!answeredItems.Add(item.Id))
            {
                errors.Add(DomainErrors.Field($"{path}.checklistItemId", "Answer each checklist item only once."));
                continue;
            }

            ValidateNote(answer, path, errors);
            ValidateReadings(answer, item, path, errors);
        }

        return errors;
    }

    private static void ValidateNote(ChecklistAnswer answer, string path, List<DomainError> errors)
    {
        var note = DomainText.Optional(answer.Note);
        if (answer.Result == InspectionResult.Fail && note is null)
        {
            errors.Add(DomainErrors.Field($"{path}.note", "Describe the problem for a failed item."));
        }
        else if (note is { Length: > MaxNoteLength })
        {
            errors.Add(DomainErrors.Field($"{path}.note", $"Enter a note of at most {MaxNoteLength} characters."));
        }
    }

    private static void ValidateReadings(
        ChecklistAnswer answer,
        InspectionChecklistItem item,
        string path,
        List<DomainError> errors)
    {
        if (answer.Readings.Count > MaxReadingsPerAnswer)
        {
            errors.Add(DomainErrors.Field($"{path}.readings", $"Send at most {MaxReadingsPerAnswer} readings."));
            return;
        }

        if (answer.Result == InspectionResult.NotApplicable && answer.Readings.Count > 0)
        {
            errors.Add(DomainErrors.Field($"{path}.readings", "A not applicable item takes no readings."));
            return;
        }

        var supplied = new HashSet<Guid>();
        var readingsValid = true;
        for (var index = 0; index < answer.Readings.Count; index++)
        {
            var reading = answer.Readings[index];
            var readingPath = $"{path}.readings[{index}]";
            if (!item.ReadingFieldIds.Contains(reading.ReadingFieldId))
            {
                errors.Add(DomainErrors.Field($"{readingPath}.readingFieldId", "Choose a reading field of this checklist item."));
                readingsValid = false;
                continue;
            }

            if (!supplied.Add(reading.ReadingFieldId))
            {
                errors.Add(DomainErrors.Field($"{readingPath}.readingFieldId", "Enter each reading only once."));
                readingsValid = false;
                continue;
            }

            if (!IsValidReadingValue(reading.Value))
            {
                errors.Add(DomainErrors.Field(
                    $"{readingPath}.value",
                    "Enter a value from -99999999.99 to 99999999.99 with at most two decimal places."));
                readingsValid = false;
            }
        }

        if (readingsValid
            && answer.Result == InspectionResult.Pass
            && item.ReadingFieldIds.Any(fieldId => !supplied.Contains(fieldId)))
        {
            errors.Add(DomainErrors.Field($"{path}.readings", "Enter every reading of this checklist item."));
        }
    }
}
