using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Features;
using FieldService.Api.Features.Inspections;
using FieldService.Api.Features.Inspections.SaveInspection;
using FieldService.Api.Persistence;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FieldService.Api.Tests.Features.Inspections;

[Collection(DatabaseCollection.Name)]
public sealed class SaveInspectionTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string Incomplete = "inspection.incomplete";

    private const string Locked = "inspection.locked";

    private const string FailNoteMessage = "Describe the problem for a failed item.";

    private static readonly Guid _templateId = ChecklistTemplateSeed.TemplateId(2);

    private static readonly Guid _suction = ChecklistTemplateSeed.ReadingFieldId(2, 3, 1);

    private static readonly Guid _discharge = ChecklistTemplateSeed.ReadingFieldId(2, 3, 2);

    private static readonly Guid _supplyAir = ChecklistTemplateSeed.ReadingFieldId(2, 8, 1);

    private static readonly Guid _unknownItem = ChecklistTemplateSeed.ItemId(2, 11);

    private DateTimeOffset Now => UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());

    private DateTimeOffset Earlier => Now.AddMinutes(-30);

    [Fact]
    public async Task TheFirstSaveCreatesTheInspectionAndReturnsItWithTheJobsNewVersion()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        var historyCount = await HistoryCountAsync(job.Id);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(
            PathOf(job.Id),
            Body(
                job.Version,
                Answer(8, "Pass", null, Reading(_supplyAir, 54m)),
                Answer(5, "Fail", "  Bearing noise at start-up.  "),
                Answer(6, "NotApplicable", "Direct-drive unit", [])));

        var inspection = await ReadAsync(response);
        Assert.NotEqual(job.Version, inspection.Version);
        Assert.Equal(JobStatus.InProgress, inspection.JobStatus);
        Assert.Equal(Now, inspection.UpdatedAt);
        Assert.Null(inspection.SubmittedAt);
        Assert.Equal([5, 6, 8], inspection.Items.Where(item => item.Answer is not null).Select(item => item.Position));
        var supplyAir = inspection.Items[7].Answer!;
        Assert.Equal(InspectionResult.Pass, supplyAir.Result);
        Assert.Null(supplyAir.Note);
        Assert.Equal([new InspectionReadingResponse(_supplyAir, 54m)], supplyAir.Readings);
        Assert.Equal(Now, supplyAir.AnsweredAt);
        Assert.Equal("Bearing noise at start-up.", inspection.Items[4].Answer!.Note);
        Assert.Equal(InspectionResult.NotApplicable, inspection.Items[5].Answer!.Result);
        Assert.Empty(inspection.Items[5].Answer!.Readings);
        Assert.Equal(new InspectionProgress(3, 10, 1, 1, 1), inspection.Progress);
        Assert.Equal(new InspectionAffordances(true, new SubmitAffordance(false, Incomplete, 3, 10)), inspection.Affordances);

        await using var db = Fixture.CreateAppDbContext();
        var storedJob = await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == job.Id, CancellationToken);
        Assert.Equal(inspection.Version, storedJob.Version);
        Assert.Equal(Now, storedJob.UpdatedAt);
        Assert.Equal(JobStatus.InProgress, storedJob.Status);
        var stored = await db.Inspections.AsNoTracking().WithAnswersAndReadings().SingleAsync(entry => entry.JobId == job.Id, CancellationToken);
        Assert.Equal(technician.UserId, stored.TechnicianId);
        Assert.Equal(Now, stored.CreatedAt);
        Assert.Equal(Now, stored.UpdatedAt);
        Assert.Null(stored.SubmittedAt);
        Assert.Null(stored.TechnicianNotes);
        Assert.Equal(3, stored.Answers.Count);
        var storedReading = Assert.Single(stored.Answers.Single(answer => answer.ChecklistItemId == Item(8)).Readings);
        Assert.Equal(54.00m, storedReading.Value);
        Assert.Equal("54.00", storedReading.Value.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(historyCount, await HistoryCountAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task ALaterSaveReplacesOnlyTheListedAnswersKeepsTheOthersAndDropsReadingsTheNewAnswerLacks()
    {
        await using var factory = Fixture.CreateFactory();
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress, UtcInstant.Truncate(factory.Clock.GetUtcNow()).AddMinutes(-30));
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, technician.User);
        var firstSave = UtcInstant.Truncate(factory.Clock.GetUtcNow());

        using var first = await client.PutJsonAsync(
            PathOf(job.Id),
            Body(
                job.Version,
                Answer(3, "Pass", null, Reading(_suction, 118m), Reading(_discharge, 285m)),
                Answer(4, "Pass"),
                Answer(8, "Pass", null, Reading(_supplyAir, 54m))));
        var saved = await ReadAsync(first);
        factory.Clock.Advance(TimeSpan.FromMinutes(10));
        var secondSave = UtcInstant.Truncate(factory.Clock.GetUtcNow());
        client.AccessToken = TestTokens.For(factory, technician.User);
        using var second = await client.PutJsonAsync(
            PathOf(job.Id),
            Body(saved.Version, Answer(3, "NotApplicable", "Gauges unavailable"), Answer(4, "Pass"), Answer(7, "Pass")));

        var inspection = await ReadAsync(second);
        Assert.NotEqual(saved.Version, inspection.Version);
        Assert.Equal(secondSave, inspection.UpdatedAt);
        Assert.Equal([3, 4, 7, 8], inspection.Items.Where(item => item.Answer is not null).Select(item => item.Position));
        var pressures = inspection.Items[2].Answer!;
        Assert.Equal(InspectionResult.NotApplicable, pressures.Result);
        Assert.Equal("Gauges unavailable", pressures.Note);
        Assert.Empty(pressures.Readings);
        Assert.Equal(secondSave, pressures.AnsweredAt);
        Assert.Equal(firstSave, inspection.Items[3].Answer!.AnsweredAt);
        Assert.Equal(secondSave, inspection.Items[6].Answer!.AnsweredAt);
        Assert.Equal(firstSave, inspection.Items[7].Answer!.AnsweredAt);
        Assert.Equal([new InspectionReadingResponse(_supplyAir, 54m)], inspection.Items[7].Answer!.Readings);
        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.Inspections.AsNoTracking().WithAnswersAndReadings().SingleAsync(entry => entry.JobId == job.Id, CancellationToken);
        Assert.Equal(firstSave, stored.CreatedAt);
        Assert.Equal(secondSave, stored.UpdatedAt);
        Assert.Equal(4, stored.Answers.Count);
        Assert.Equal([_supplyAir], stored.Answers.SelectMany(answer => answer.Readings).Select(reading => reading.ReadingFieldId));
        factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task ResendingTheSavedAnswersIsTheSameStateAndReturns200WithoutAWriteWhateverTheVersion()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);
        using var first = await client.PutJsonAsync(
            PathOf(job.Id),
            Body(job.Version, Answer(5, "Fail", "Bearing noise at start-up."), Answer(8, "Pass", null, Reading(_supplyAir, 54m))));
        var saved = await ReadAsync(first);
        var state = await StateOfAsync(job.Id);

        using var reordered = await client.PutJsonAsync(
            PathOf(job.Id),
            Body(job.Version, Answer(8, "Pass", null, Reading(_supplyAir, 54.00m)), Answer(5, "Fail", " Bearing noise at start-up.\n")));
        using var subset = await client.PutJsonAsync(PathOf(job.Id), Body(saved.Version + 7, Answer(5, "Fail", "Bearing noise at start-up.")));

        foreach (var response in new[] { reordered, subset })
        {
            var inspection = await ReadAsync(response);
            Assert.Equal(saved.Version, inspection.Version);
            Assert.Equal(saved.UpdatedAt, inspection.UpdatedAt);
        }

        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task EverySaveThatWritesMovesTheVersionEvenAtTheSameInstantSoTheEarlierVersionThenConflicts()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);

        using var first = await client.PutJsonAsync(PathOf(job.Id), Body(job.Version, Answer(1, "Pass")));
        var afterFirst = await ReadAsync(first);
        using var second = await client.PutJsonAsync(PathOf(job.Id), Body(afterFirst.Version, Answer(2, "Pass")));
        var afterSecond = await ReadAsync(second);
        var state = await StateOfAsync(job.Id);
        using var stale = await client.PutJsonAsync(PathOf(job.Id), Body(afterFirst.Version, Answer(4, "Pass")));

        Assert.Equal(Now, afterFirst.UpdatedAt);
        Assert.Equal(Now, afterSecond.UpdatedAt);
        Assert.NotEqual(job.Version, afterFirst.Version);
        Assert.NotEqual(afterFirst.Version, afterSecond.Version);
        await ApiClient.AssertProblemAsync(stale, HttpStatusCode.Conflict, "concurrency.conflict");
        Assert.Equal(state, await StateOfAsync(job.Id));
        Assert.Equal(afterSecond.Version, (await LoadJobAsync(job.Id)).Version);
    }

    [Fact]
    public async Task AStaleVersionReturns409ConcurrencyConflictAndWritesNothing()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        await DataAt(Earlier.AddMinutes(10)).SaveInspectionAsync(job, [Saved(1, InspectionResult.Pass)], CancellationToken);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(job.Version, Answer(2, "Pass")));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrency.conflict");
        Assert.Equal("The job was changed by someone else.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Theory]
    [InlineData("""{"answers":[{"checklistItemId":"@foreign","result":"Pass"}],"version":@v}""", "answers[0].checklistItemId")]
    [InlineData("""{"answers":[{"checklistItemId":"@unknown","result":"Pass"}],"version":@v}""", "answers[0].checklistItemId")]
    [InlineData("""{"answers":[{"checklistItemId":"@i01","result":"Pass"},{"checklistItemId":"@i02","result":"Pass"},{"checklistItemId":"@i01","result":"Fail","note":"Loose"}],"version":@v}""", "answers[2].checklistItemId")]
    [InlineData("""{"answers":[{"checklistItemId":"@i05","result":"Fail"}],"version":@v}""", "answers[0].note")]
    [InlineData("""{"answers":[{"checklistItemId":"@i05","result":"Fail","note":null}],"version":@v}""", "answers[0].note")]
    [InlineData("""{"answers":[{"checklistItemId":"@i05","result":"Fail","note":""}],"version":@v}""", "answers[0].note")]
    [InlineData("""{"answers":[{"checklistItemId":"@i05","result":"Fail","note":"   "}],"version":@v}""", "answers[0].note")]
    [InlineData("""{"answers":[{"checklistItemId":"@i05","result":"Fail","note":"\t\n\r"}],"version":@v}""", "answers[0].note")]
    [InlineData("""{"answers":[{"checklistItemId":"@i05","result":"Fail","note":"  "}],"version":@v}""", "answers[0].note")]
    [InlineData("""{"answers":[{"checklistItemId":"@i05","result":"Fail","note":"a\u0000b"}],"version":@v}""", "answers[0].note")]
    [InlineData("""{"answers":[{"checklistItemId":"@i05","result":"Fail","note":42}],"version":@v}""", "answers[0].note")]
    [InlineData("""{"answers":[{"checklistItemId":"@i03","result":"Pass","readings":[{"readingFieldId":"@suction","value":118}]}],"version":@v}""", "answers[0].readings")]
    [InlineData("""{"answers":[{"checklistItemId":"@i08","result":"Pass"}],"version":@v}""", "answers[0].readings")]
    [InlineData("""{"answers":[{"checklistItemId":"@i08","result":"Pass","readings":[]}],"version":@v}""", "answers[0].readings")]
    [InlineData("""{"answers":[{"checklistItemId":"@i08","result":"NotApplicable","readings":[{"readingFieldId":"@supply","value":54}]}],"version":@v}""", "answers[0].readings")]
    [InlineData("""{"answers":[{"checklistItemId":"@i03","result":"Fail","note":"Low","readings":[{"readingFieldId":"@supply","value":54}]}],"version":@v}""", "answers[0].readings[0].readingFieldId")]
    [InlineData("""{"answers":[{"checklistItemId":"@i03","result":"Pass","readings":[{"readingFieldId":"@suction","value":118},{"readingFieldId":"@suction","value":120}]}],"version":@v}""", "answers[0].readings[1].readingFieldId")]
    [InlineData("""{"answers":[{"checklistItemId":"@i08","result":"Pass","readings":[{"value":54}]}],"version":@v}""", "answers[0].readings[0].readingFieldId")]
    [InlineData("""{"answers":[{"checklistItemId":"@i08","result":"Pass","readings":[{"readingFieldId":"@supply","value":54.125}]}],"version":@v}""", "answers[0].readings[0].value")]
    [InlineData("""{"answers":[{"checklistItemId":"@i08","result":"Pass","readings":[{"readingFieldId":"@supply","value":0.005}]}],"version":@v}""", "answers[0].readings[0].value")]
    [InlineData("""{"answers":[{"checklistItemId":"@i08","result":"Pass","readings":[{"readingFieldId":"@supply","value":99999999.991}]}],"version":@v}""", "answers[0].readings[0].value")]
    [InlineData("""{"answers":[{"checklistItemId":"@i08","result":"Pass","readings":[{"readingFieldId":"@supply","value":100000000}]}],"version":@v}""", "answers[0].readings[0].value")]
    [InlineData("""{"answers":[{"checklistItemId":"@i08","result":"Pass","readings":[{"readingFieldId":"@supply","value":-100000000}]}],"version":@v}""", "answers[0].readings[0].value")]
    [InlineData("""{"answers":[{"checklistItemId":"@i08","result":"Pass","readings":[{"readingFieldId":"@supply","value":"54"}]}],"version":@v}""", "answers[0].readings[0].value")]
    [InlineData("""{"answers":[{"checklistItemId":"@i08","result":"Pass","readings":[{"readingFieldId":"@supply"}]}],"version":@v}""", "answers[0].readings[0].value")]
    [InlineData("""{"answers":[{"checklistItemId":"@i08","result":"Pass","readings":[{"readingFieldId":"@supply","value":null}]}],"version":@v}""", "answers[0].readings[0].value")]
    [InlineData("""{"answers":[{"checklistItemId":"@i08","result":"Pass","readings":[{"readingFieldId":"@supply","value":1e29}]}],"version":@v}""", "answers[0].readings[0].value")]
    [InlineData("""{"answers":[{"checklistItemId":"@i01","result":"Pass"},null],"version":@v}""", "answers[1]")]
    [InlineData("""{"answers":[{"checklistItemId":"@i08","result":"Pass","readings":[null]}],"version":@v}""", "answers[0].readings[0]")]
    [InlineData("""{"answers":[{"checklistItemId":"@i01","result":"Maybe"}],"version":@v}""", "answers[0].result")]
    [InlineData("""{"answers":[{"checklistItemId":"@i01","result":0}],"version":@v}""", "answers[0].result")]
    [InlineData("""{"answers":[{"checklistItemId":"@i01","result":null}],"version":@v}""", "answers[0].result")]
    [InlineData("""{"answers":[{"checklistItemId":"@i01"}],"version":@v}""", "answers[0].result")]
    [InlineData("""{"answers":[{"result":"Pass"}],"version":@v}""", "answers[0].checklistItemId")]
    [InlineData("""{"answers":[{"checklistItemId":"JOB-1042-01","result":"Pass"}],"version":@v}""", "answers[0].checklistItemId")]
    [InlineData("""{"answers":[],"version":@v}""", "answers")]
    [InlineData("""{"version":@v}""", "answers")]
    [InlineData("""{"answers":null,"version":@v}""", "answers")]
    [InlineData("""{"answers":{},"version":@v}""", "answers")]
    [InlineData("""{"answers":[{"checklistItemId":"@i01","result":"Pass"}]}""", "version")]
    [InlineData("""{"answers":[{"checklistItemId":"@i01","result":"Pass"}],"version":"1"}""", "version")]
    [InlineData("""{"answers":[{"checklistItemId":"@i01","result":"Pass"}],"version":-1}""", "version")]
    [InlineData("""{"answers":[{"checklistItemId":"@i05","result":"Fail"},{"checklistItemId":"@i01","result":"Pass"},{"checklistItemId":"@i08","result":"Pass"}],"version":@v}""", "answers[0].note,answers[2].readings")]
    [InlineData("""[]""", "body")]
    [InlineData("""not json""", "body")]
    public async Task ARuleBreakOrAMalformedMemberReturns400KeyedByItsPathAndWritesNothing(string template, string keys)
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(technician.User);

        using var response = await client.PutAsync(PathOf(job.Id), Json(Expand(template, job)));

        await ApiClient.AssertFieldErrorsAsync(response, keys.Split(','));
        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Theory]
    [InlineData("answers", "answers")]
    [InlineData("readings", "answers[0].readings")]
    [InlineData("note", "answers[0].note")]
    public async Task TooManyAnswersTooManyReadingsAndALongNoteReturn400(string limit, string key)
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(technician.User);
        object body = limit switch
        {
            "answers" => new { answers = Enumerable.Range(0, 101).Select(_ => new { checklistItemId = Guid.CreateVersion7(), result = "Pass" }).ToArray(), version = job.Version },
            "readings" => Body(job.Version, Answer(3, "Fail", "Erratic", [.. Enumerable.Range(0, 11).Select(index => Reading(_suction, index))])),
            _ => Body(job.Version, Answer(5, "Fail", new string('n', 1001))),
        };

        using var response = await client.PutJsonAsync(PathOf(job.Id), body);

        await ApiClient.AssertFieldErrorsAsync(response, key);
        Assert.Equal(state, await StateOfAsync(job.Id));
    }

    [Theory]
    [InlineData("answers", 101, "answers")]
    [InlineData("answers", 5000, "answers")]
    [InlineData("readings", 11, "answers[0].readings")]
    [InlineData("readings", 5000, "answers[0].readings")]
    [InlineData("readings of the fourth answer", 5000, "answers[3].readings")]
    public async Task AnOversizedListOfEmptyEntriesIsOneFieldErrorInASmallResponse(string list, int count, string key)
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(technician.User);
        var empties = string.Join(',', Enumerable.Repeat("{}", count));
        var body = list switch
        {
            "answers" => Expand($$"""{"answers":[{{empties}}],"version":@v}""", job),
            "readings" => Expand($$"""{"answers":[{"checklistItemId":"@i03","result":"Pass","readings":[{{empties}}]}],"version":@v}""", job),
            _ => Expand(
                $$"""{"answers":[{},{"checklistItemId":"@i01"},{"result":"Fail"},{"checklistItemId":"@i03","result":"Pass","readings":[{{empties}}]}],"version":@v}""",
                job),
        };

        using var response = await client.PutAsync(PathOf(job.Id), Json(body));

        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(text.Length < 1024, $"The 400 holds {text.Length} characters for a {body.Length}-character body.");
        await ApiClient.AssertFieldErrorsAsync(response, key);
        Assert.Equal([list == "answers" ? AnswersLimit.Message : ReadingsLimit.Message], await MessagesAsync(response, key));
        Assert.Equal(state, await StateOfAsync(job.Id));
    }

    [Fact]
    public async Task ANumberWithMoreSignificantDigitsThanADecimalHoldsIsRoundedByTheJsonParserBeforeTheCheck()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);

        using var response = await client.PutAsync(
            PathOf(job.Id),
            Json(Expand(
                """{"answers":[{"checklistItemId":"@i08","result":"Pass","readings":[{"readingFieldId":"@supply","value":54.0000000000000000000000000001}]}],"version":@v}""",
                job)));

        var inspection = await ReadAsync(response);
        Assert.Equal([new InspectionReadingResponse(_supplyAir, 54m)], inspection.Items[7].Answer!.Readings);
    }

    [Fact]
    public void EachListLimitAgreesWithTheMaxLengthItsPropertyDocuments()
    {
        var answers = typeof(SaveInspectionRequest).GetProperty(nameof(SaveInspectionRequest.Answers))!
            .GetCustomAttributes(typeof(MaxLengthAttribute), inherit: false).Cast<MaxLengthAttribute>().Single();
        var readings = typeof(InspectionAnswerInput).GetProperty(nameof(InspectionAnswerInput.Readings))!
            .GetCustomAttributes(typeof(MaxLengthAttribute), inherit: false).Cast<MaxLengthAttribute>().Single();

        Assert.Equal((InspectionRules.MaxAnswers, InspectionRules.MaxAnswers), (answers.Length, AnswersLimit.Max));
        Assert.Equal((InspectionRules.MaxReadingsPerAnswer, InspectionRules.MaxReadingsPerAnswer), (readings.Length, ReadingsLimit.Max));
        Assert.Equal("Send at most 100 answers.", AnswersLimit.Message);
        Assert.Equal("Send at most 10 readings.", ReadingsLimit.Message);
        Assert.Equal(AnswersLimit.Message, answers.FormatErrorMessage(nameof(SaveInspectionRequest.Answers)));
        Assert.Equal(ReadingsLimit.Message, readings.FormatErrorMessage(nameof(InspectionAnswerInput.Readings)));
    }

    [Fact]
    public async Task TheMostEmptyAnswersTheLimitAllowsGiveTwoBoundedErrorsEach()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);
        var empties = string.Join(',', Enumerable.Repeat("{}", 100));

        using var response = await client.PutAsync(PathOf(job.Id), Json(Expand($$"""{"answers":[{{empties}}],"version":@v}""", job)));

        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        await ApiClient.AssertFieldErrorsAsync(
            response,
            [.. Enumerable.Range(0, 100).SelectMany(index => new[] { $"answers[{index}].checklistItemId", $"answers[{index}].result" })]);
        Assert.True(text.Length < 16 * 1024, $"The 400 holds {text.Length} characters.");
    }

    [Fact]
    public async Task TheFieldErrorMessagesNameTheRuleAndNeverRepeatTheValue()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);

        using var response = await client.PutAsync(
            PathOf(job.Id),
            Json(Expand(
                """{"answers":[{"checklistItemId":"@i05","result":"Fail","note":" "},{"checklistItemId":"@i08","result":"Pass","readings":[{"readingFieldId":"@supply","value":54.125}]},{"checklistItemId":"@i05","result":"Pass"}],"version":@v}""",
                job)));
        using var duplicate = await client.PutAsync(
            PathOf(job.Id),
            Json(Expand("""{"answers":[{"checklistItemId":"@i01","result":"Pass"},{"checklistItemId":"@i01","result":"Pass"},null],"version":@v}""", job)));
        using var foreign = await client.PutAsync(
            PathOf(job.Id),
            Json(Expand("""{"answers":[{"checklistItemId":"@foreign","result":"Pass"},{"checklistItemId":"@i03","result":"Pass","readings":[{"readingFieldId":"@suction","value":1}]}],"version":@v}""", job)));

        Assert.Equal([SaveInspectionRequest.DuplicateItemMessage], await MessagesAsync(response, "answers[2].checklistItemId"));
        Assert.Equal([SaveInspectionRequest.DuplicateItemMessage], await MessagesAsync(duplicate, "answers[1].checklistItemId"));
        Assert.Equal([SaveInspectionRequest.EmptyAnswerMessage], await MessagesAsync(duplicate, "answers[2]"));
        Assert.Equal(["Choose an item of this job's checklist."], await MessagesAsync(foreign, "answers[0].checklistItemId"));
        Assert.Equal(["Enter every reading of this checklist item."], await MessagesAsync(foreign, "answers[1].readings"));
        using var ruleBreaks = await client.PutAsync(
            PathOf(job.Id),
            Json(Expand(
                """{"answers":[{"checklistItemId":"@i05","result":"Fail","note":" "},{"checklistItemId":"@i08","result":"Pass","readings":[{"readingFieldId":"@supply","value":54.125}]}],"version":@v}""",
                job)));
        Assert.Equal([FailNoteMessage], await MessagesAsync(ruleBreaks, "answers[0].note"));
        Assert.Equal([InspectionReadingInput.ValueMessage], await MessagesAsync(ruleBreaks, "answers[1].readings[0].value"));
        var text = await ruleBreaks.Content.ReadAsStringAsync(CancellationToken);
        Assert.DoesNotContain("54.125", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(JobStatus.Completed, """{"answers":[{"checklistItemId":"@i05","result":"Fail"}],"version":@v}""", "answers[0].note")]
    [InlineData(JobStatus.Completed, """{"answers":[{"checklistItemId":"@foreign","result":"Pass"}],"version":@v}""", "answers[0].checklistItemId")]
    [InlineData(JobStatus.Completed, """{"answers":[{"checklistItemId":"@i08","result":"Pass","readings":[{"readingFieldId":"@supply","value":54.125}]}],"version":@v}""", "answers[0].readings[0].value")]
    [InlineData(JobStatus.Completed, """{"answers":[{"checklistItemId":"@i01","result":"Pass"},null],"version":@v}""", "answers[1]")]
    [InlineData(JobStatus.OnHold, """{"answers":[{"checklistItemId":"@i08","result":"NotApplicable","readings":[{"readingFieldId":"@supply","value":54}]}],"version":@v}""", "answers[0].readings")]
    [InlineData(JobStatus.OnHold, """{"answers":[{"checklistItemId":"@unknown","result":"Pass"}],"version":@v}""", "answers[0].checklistItemId")]
    [InlineData(JobStatus.Assigned, """{"answers":[{"checklistItemId":"@i03","result":"Pass","readings":[{"readingFieldId":"@suction","value":118}]}],"version":@v}""", "answers[0].readings")]
    [InlineData(JobStatus.Cancelled, """{"answers":[{"checklistItemId":"@i01","result":"Pass"},{"checklistItemId":"@i01","result":"Pass"}],"version":@v}""", "answers[1].checklistItemId")]
    public async Task EveryFieldErrorIsReportedBeforeTheLock(JobStatus status, string template, string key)
    {
        var (technician, job) = await ArrangeAsync(status, withDraft: status == JobStatus.OnHold);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(technician.User);

        using var response = await client.PutAsync(PathOf(job.Id), Json(Expand(template, job)));

        await ApiClient.AssertFieldErrorsAsync(response, key);
        Assert.Equal(state, await StateOfAsync(job.Id));
    }

    [Fact]
    public async Task ASaveAfterSubmissionGets409InspectionLockedAndTheInspectionNeverChanges()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.Completed);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(job.Version, Answer(1, "Fail", "Lockout hasp missing")));
        using var stale = await client.PutJsonAsync(PathOf(job.Id), Body(job.Version + 3, Answer(1, "Fail", "Lockout hasp missing")));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, Locked);
        Assert.Equal("The inspection can no longer be changed.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("The inspection was submitted and can no longer be changed.", problem.RootElement.GetProperty("detail").GetString());
        await ApiClient.AssertProblemAsync(stale, HttpStatusCode.Conflict, Locked);
        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task ASaveThatMeetsASubmissionCommittedAfterItsReadGets409InspectionLockedFromTheDatabaseAndChangesNothing()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        await DataAt(Earlier.AddMinutes(10)).SaveInspectionAsync(
            job,
            TestData.PassingAnswers(TestData.ChecklistOf(_templateId)),
            CancellationToken);
        var current = await LoadJobAsync(job.Id);
        var submission = new BeforeFirstSaveChanges(() => DataAt(Earlier.AddMinutes(20)).SubmitJobAsync(current, CancellationToken, "Submitted from the other device"));
        await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services =>
            services.ConfigureDbContext<FieldServiceDbContext>(options => options.AddInterceptors(submission)));
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, technician.User);

        using var response = await client.PutJsonAsync(
            PathOf(job.Id),
            Body(current.Version, Answer(5, "Fail", "Bearing noise at start-up."), Answer(9, "NotApplicable", "No economizer")));

        Assert.True(submission.Ran);
        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, Locked);
        Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.Inspections.AsNoTracking().WithAnswersAndReadings().SingleAsync(entry => entry.JobId == job.Id, CancellationToken);
        Assert.NotNull(stored.SubmittedAt);
        Assert.Equal("Submitted from the other device", stored.TechnicianNotes);
        Assert.All(stored.Answers, answer => Assert.Equal(InspectionResult.Pass, answer.Result));
        Assert.Equal(JobStatus.Completed, (await LoadJobAsync(job.Id)).Status);
        Assert.DoesNotContain(factory.LogSink.Events, entry => entry.Level >= Serilog.Events.LogEventLevel.Error);
        factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task ResendingTheSubmittedAnswersIsTheSameStateAndReturnsTheReadOnlyInspection()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.Completed);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(
            PathOf(job.Id),
            BodyOf(TestData.PassingAnswers(TestData.ChecklistOf(_templateId), 3), job.Version));

        var inspection = await ReadAsync(response);
        Assert.Equal(job.Version, inspection.Version);
        Assert.False(inspection.Affordances.CanEdit);
        Assert.NotNull(inspection.SubmittedAt);
        Assert.Equal(state, await StateOfAsync(job.Id));
    }

    [Theory]
    [InlineData(JobStatus.Assigned, false)]
    [InlineData(JobStatus.OnHold, true)]
    [InlineData(JobStatus.Cancelled, false)]
    public async Task ASaveWhileTheJobIsNotInProgressGets409InspectionLockedAndWritesNothing(JobStatus status, bool withDraft)
    {
        var (technician, job) = await ArrangeAsync(status, withDraft);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(job.Version, Answer(9, "Pass")));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, Locked);
        Assert.Equal(
            $"{JobNumber.Format(job.Number)} is {status}, so its inspection cannot be changed.",
            problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal(state, await StateOfAsync(job.Id));
        await using var db = Fixture.CreateAppDbContext();
        Assert.Equal(withDraft, await db.Inspections.AnyAsync(entry => entry.JobId == job.Id, CancellationToken));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task ABrokenRuleWinsOverAStaleVersion()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.OnHold, withDraft: true);
        using var client = ClientFor(technician.User);

        using var locked = await client.PutJsonAsync(PathOf(job.Id), Body(job.Version + 5, Answer(9, "Pass")));
        using var invalid = await client.PutJsonAsync(PathOf(job.Id), Body(job.Version + 5, Answer(9, "Fail")));

        await ApiClient.AssertProblemAsync(locked, HttpStatusCode.Conflict, Locked);
        await ApiClient.AssertFieldErrorsAsync(invalid, "answers[0].note");
    }

    [Fact]
    public async Task ReadingValuesWithAtMostTwoDecimalsAreStoredExactlyAndAFailMayCarryAnySubsetOfItsReadings()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);

        using var response = await client.PutAsync(
            PathOf(job.Id),
            Json(Expand(
                """{"answers":[{"checklistItemId":"@i03","result":"Fail","note":"Discharge too high","readings":[{"readingFieldId":"@discharge","value":99999999.99}]},{"checklistItemId":"@i08","result":"Pass","readings":[{"readingFieldId":"@supply","value":-99999999.99}]},{"checklistItemId":"@i09","result":"NotApplicable","readings":null}],"version":@v}""",
                job)));
        var inspection = await ReadAsync(response);
        using var next = await client.PutJsonAsync(PathOf(job.Id), Body(inspection.Version, Answer(8, "Pass", null, Reading(_supplyAir, 54.1m))));
        var updated = await ReadAsync(next);

        Assert.Equal([new InspectionReadingResponse(_discharge, 99999999.99m)], inspection.Items[2].Answer!.Readings);
        Assert.Equal([new InspectionReadingResponse(_supplyAir, -99999999.99m)], inspection.Items[7].Answer!.Readings);
        Assert.Equal([new InspectionReadingResponse(_supplyAir, 54.10m)], updated.Items[7].Answer!.Readings);
        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.Inspections.AsNoTracking().WithAnswersAndReadings().SingleAsync(entry => entry.JobId == job.Id, CancellationToken);
        Assert.Equal(
            ["99999999.99", "54.10"],
            stored.Answers.OrderBy(answer => answer.ChecklistItemId)
                .SelectMany(answer => answer.Readings)
                .Select(reading => reading.Value.ToString(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task AnsweringEveryItemMakesSubmissionAllowed()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(
            PathOf(job.Id),
            BodyOf(TestData.PassingAnswers(TestData.ChecklistOf(_templateId)), job.Version));

        var inspection = await ReadAsync(response);
        Assert.Equal(new InspectionProgress(10, 10, 10, 0, 0), inspection.Progress);
        Assert.Equal(new InspectionAffordances(true, new SubmitAffordance(true, null, 10, 10)), inspection.Affordances);
    }

    [Fact]
    public async Task AnotherTechniciansJobAnUnassignedJobAndAMissingJobReturnTheSame404AndNothingIsWritten()
    {
        var (_, job) = await ArrangeAsync(JobStatus.InProgress);
        var (_, unassigned) = await ArrangeAsync(JobStatus.Unassigned);
        var other = await DataAt(Earlier).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(other.User);

        using var forOther = await client.PutJsonAsync(PathOf(job.Id), Body(job.Version, Answer(1, "Pass")));
        using var forUnassigned = await client.PutJsonAsync(PathOf(unassigned.Id), Body(unassigned.Version, Answer(1, "Pass")));
        using var forMissing = await client.PutJsonAsync(PathOf(Guid.CreateVersion7()), Body(1, Answer(1, "Pass")));

        using var first = await ApiClient.AssertProblemAsync(forOther, HttpStatusCode.NotFound, "job.not-found");
        using var second = await ApiClient.AssertProblemAsync(forUnassigned, HttpStatusCode.NotFound, "job.not-found");
        using var third = await ApiClient.AssertProblemAsync(forMissing, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal(
            third.RootElement.EnumerateObject().Select(member => member.Name),
            first.RootElement.EnumerateObject().Select(member => member.Name));
        Assert.False(second.RootElement.TryGetProperty("detail", out _));
        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AnUnknownJobIsReportedBeforeAnUnknownItem()
    {
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(
            PathOf(Guid.CreateVersion7()),
            new { answers = new[] { new { checklistItemId = _unknownItem, result = "Pass" } }, version = 1 });

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.NotFound, "job.not-found");
    }

    [Fact]
    public async Task AnAdministratorGets403EvenWithAnInvalidBodyAndNothingIsWritten()
    {
        var (_, job) = await ArrangeAsync(JobStatus.InProgress);
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(administrator);

        using var valid = await client.PutJsonAsync(PathOf(job.Id), Body(job.Version, Answer(1, "Pass")));
        using var invalid = await client.PutJsonAsync(PathOf(job.Id), new { });

        await ApiClient.AssertProblemAsync(valid, HttpStatusCode.Forbidden, "auth.forbidden");
        await ApiClient.AssertProblemAsync(invalid, HttpStatusCode.Forbidden, "auth.forbidden");
        Assert.Equal(state, await StateOfAsync(job.Id));
    }

    [Fact]
    public async Task AnAnonymousCallerAnExpiredTokenAndATokenSignedWithAnotherKeyGet401AndNothingIsWritten()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        var state = await StateOfAsync(job.Id);
        await using var expiring = Fixture.CreateFactory();
        await using var foreign = Fixture.CreateFactory();
        using var anonymous = Fixture.Factory.CreateApiClient();
        using var expired = expiring.CreateApiClient();
        expired.AccessToken = TestTokens.For(expiring, technician.User);
        expiring.Clock.Advance(TimeSpan.FromMinutes(16));
        using var signedElsewhere = Fixture.Factory.CreateApiClient();
        signedElsewhere.AccessToken = TestTokens.For(foreign, technician.User);

        using var withoutToken = await anonymous.PutJsonAsync(PathOf(job.Id), Body(job.Version, Answer(1, "Pass")));
        using var withExpired = await expired.PutJsonAsync(PathOf(job.Id), Body(job.Version, Answer(1, "Pass")));
        using var withForeign = await signedElsewhere.PutJsonAsync(PathOf(job.Id), Body(job.Version, Answer(1, "Pass")));

        await ApiClient.AssertProblemAsync(withoutToken, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await ApiClient.AssertProblemAsync(withExpired, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await ApiClient.AssertProblemAsync(withForeign, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        Assert.Equal(state, await StateOfAsync(job.Id));
    }

    [Fact]
    public async Task AJobIdThatIsNotAGuidMatchesNoRouteAMissingBodyGets400AndAnotherMediaTypeGets415()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);

        using var notAGuid = await client.PutJsonAsync("/api/jobs/1042/inspection", Body(job.Version, Answer(1, "Pass")));
        using var missing = await client.PutAsync(PathOf(job.Id));
        using var text = await client.PutAsync(
            PathOf(job.Id),
            new StringContent(Expand("""{"answers":[{"checklistItemId":"@i01","result":"Pass"}],"version":@v}""", job), Encoding.UTF8, "text/plain"));

        await ApiClient.AssertProblemAsync(notAGuid, HttpStatusCode.NotFound, "resource.not-found");
        await ApiClient.AssertFieldErrorsAsync(missing, "body");
        await ApiClient.AssertProblemAsync(text, HttpStatusCode.UnsupportedMediaType, "request.unsupported-media-type");
    }

    [Fact]
    public async Task SavingLogsNoNoteNameEmailOrJobTextAtInformationLevel()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        const string Note = "Condensate pan cracked near the Harbor Supply bracket";
        await using var factory = Fixture.CreateFactory(factory =>
        {
            factory.Settings["Serilog:MinimumLevel:Default"] = "Information";
            factory.Settings["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Information";
        });
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, technician.User);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(job.Version, Answer(7, "Fail", Note)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(factory.LogSink.Events);
        foreach (var text in new[]
        {
            Note, "Harbor Supply", technician.User.DisplayName, technician.User.Email, technician.PhoneNumber,
            job.Title, job.CustomerName, job.SiteAddress,
        })
        {
            Assert.All(factory.LogSink.Events, logEvent =>
                Assert.DoesNotContain(text, CapturingLogSink.Text(logEvent), StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task EachWriteIsOneBatchThatChangesTheInspectionRowsBeforeTheJobRowAndAReadTakesTheVersionFirst()
    {
        await using var factory = Fixture.CreateFactory(factory =>
        {
            factory.Settings["Serilog:MinimumLevel:Default"] = "Information";
            factory.Settings["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Information";
        });
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress, UtcInstant.Truncate(factory.Clock.GetUtcNow()).AddMinutes(-30));
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, technician.User);
        factory.LogSink.Clear();

        using var first = await client.PutJsonAsync(
            PathOf(job.Id),
            Body(job.Version, Answer(8, "Pass", null, Reading(_supplyAir, 54m)), Answer(1, "Pass")));
        var afterFirst = await ReadAsync(first);
        var firstSave = CommandsOf(factory);
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        client.AccessToken = TestTokens.For(factory, technician.User);
        using var later = await client.PutJsonAsync(
            PathOf(job.Id),
            Body(afterFirst.Version, Answer(8, "NotApplicable", "Sensor missing"), Answer(2, "Pass")));
        var afterLater = await ReadAsync(later);
        var laterSave = CommandsOf(factory);
        using var fill = await client.PutJsonAsync(
            PathOf(job.Id),
            BodyOf(TestData.PassingAnswers(TestData.ChecklistOf(_templateId)), afterLater.Version));
        var full = await ReadAsync(fill);
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        client.AccessToken = TestTokens.For(factory, technician.User);
        factory.LogSink.Clear();
        using var submit = await client.PostJsonAsync(
            $"/api/jobs/{job.Id}/submission",
            new { technicianNotes = "Done", siteSafetyConfirmed = true, version = full.Version });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var submission = CommandsOf(factory);
        using var read = await client.GetAsync(PathOf(job.Id));
        await ReadAsync(read);
        var reading = CommandsOf(factory);

        AssertOneWriteInOrder(
            firstSave,
            "INSERT INTO fieldservice.inspections ",
            "UPDATE fieldservice.jobs ",
            "INSERT INTO fieldservice.inspection_answers ",
            "INSERT INTO fieldservice.inspection_readings ");
        AssertOneWriteInOrder(
            laterSave,
            "UPDATE fieldservice.inspection_answers ",
            "INSERT INTO fieldservice.inspection_answers ",
            "DELETE FROM fieldservice.inspection_readings",
            "UPDATE fieldservice.inspections ",
            "UPDATE fieldservice.jobs ");
        AssertOneWriteInOrder(
            submission,
            "UPDATE fieldservice.inspections ",
            "INSERT INTO fieldservice.job_history ",
            "UPDATE fieldservice.jobs ");
        Assert.Equal(3, reading.Count);
        Assert.All(reading, command => Assert.StartsWith("SELECT", command, StringComparison.Ordinal));
        Assert.Contains("j.xmin", reading[0], StringComparison.Ordinal);
        Assert.Contains("FROM fieldservice.inspections", reading[2], StringComparison.Ordinal);
    }

    public static string PathOf(Guid jobId) => $"/api/jobs/{jobId}/inspection";

    public static object Body(uint version, params object[] answers) => new { answers, version };

    public static object BodyOf(IEnumerable<ChecklistAnswer> answers, uint version) =>
        new
        {
            answers = answers.Select(answer => new
            {
                checklistItemId = answer.ChecklistItemId,
                result = answer.Result.ToString(),
                note = answer.Note,
                readings = answer.Readings.Select(reading => Reading(reading.ReadingFieldId, reading.Value)).ToArray(),
            }).ToArray(),
            version,
        };

    public static object Answer(int position, string result, string? note = null, params object[] readings) =>
        new { checklistItemId = Item(position), result, note, readings };

    public static object Reading(Guid readingFieldId, decimal value) => new { readingFieldId, value };

    public static Guid Item(int position) => ChecklistTemplateSeed.ItemId(2, position);

    public static string Expand(string template, Job job)
    {
        var expanded = template
            .Replace("@v", job.Version.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("@foreign", ChecklistTemplateSeed.ItemId(1, 1).ToString(), StringComparison.Ordinal)
            .Replace("@unknown", _unknownItem.ToString(), StringComparison.Ordinal)
            .Replace("@suction", _suction.ToString(), StringComparison.Ordinal)
            .Replace("@discharge", _discharge.ToString(), StringComparison.Ordinal)
            .Replace("@supply", _supplyAir.ToString(), StringComparison.Ordinal);
        for (var position = 10; position >= 1; position--)
        {
            expanded = expanded.Replace(
                string.Create(CultureInfo.InvariantCulture, $"@i{position:D2}"),
                Item(position).ToString(),
                StringComparison.Ordinal);
        }

        return expanded;
    }

    private static ChecklistAnswer Saved(int position, InspectionResult result, string? note = null) =>
        new(Item(position), result, note, []);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static List<string> CommandsOf(ApiFactory factory)
    {
        var commands = factory.LogSink.Events
            .Where(entry => CapturingLogSink.Scalar(entry, "SourceContext") == "Microsoft.EntityFrameworkCore.Database.Command"
                && CapturingLogSink.Scalar(entry, "commandText") is not null)
            .Select(entry => CapturingLogSink.Scalar(entry, "commandText")!)
            .ToList();
        factory.LogSink.Clear();
        return commands;
    }

    private static void AssertOneWriteInOrder(List<string> commands, params string[] fragments)
    {
        var write = Assert.Single(commands, command => !command.StartsWith("SELECT", StringComparison.Ordinal));
        var positions = fragments.Select(fragment => write.IndexOf(fragment, StringComparison.Ordinal)).ToList();
        Assert.All(positions, position => Assert.True(position >= 0, write));
        Assert.True(positions.SequenceEqual(positions.Order()), write);
    }

    private static async Task<InspectionResponse> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var inspection = await response.Content.ReadFromJsonAsync<InspectionResponse>(ApiClient.Json, TestContext.Current.CancellationToken);
        return inspection ?? throw new InvalidOperationException("The response has no inspection body.");
    }

    private static async Task<IReadOnlyList<string?>> MessagesAsync(HttpResponseMessage response, string key)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return [.. problem.RootElement.GetProperty("errors").GetProperty(key).EnumerateArray().Select(message => message.GetString())];
    }

    private async Task<(Technician Technician, Job Job)> ArrangeAsync(
        JobStatus status,
        bool withDraft = false,
        DateTimeOffset? at = null)
    {
        var start = at ?? Earlier;
        var administrator = await DataAt(start).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(start).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var created = await DataAt(start).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        if (!withDraft)
        {
            return (technician, await DataAt(start.AddMinutes(5)).MoveToAsync(created, technician, status, CancellationToken));
        }

        var job = await DataAt(start.AddMinutes(5)).MoveToAsync(created, technician, JobStatus.InProgress, CancellationToken);
        await DataAt(start.AddMinutes(10)).SaveInspectionAsync(
            job,
            [Saved(1, InspectionResult.Pass), Saved(2, InspectionResult.Pass)],
            CancellationToken);
        if (status == JobStatus.OnHold)
        {
            await DataAt(start.AddMinutes(15)).ChangeStatusAsync(job, JobStatus.OnHold, CancellationToken, TestData.HoldNote);
        }

        return (technician, await LoadJobAsync(job.Id));
    }

    private Task<(Technician Technician, Job Job)> ArrangeAsync(JobStatus status, DateTimeOffset at) =>
        ArrangeAsync(status, withDraft: false, at);

    private async Task<Job> LoadJobAsync(Guid jobId)
    {
        await using var db = Fixture.CreateAppDbContext();
        return await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == jobId, CancellationToken);
    }

    private async Task<int> HistoryCountAsync(Guid jobId)
    {
        await using var db = Fixture.CreateAppDbContext();
        return await db.JobHistory.CountAsync(entry => entry.JobId == jobId, CancellationToken);
    }

    private async Task<string> StateOfAsync(Guid jobId)
    {
        await using var db = Fixture.CreateAppDbContext();
        var job = await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == jobId, CancellationToken);
        var history = await db.JobHistory.CountAsync(entry => entry.JobId == jobId, CancellationToken);
        var inspection = await db.Inspections.AsNoTracking().WithAnswersAndReadings().SingleOrDefaultAsync(entry => entry.JobId == jobId, CancellationToken);
        var answers = inspection is null
            ? "none"
            : string.Join(
                ";",
                inspection.Answers
                    .OrderBy(answer => answer.ChecklistItemId)
                    .Select(answer => string.Create(
                        CultureInfo.InvariantCulture,
                        $"{answer.ChecklistItemId}:{answer.Result}:{answer.Note}:{answer.AnsweredAt:O}:{string.Join(',', answer.Readings.OrderBy(reading => reading.ReadingFieldId).Select(reading => string.Create(CultureInfo.InvariantCulture, $"{reading.ReadingFieldId}={reading.Value}")))}")));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{job.Version}|{job.Status}|{job.UpdatedAt:O}|{history}|{inspection?.UpdatedAt:O}|{inspection?.SubmittedAt:O}|{inspection?.TechnicianNotes}|{answers}");
    }

    private TestData DataAt(DateTimeOffset now) => new(Fixture, now);

    private ApiClient ClientFor(User user)
    {
        var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, user);
        return client;
    }
}
