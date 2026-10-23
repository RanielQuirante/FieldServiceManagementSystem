using FieldService.Api.Domain;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Domain;

public sealed class InspectionRulesTests
{
    private static readonly DateTimeOffset _start = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly InspectionChecklist _hvac = TestData.ChecklistOf(TestData.HvacTemplateId);

    private static readonly Guid _filtersItem = ChecklistTemplateSeed.ItemId(2, 2);

    private static readonly Guid _pressuresItem = ChecklistTemplateSeed.ItemId(2, 3);

    private static readonly Guid _suction = ChecklistTemplateSeed.ReadingFieldId(2, 3, 1);

    private static readonly Guid _discharge = ChecklistTemplateSeed.ReadingFieldId(2, 3, 2);

    private static readonly Guid _supplyAir = ChecklistTemplateSeed.ReadingFieldId(2, 8, 1);

    public static TheoryData<decimal> OutOfRangeValues => [100000000m, -100000000m, 99999999.991m, 1.234m, 0.005m];

    public static TheoryData<decimal> BoundaryValues => [99999999.99m, -99999999.99m, 0.40m, 0m, 18m];

    [Fact]
    public void AnItemOfAnotherTemplateIsAFieldErrorOnItsChecklistItemId()
    {
        var errors = InspectionRules.Validate(_hvac, [Pass(ChecklistTemplateSeed.ItemId(1, 1))]);

        AssertSingleField(errors, "answers[0].checklistItemId");
    }

    [Fact]
    public void AnUnknownItemIsAFieldErrorOnItsChecklistItemId()
    {
        var errors = InspectionRules.Validate(_hvac, [Pass(_filtersItem), Pass(Guid.CreateVersion7())]);

        AssertSingleField(errors, "answers[1].checklistItemId");
    }

    [Fact]
    public void AnItemAnsweredTwiceIsAFieldErrorOnTheSecondAnswer()
    {
        var errors = InspectionRules.Validate(_hvac, [Pass(_filtersItem), Pass(_filtersItem)]);

        AssertSingleField(errors, "answers[1].checklistItemId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AFailWithoutANoteIsAFieldErrorOnTheNote(string? note)
    {
        var errors = InspectionRules.Validate(_hvac, [Answer(_filtersItem, InspectionResult.Fail, note)]);

        AssertSingleField(errors, "answers[0].note");
    }

    [Fact]
    public void ANoteOverOneThousandCharactersIsAFieldErrorOnTheNote()
    {
        var errors = InspectionRules.Validate(_hvac, [Answer(_filtersItem, InspectionResult.Pass, new string('n', 1001))]);

        AssertSingleField(errors, "answers[0].note");
    }

    [Fact]
    public void APassWithAMissingReadingIsAFieldErrorOnTheReadings()
    {
        var errors = InspectionRules.Validate(
            _hvac,
            [Answer(_pressuresItem, InspectionResult.Pass, null, new ChecklistReading(_suction, 118m))]);

        AssertSingleField(errors, "answers[0].readings");
    }

    [Fact]
    public void APassWithoutAnyReadingOfAnItemThatHasReadingFieldsIsAFieldError()
    {
        var errors = InspectionRules.Validate(_hvac, [Pass(_pressuresItem)]);

        AssertSingleField(errors, "answers[0].readings");
    }

    [Fact]
    public void ANotApplicableAnswerWithReadingsIsAFieldErrorOnTheReadings()
    {
        var errors = InspectionRules.Validate(
            _hvac,
            [Answer(_pressuresItem, InspectionResult.NotApplicable, "Not fitted", new ChecklistReading(_suction, 118m))]);

        AssertSingleField(errors, "answers[0].readings");
    }

    [Fact]
    public void AFailMaySupplyAnySubsetOfItsReadings()
    {
        var errors = InspectionRules.Validate(
            _hvac,
            [Answer(_pressuresItem, InspectionResult.Fail, "Low charge", new ChecklistReading(_discharge, 200m))]);

        Assert.Empty(errors);
    }

    [Fact]
    public void AReadingFieldOfAnotherItemIsAFieldErrorOnItsReadingFieldId()
    {
        var errors = InspectionRules.Validate(
            _hvac,
            [
                Answer(
                    _pressuresItem,
                    InspectionResult.Pass,
                    null,
                    new ChecklistReading(_suction, 118m),
                    new ChecklistReading(_supplyAir, 54m)),
            ]);

        AssertSingleField(errors, "answers[0].readings[1].readingFieldId");
    }

    [Fact]
    public void AReadingFieldSuppliedTwiceIsAFieldErrorOnTheSecondReading()
    {
        var errors = InspectionRules.Validate(
            _hvac,
            [
                Answer(
                    _pressuresItem,
                    InspectionResult.Pass,
                    null,
                    new ChecklistReading(_suction, 118m),
                    new ChecklistReading(_suction, 119m)),
            ]);

        AssertSingleField(errors, "answers[0].readings[1].readingFieldId");
    }

    [Theory]
    [MemberData(nameof(OutOfRangeValues))]
    public void AReadingValueOutsideNumericTenTwoIsAFieldErrorOnItsValue(decimal value)
    {
        var errors = InspectionRules.Validate(
            _hvac,
            [
                Answer(
                    _pressuresItem,
                    InspectionResult.Pass,
                    null,
                    new ChecklistReading(_suction, value),
                    new ChecklistReading(_discharge, 285m)),
            ]);

        AssertSingleField(errors, "answers[0].readings[0].value");
    }

    [Theory]
    [MemberData(nameof(BoundaryValues))]
    public void AReadingValueInsideNumericTenTwoIsAccepted(decimal value)
    {
        var errors = InspectionRules.Validate(
            _hvac,
            [
                Answer(
                    _pressuresItem,
                    InspectionResult.Pass,
                    null,
                    new ChecklistReading(_suction, value),
                    new ChecklistReading(_discharge, value)),
            ]);

        Assert.Empty(errors);
    }

    [Fact]
    public void MoreThanTenReadingsOnOneAnswerIsAFieldErrorOnTheReadings()
    {
        var readings = Enumerable.Range(0, 11).Select(index => new ChecklistReading(_suction, index)).ToArray();

        var errors = InspectionRules.Validate(_hvac, [Answer(_pressuresItem, InspectionResult.Fail, "Too many", readings)]);

        AssertSingleField(errors, "answers[0].readings");
    }

    [Fact]
    public void NoAnswersOrMoreThanOneHundredAnswersIsAFieldErrorOnTheAnswers()
    {
        var empty = InspectionRules.Validate(_hvac, []);
        var tooMany = InspectionRules.Validate(_hvac, [.. Enumerable.Repeat(Pass(_filtersItem), 101)]);

        AssertSingleField(empty, "answers");
        AssertSingleField(tooMany, "answers");
    }

    [Fact]
    public void CompletenessCountsAnswersPerResultAgainstTheTemplate()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start);
        var inspection = Inspection.Start(arrangement.Job, arrangement.Later);
        TestData.Require(inspection.SaveAnswers(
            arrangement.Job,
            _hvac,
            [
                Pass(ChecklistTemplateSeed.ItemId(2, 1)),
                Pass(_filtersItem),
                Answer(_pressuresItem, InspectionResult.Pass, null, new ChecklistReading(_suction, 118m), new ChecklistReading(_discharge, 285m)),
                Pass(ChecklistTemplateSeed.ItemId(2, 4)),
                Answer(ChecklistTemplateSeed.ItemId(2, 5), InspectionResult.Fail, "Bearing noise at start-up."),
                Answer(ChecklistTemplateSeed.ItemId(2, 6), InspectionResult.NotApplicable, "Direct-drive unit"),
            ],
            arrangement.AssigneeActor,
            arrangement.Later));

        var partial = InspectionCompleteness.Of(_hvac, inspection);
        var none = InspectionCompleteness.Of(_hvac, null);

        Assert.Equal(new InspectionCompleteness(6, 10, 4, 1, 1), partial);
        Assert.Equal(4, partial.Unanswered);
        Assert.False(partial.IsComplete);
        Assert.Equal(new InspectionCompleteness(0, 10, 0, 0, 0), none);
        Assert.False(none.IsComplete);
    }

    [Fact]
    public void TheFirstSaveStampsTheInspectionTheAnswersAndTheJobWithTheSaveTime()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start);
        var job = arrangement.Job;

        var inspection = Inspection.Start(job, arrangement.Later);
        var error = inspection.SaveAnswers(job, _hvac, TestData.PassingAnswers(_hvac, 3), arrangement.AssigneeActor, arrangement.Later);

        Assert.Null(error);
        Assert.Equal(job.Id, inspection.JobId);
        Assert.Equal(arrangement.Assignee.UserId, inspection.TechnicianId);
        Assert.Equal(arrangement.Later, inspection.CreatedAt);
        Assert.Equal(arrangement.Later, inspection.UpdatedAt);
        Assert.Equal(arrangement.Later, job.UpdatedAt);
        Assert.Equal(3, inspection.Answers.Count);
        Assert.All(inspection.Answers, answer => Assert.Equal(arrangement.Later, answer.AnsweredAt));
        Assert.Equal(
            [(_suction, 12.5m), (_discharge, 12.5m)],
            AnswerTo(inspection, _pressuresItem).Readings.Select(reading => (reading.ReadingFieldId, reading.Value)));
    }

    [Fact]
    public void ALaterSaveReplacesOnlyTheChangedAnswersAndTouchesTheJob()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start);
        var job = arrangement.Job;
        var first = arrangement.Later;
        var second = first.AddMinutes(19);
        var inspection = Inspection.Start(job, first);
        TestData.Require(inspection.SaveAnswers(job, _hvac, TestData.PassingAnswers(_hvac, 3), arrangement.AssigneeActor, first));

        var error = inspection.SaveAnswers(
            job,
            _hvac,
            [
                Pass(_filtersItem),
                Answer(_pressuresItem, InspectionResult.Pass, null, new ChecklistReading(_suction, 120m), new ChecklistReading(_discharge, 285m)),
                Pass(ChecklistTemplateSeed.ItemId(2, 4)),
            ],
            arrangement.AssigneeActor,
            second);

        Assert.Null(error);
        Assert.Equal(first, inspection.CreatedAt);
        Assert.Equal(second, inspection.UpdatedAt);
        Assert.Equal(second, job.UpdatedAt);
        Assert.Equal(4, inspection.Answers.Count);
        Assert.Equal(first, AnswerTo(inspection, _filtersItem).AnsweredAt);
        var pressures = AnswerTo(inspection, _pressuresItem);
        Assert.Equal(second, pressures.AnsweredAt);
        Assert.Equal(120m, pressures.Readings.Single(reading => reading.ReadingFieldId == _suction).Value);
        Assert.Equal(285m, pressures.Readings.Single(reading => reading.ReadingFieldId == _discharge).Value);
        Assert.Equal(second, AnswerTo(inspection, ChecklistTemplateSeed.ItemId(2, 4)).AnsweredAt);
    }

    [Fact]
    public void SavingTheSameAnswersAgainChangesNothing()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start);
        var job = arrangement.Job;
        var answers = TestData.PassingAnswers(_hvac, 3);
        var inspection = Inspection.Start(job, arrangement.Later);
        TestData.Require(inspection.SaveAnswers(job, _hvac, answers, arrangement.AssigneeActor, arrangement.Later));

        var error = inspection.SaveAnswers(job, _hvac, answers, arrangement.AssigneeActor, arrangement.Later.AddHours(1));

        Assert.Null(error);
        Assert.True(inspection.HasSameAnswers(answers));
        Assert.Equal(arrangement.Later, inspection.UpdatedAt);
        Assert.Equal(arrangement.Later, job.UpdatedAt);
    }

    [Fact]
    public void AnInvalidSaveReturnsTheFirstFieldErrorAndChangesNothing()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start);
        var inspection = Inspection.Start(arrangement.Job, arrangement.Later);

        var error = inspection.SaveAnswers(
            arrangement.Job,
            _hvac,
            [Pass(_filtersItem), Answer(_filtersItem, InspectionResult.Fail, null)],
            arrangement.AssigneeActor,
            arrangement.Later);

        Assert.Equal(DomainErrors.ValidationFailedCode, error?.Code);
        Assert.Equal("answers[1].checklistItemId", error?.Field);
        Assert.Empty(inspection.Answers);
    }

    [Fact]
    public void ASubmittedInspectionIsLocked()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.Completed, _start);
        var inspection = arrangement.Inspection!;
        var updatedAt = inspection.UpdatedAt;

        var error = inspection.SaveAnswers(
            arrangement.Job,
            _hvac,
            [Answer(_filtersItem, InspectionResult.Fail, "Filters missing")],
            arrangement.AssigneeActor,
            arrangement.Later);

        Assert.Equal(DomainErrors.InspectionLockedCode, error?.Code);
        Assert.Equal(InspectionResult.Pass, AnswerTo(inspection, _filtersItem).Result);
        Assert.Equal(updatedAt, inspection.UpdatedAt);
        Assert.False(Inspection.CanEdit(arrangement.Job, inspection, arrangement.AssigneeActor));
    }

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.OnHold)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Assigned)]
    public void AFieldErrorIsReportedBeforeTheLock(JobStatus status)
    {
        var arrangement = TestData.ArrangeJob(status, _start);
        var inspection = arrangement.Inspection ?? Inspection.Start(arrangement.Job, arrangement.Later);

        var failWithoutNote = inspection.SaveAnswers(
            arrangement.Job,
            _hvac,
            [Answer(_filtersItem, InspectionResult.Fail, " ")],
            arrangement.AssigneeActor,
            arrangement.Later);
        var foreignItem = inspection.SaveAnswers(
            arrangement.Job,
            _hvac,
            [Pass(ChecklistTemplateSeed.ItemId(1, 1))],
            arrangement.AdministratorActor,
            arrangement.Later);
        var locked = inspection.SaveAnswers(arrangement.Job, _hvac, [Pass(_filtersItem)], arrangement.AssigneeActor, arrangement.Later);

        Assert.Equal("answers[0].note", failWithoutNote?.Field);
        Assert.Equal("answers[0].checklistItemId", foreignItem?.Field);
        Assert.Equal(DomainErrors.InspectionLockedCode, locked?.Code);
    }

    [Fact]
    public void AnswersAreAppliedInChecklistPositionOrderWhateverTheOrderTheyArrive()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start);
        var inspection = Inspection.Start(arrangement.Job, arrangement.Later);

        var error = inspection.SaveAnswers(
            arrangement.Job,
            _hvac,
            [
                Answer(ChecklistTemplateSeed.ItemId(2, 8), InspectionResult.Pass, null, new ChecklistReading(_supplyAir, 54m)),
                Pass(_filtersItem),
                Answer(ChecklistTemplateSeed.ItemId(2, 5), InspectionResult.Fail, "Bearing noise"),
            ],
            arrangement.AssigneeActor,
            arrangement.Later);

        Assert.Null(error);
        Assert.Equal(
            [_filtersItem, ChecklistTemplateSeed.ItemId(2, 5), ChecklistTemplateSeed.ItemId(2, 8)],
            inspection.Answers.Select(answer => answer.ChecklistItemId));
    }

    [Theory]
    [InlineData(JobStatus.OnHold)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Assigned)]
    public void AnInspectionIsLockedWhileTheJobIsNotInProgress(JobStatus status)
    {
        var arrangement = TestData.ArrangeJob(status, _start);
        var inspection = Inspection.Start(arrangement.Job, arrangement.Later);

        var error = inspection.SaveAnswers(arrangement.Job, _hvac, [Pass(_filtersItem)], arrangement.AssigneeActor, arrangement.Later);

        Assert.Equal(DomainErrors.InspectionLockedCode, error?.Code);
        Assert.Empty(inspection.Answers);
    }

    [Fact]
    public void OnlyTheAssignedTechnicianCanSaveTheInspection()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start);
        var inspection = Inspection.Start(arrangement.Job, arrangement.Later);

        var byOther = inspection.SaveAnswers(arrangement.Job, _hvac, [Pass(_filtersItem)], arrangement.OtherTechnicianActor, arrangement.Later);
        var byAdministrator = inspection.SaveAnswers(arrangement.Job, _hvac, [Pass(_filtersItem)], arrangement.AdministratorActor, arrangement.Later);

        Assert.Equal(DomainErrors.InspectionLockedCode, byOther?.Code);
        Assert.Equal(DomainErrors.InspectionLockedCode, byAdministrator?.Code);
        Assert.True(Inspection.CanEdit(arrangement.Job, inspection, arrangement.AssigneeActor));
        Assert.False(Inspection.CanEdit(arrangement.Job, inspection, arrangement.OtherTechnicianActor));
        Assert.False(Inspection.CanEdit(arrangement.Job, inspection, arrangement.AdministratorActor));
    }

    [Fact]
    public void SubmittingAnIncompleteInspectionReturnsInspectionIncompleteWithTheUnansweredCount()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start);
        var job = arrangement.Job;
        var inspection = Inspection.Start(job, arrangement.Later);
        TestData.Require(inspection.SaveAnswers(job, _hvac, TestData.PassingAnswers(_hvac, 6), arrangement.AssigneeActor, arrangement.Later));

        var error = job.Submit(inspection, _hvac, "Done", arrangement.AssigneeActor, arrangement.Later.AddMinutes(5));

        Assert.Equal(DomainErrors.InspectionIncompleteCode, error?.Code);
        Assert.Equal("4 of 10 checklist items are unanswered.", error?.Message);
        Assert.Equal(JobStatus.InProgress, job.Status);
        Assert.Null(job.CompletedAt);
        Assert.False(inspection.IsSubmitted);
    }

    [Fact]
    public void SubmittingWithoutAnInspectionReturnsInspectionIncomplete()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start);

        var error = arrangement.Job.Submit(null, _hvac, null, arrangement.AssigneeActor, arrangement.Later);

        Assert.Equal(DomainErrors.InspectionIncompleteCode, error?.Code);
        Assert.Equal("10 of 10 checklist items are unanswered.", error?.Message);
    }

    [Fact]
    public void SubmittingACompleteInspectionCompletesTheJobAndLocksTheInspectionAtTheSameInstant()
    {
        var arrangement = TestData.ArrangeJob(JobStatus.InProgress, _start, answerChecklist: true);
        var job = arrangement.Job;
        var inspection = arrangement.Inspection!;

        var error = job.Submit(inspection, _hvac, "  Recharged and cleaned.  ", arrangement.AssigneeActor, arrangement.Later);

        Assert.Null(error);
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Equal(arrangement.Later, job.CompletedAt);
        Assert.Equal(arrangement.Later, job.UpdatedAt);
        Assert.Equal(arrangement.Later, inspection.SubmittedAt);
        Assert.Equal(arrangement.Later, inspection.UpdatedAt);
        Assert.Equal("Recharged and cleaned.", inspection.TechnicianNotes);
        var entry = job.History[^1];
        Assert.Equal(JobHistoryAction.Completed, entry.Action);
        Assert.Null(entry.Note);
    }

    [Fact]
    public void BlankCompletionNotesAreStoredAsNullAndLongNotesAreAFieldError()
    {
        var blank = TestData.ArrangeJob(JobStatus.InProgress, _start, answerChecklist: true);
        var tooLong = TestData.ArrangeJob(JobStatus.InProgress, _start, answerChecklist: true);

        var blankError = blank.Job.Submit(blank.Inspection, _hvac, "   ", blank.AssigneeActor, blank.Later);
        var longError = tooLong.Job.Submit(tooLong.Inspection, _hvac, new string('n', 2001), tooLong.AssigneeActor, tooLong.Later);

        Assert.Null(blankError);
        Assert.Null(blank.Inspection!.TechnicianNotes);
        Assert.Equal("technicianNotes", longError?.Field);
        Assert.Equal(JobStatus.InProgress, tooLong.Job.Status);
        Assert.False(tooLong.Inspection!.IsSubmitted);
    }

    private static ChecklistAnswer Pass(Guid itemId) => Answer(itemId, InspectionResult.Pass, null);

    private static ChecklistAnswer Answer(Guid itemId, InspectionResult result, string? note, params ChecklistReading[] readings) =>
        new(itemId, result, note, readings);

    private static InspectionAnswer AnswerTo(Inspection inspection, Guid itemId) =>
        inspection.Answers.Single(answer => answer.ChecklistItemId == itemId);

    private static void AssertSingleField(IReadOnlyList<DomainError> errors, string field)
    {
        var error = Assert.Single(errors);
        Assert.Equal(DomainErrors.ValidationFailedCode, error.Code);
        Assert.Equal(field, error.Field);
    }
}
