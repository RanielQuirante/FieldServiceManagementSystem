using FieldService.Mobile.Features.Inspection;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Formatting;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class TimeWindowBreakTests
{
    private const char ZeroWidth = (char)0x200B;
    private const char NoBreak = (char)0x00A0;

    private static readonly DateOnly _today = new(2026, 10, 19);

    [Fact]
    public void TheBreakMarkIsOneZeroWidthSpaceAndTheJoinIsANoBreakSpace()
    {
        Assert.Equal(ZeroWidth, Assert.Single(DisplayFormats.ZeroWidthSpace));
        Assert.Equal(NoBreak, DisplayFormats.NoBreakSpace);
    }

    [Fact]
    public async Task ACrossMidnightScheduleLineMayBreakOnlyRightAfterItsDashAndKeepsItsNoBreakSpaces()
    {
        var viewModel = await LoadAsync(CrossMidnight1047(JobDtoBuilder.Local(10, 18, 23, 30)));

        Assert.Equal(Text("Sun_18_Oct, 11:30_PM–|Mon_19_Oct, 1:00_AM"), viewModel.ScheduleLine);
        Assert.Equal(viewModel.ScheduleLine.IndexOf('–', StringComparison.Ordinal) + 1, viewModel.ScheduleLine.IndexOf(ZeroWidth, StringComparison.Ordinal));
        Assert.Equal(1, viewModel.ScheduleLine.Count(character => character == ZeroWidth));
    }

    [Fact]
    public async Task AScheduleLineStartingTodayAndEndingTomorrowAlsoBreaksAfterItsDash()
    {
        var viewModel = await LoadAsync(CrossMidnight1047(JobDtoBuilder.Local(10, 19, 23, 30)));

        Assert.Equal(Text("Today 11:30_PM–|Tue_20_Oct, 1:00_AM"), viewModel.ScheduleLine);
    }

    [Fact]
    public async Task ACrossMidnightScheduledRowMayBreakOnlyRightAfterItsDashAndItsDescriptionIsUnchanged()
    {
        var job = JobDtoBuilder.Details1042() with { ScheduledStart = JobDtoBuilder.Local(10, 18, 23, 0), ScheduledEnd = JobDtoBuilder.Local(10, 19, 1, 0) };
        var viewModel = await LoadAsync(job);
        var unbroken = DisplayFormats.Scheduled(job.ScheduledStart, job.ScheduledEnd, JobDtoBuilder.Zone, _today);

        Assert.True(viewModel.ShowsScheduledRow);
        Assert.Equal(Text("Sun_18_Oct, 11:00_PM–|Mon_19_Oct, 1:00_AM"), viewModel.ScheduledText);
        Assert.Equal(Text("Scheduled, Sun_18_Oct, 11:00_PM–Mon_19_Oct, 1:00_AM"), InfoRow.Describe("Scheduled", viewModel.ScheduledText, null));
        Assert.Equal(InfoRow.Describe("Scheduled", unbroken, null), InfoRow.Describe("Scheduled", viewModel.ScheduledText, null));
    }

    [Fact]
    public void AnInfoRowDescriptionDropsEveryZeroWidthSpaceAndKeepsEverythingElse()
    {
        Assert.Equal(Text("Scheduled, Mon_19_Oct, 9:00–11:00_AM, note"), InfoRow.Describe("Scheduled", Text("Mon_19_Oct, 9:00–|11:00_AM"), Text("no|te")));
        Assert.Equal("Contact, Nina Alvarez, (555) 014-6620", InfoRow.Describe("Contact", "Nina Alvarez", "(555) 014-6620"));
    }

    [Fact]
    public async Task NoDescriptionOfACrossMidnightJobHoldsAZeroWidthSpace()
    {
        var assigned = await LoadAsync(CrossMidnight1047(JobDtoBuilder.Local(10, 18, 23, 30)));
        var inProgress = await LoadAsync(JobDtoBuilder.Details1042() with { ScheduledStart = JobDtoBuilder.Local(10, 18, 23, 0), ScheduledEnd = JobDtoBuilder.Local(10, 19, 1, 0) });
        var spoken = new[] { assigned, inProgress }
            .SelectMany(viewModel => typeof(JobDetailsViewModel).GetProperties()
                .Where(property => property.PropertyType == typeof(string) && (property.Name.EndsWith("Description", StringComparison.Ordinal) || property.Name.EndsWith("Label", StringComparison.Ordinal)))
                .Select(property => (string?)property.GetValue(viewModel) ?? string.Empty))
            .ToList();
        var card = JobListItem.From(
            JobDtoBuilder.Assigned(JobDtoBuilder.Job1047, 1047, "Walk-in freezer temperature alarm", JobPriority.Urgent, JobStatus.Assigned, "Elm Street Bakery", "88 Elm Street", JobDtoBuilder.Local(10, 18, 23, 30), JobDtoBuilder.Local(10, 19, 1, 0)),
            JobDtoBuilder.Zone,
            _today,
            new DateOnly(2026, 10, 18));

        Assert.True(spoken.Count >= 8, $"{spoken.Count} descriptions read");
        Assert.All(spoken, text => Assert.DoesNotContain(ZeroWidth, text));
        Assert.DoesNotContain(ZeroWidth, card.Description);
        Assert.Contains(Text("11:30_PM to Mon_19_Oct, 1:00_AM"), card.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void TimeOnSiteTheSameDayMayBreakOnlyRightAfterItsDash()
    {
        var text = ReviewAndSubmitViewModel.TimeOnSite(JobDtoBuilder.Local(10, 19, 9, 12), JobDtoBuilder.Local(10, 19, 13, 5), JobDtoBuilder.Zone, _today);

        Assert.Equal(Text("9:12_AM–|1:05_PM_· 3_h_53_min"), text);
    }

    [Fact]
    public void TimeOnSiteAcrossMidnightKeepsItsSpacedDashOnTheLineBeforeIt()
    {
        var text = ReviewAndSubmitViewModel.TimeOnSite(JobDtoBuilder.Local(10, 18, 22, 16), JobDtoBuilder.Local(10, 19, 5, 11), JobDtoBuilder.Zone, _today);
        var dash = text.IndexOf('–', StringComparison.Ordinal);

        Assert.Equal(Text("Sun_18_Oct, 10:16_PM_– 5:11_AM_· 6_h_55_min"), text);
        Assert.Equal(NoBreak, text[dash - 1]);
        Assert.Equal(' ', text[dash + 1]);
        Assert.DoesNotContain(ZeroWidth, text);
    }

    private static string Text(string pattern) => pattern.Replace('_', NoBreak).Replace('|', ZeroWidth);

    private static JobDetailsResponse CrossMidnight1047(DateTimeOffset start) =>
        JobDtoBuilder.Details1047() with { ScheduledStart = start, ScheduledEnd = start.AddMinutes(90) };

    private static async Task<JobDetailsViewModel> LoadAsync(JobDetailsResponse job)
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(job)));
        var viewModel = harness.CreateDetails();
        await viewModel.ShowJobAsync(job.Id);
        return viewModel;
    }
}
