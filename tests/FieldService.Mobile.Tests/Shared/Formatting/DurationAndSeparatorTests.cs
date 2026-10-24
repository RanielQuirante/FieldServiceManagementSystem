using System.Xml.Linq;
using FieldService.Mobile.Features.Inspection;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Formatting;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Formatting;

public sealed class DurationAndSeparatorTests
{
    private static readonly DateOnly _today = new(2026, 10, 19);
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";

    [Fact]
    public void TheSeparatorIsANoBreakSpaceTheMiddleDotAndAnOrdinarySpace()
    {
        Assert.Equal([0x00A0, 0x00B7, 0x0020], DisplayFormats.Separator.Select(character => (int)character));
        Assert.Equal(Spaces.NoBreak, DisplayFormats.Separator[0]);
        Assert.Equal(Spaces.MiddleDot, DisplayFormats.Separator[1]);
    }

    [Theory]
    [InlineData(0, "0_min")]
    [InlineData(35, "35_min")]
    [InlineData(59, "59_min")]
    [InlineData(60, "1_h")]
    [InlineData(540, "9_h")]
    [InlineData(553, "9_h_13_min")]
    [InlineData(2970, "49_h_30_min")]
    public void ADurationJoinsEveryNumberAndUnitWithNoBreakSpaces(int minutes, string expected)
    {
        var text = DisplayFormats.Duration(TimeSpan.FromMinutes(minutes));

        Assert.Equal(Spaces.Text(expected), text);
        Assert.DoesNotContain(' ', text);
    }

    [Fact]
    public void TheStartsLineMayBreakBeforeItsDurationButNeverInsideIt()
    {
        var start = new DateTimeOffset(2026, 10, 19, 13, 30, 0, TimeSpan.Zero);

        var line = DisplayFormats.StartsLine(start, start.AddHours(-2).AddMinutes(-48), TimeZoneInfo.Utc);

        Assert.Equal(Spaces.Text("Starts in 2_h_48_min"), line);
    }

    [Fact]
    public void EverySeparatedLineKeepsItsDotOnTheLineBeforeItsNextPart()
    {
        var lines = new[]
        {
            DisplayFormats.DayGroupLabel(_today, _today),
            DisplayFormats.DayGroupLabel(_today.AddDays(1), _today),
            ReviewAndSubmitViewModel.TimeOnSite(new DateTimeOffset(2026, 10, 18, 22, 16, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 19, 7, 29, 0, TimeSpan.Zero), TimeZoneInfo.Utc, _today),
            "6 of 10 answered" + JobDetailsViewModel.NotSubmittedSuffix,
        };

        Assert.Equal(DisplayFormats.Separator + "not submitted", JobDetailsViewModel.NotSubmittedSuffix);
        Assert.Equal(Spaces.Text("Sun_18_Oct, 10:16_PM_– 7:29_AM_· 9_h_13_min"), lines[2]);
        Assert.All(lines, line =>
        {
            var dot = line.IndexOf(Spaces.MiddleDot, StringComparison.Ordinal);
            Assert.True(dot > 0, line);
            Assert.Equal(Spaces.NoBreak, line[dot - 1]);
            Assert.Equal(' ', line[dot + 1]);
        });
    }

    [Fact]
    public void EverySeparatorInTheAppComesFromDisplayFormats()
    {
        var sources = RepositoryPaths.MobileSources("*.cs").Concat(RepositoryPaths.MobileSources("*.xaml")).ToList();
        var title = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Features", "Jobs", "UpdateStatusSheetTitle.xaml"));
        var spans = title.Descendants(_maui + "Span").Select(span => span.Attribute("Text")?.Value).ToList();

        Assert.Contains("Shared/Formatting/DisplayFormats.cs", sources.Select(RepositoryPaths.ToRelative));
        Assert.DoesNotContain(sources, path => File.ReadAllText(path).Contains(Spaces.MiddleDot, StringComparison.Ordinal));
        Assert.Equal(
            ["Shared/Formatting/DisplayFormats.cs"],
            sources.Where(path => File.ReadAllText(path).Contains("U000000B7", StringComparison.Ordinal)).Select(RepositoryPaths.ToRelative));
        Assert.Equal(["{Binding JobNumber}", "{x:Static formatting:DisplayFormats.Separator}", "{Binding Title}"], spans);
    }
}
