namespace FieldService.Api.Domain;

public sealed record InspectionCompleteness(
    int Answered,
    int Total,
    int PassCount,
    int FailCount,
    int NotApplicableCount)
{
    public int Unanswered => Total - Answered;

    public bool IsComplete => Total > 0 && Answered == Total;

    public static InspectionCompleteness Of(InspectionChecklist checklist, Inspection? inspection)
    {
        var results = inspection?.Answers
            .Where(answer => checklist.Find(answer.ChecklistItemId) is not null)
            .Select(answer => answer.Result) ?? [];
        return Of(checklist.Total, results);
    }

    public static InspectionCompleteness Of(int total, IEnumerable<InspectionResult> results)
    {
        int pass = 0, fail = 0, notApplicable = 0;
        foreach (var result in results)
        {
            switch (result)
            {
                case InspectionResult.Pass:
                    pass++;
                    break;
                case InspectionResult.Fail:
                    fail++;
                    break;
                case InspectionResult.NotApplicable:
                    notApplicable++;
                    break;
            }
        }

        return new InspectionCompleteness(pass + fail + notApplicable, total, pass, fail, notApplicable);
    }
}
