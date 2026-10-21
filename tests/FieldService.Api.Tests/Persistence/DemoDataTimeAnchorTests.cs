using System.Globalization;
using FieldService.Api.Persistence.Seeding;
using Microsoft.Extensions.Time.Testing;

namespace FieldService.Api.Tests.Persistence;

public sealed class DemoDataTimeAnchorTests
{
    private static readonly TimeZoneInfo _newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public static TheoryData<string, int> ShiftsInUtc => new()
    {
        { "2026-10-19T10:42:00Z", 0 },
        { "2026-10-19T11:41:59Z", 0 },
        { "2026-10-19T11:42:00Z", 1 },
        { "2026-10-19T10:41:59Z", -1 },
        { "2026-10-19T02:42:00Z", -8 },
        { "2026-10-19T02:41:59Z", -9 },
        { "2026-10-19T19:41:59Z", 8 },
        { "2026-10-19T00:00:00Z", -11 },
        { "2026-10-19T23:59:59Z", 13 },
    };

    [Fact]
    public void AtTenFortyTwoInUtcTheShiftIsZeroAndEveryInstantIsItsDesignTime()
    {
        var anchor = DemoTimeAnchor.From(new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 10, 42, 0, TimeSpan.Zero)));

        Assert.Equal(new DateOnly(2026, 10, 19), anchor.Day);
        Assert.Equal(0, anchor.DeltaHours);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 10, 42, 0, TimeSpan.Zero), anchor.Resolve(new DemoTime(0, 10, 42)));
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 16, 40, 0, TimeSpan.Zero), anchor.Resolve(new DemoTime(-1, 16, 40)));
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 9, 0, 0, TimeSpan.Zero), anchor.Resolve(DemoData.UsersCreatedAt));
    }

    [Theory]
    [MemberData(nameof(ShiftsInUtc))]
    public void TheShiftIsTheWholeNumberOfHoursSinceTenFortyTwoRoundedDown(string now, int expected)
    {
        var anchor = DemoTimeAnchor.From(new FakeTimeProvider(DateTimeOffset.Parse(now, CultureInfo.InvariantCulture)));

        Assert.Equal(expected, anchor.DeltaHours);
        Assert.Equal(new DateOnly(2026, 10, 19), anchor.Day);
    }

    [Fact]
    public void OnTheSpringChangeDayInNewYorkEachTimeConvertsOnItsOwnDayAndThenShifts()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2027, 3, 14, 9, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(_newYork);

        var anchor = DemoTimeAnchor.From(clock);

        Assert.Equal(new DateOnly(2027, 3, 14), anchor.Day);
        Assert.Equal(-6, anchor.DeltaHours);
        Assert.Equal(new DateTimeOffset(2027, 3, 14, 6, 14, 0, TimeSpan.Zero), anchor.Resolve(new DemoTime(0, 8, 14)));
        Assert.Equal(new DateTimeOffset(2027, 3, 14, 6, 20, 0, TimeSpan.Zero), anchor.Resolve(new DemoTime(0, 8, 20)));
        Assert.Equal(new DateTimeOffset(2027, 3, 14, 7, 12, 0, TimeSpan.Zero), anchor.Resolve(new DemoTime(0, 9, 12)));
        Assert.Equal(new DateTimeOffset(2027, 3, 13, 15, 40, 0, TimeSpan.Zero), anchor.Resolve(new DemoTime(-1, 16, 40)));
        var job1042 = DemoTimeline.Resolve(DemoData.Jobs.Single(job => job.Number == 1042), anchor);
        Assert.Equal(["Created", "Assigned", "Started", "InspectionSaved", "InspectionSaved"], job1042.Select(step => step.Name));
        Assert.True(job1042.Zip(job1042.Skip(1)).All(pair => pair.First.At < pair.Second.At));
    }

    [Fact]
    public void ALocalTimeInASpringGapMovesForwardByTheGapAndAnAmbiguousTimeTakesStandardTime()
    {
        var gap = DemoTimeAnchor.ToUtc(new DateTime(2027, 3, 14, 2, 30, 0), _newYork);
        var ambiguous = DemoTimeAnchor.ToUtc(new DateTime(2026, 11, 1, 1, 30, 0), _newYork);

        Assert.Equal(new DateTimeOffset(2027, 3, 14, 7, 30, 0, TimeSpan.Zero), gap);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), ambiguous);
    }

    [Fact]
    public void EveryTimelineIncreasesStrictlyAroundEachDaylightSavingChangeForShiftsFromMinusEightToEight()
    {
        var zones = new[] { "America/New_York", "Europe/Berlin", "Australia/Sydney", "America/Santiago" };
        var runs = 0;
        foreach (var zone in zones.Select(TimeZoneInfo.FindSystemTimeZoneById))
        {
            foreach (var change in ChangeDays(zone, new DateOnly(2026, 10, 1), new DateOnly(2027, 9, 30)))
            {
                for (var dayOffset = -4; dayOffset <= 3; dayOffset++)
                {
                    var day = change.AddDays(dayOffset);
                    var designNow = DemoTimeAnchor.ToUtc(day.ToDateTime(DemoTimeAnchor.DesignNow), zone);
                    for (var delta = -8; delta <= 8; delta++)
                    {
                        var clock = new FakeTimeProvider(designNow.AddHours(delta).AddMinutes(30));
                        clock.SetLocalTimeZone(zone);
                        var anchor = DemoTimeAnchor.From(clock);
                        Assert.Equal((day, delta), (anchor.Day, anchor.DeltaHours));

                        DemoTimeline.EnsureStrictlyIncreasing(DemoData.Jobs, anchor);
                        runs++;
                    }
                }
            }
        }

        Assert.Equal(1088, runs);
    }

    [Fact]
    public void TheOrderingGuardRefusesATimelineThatDoesNotIncreaseStrictly()
    {
        var created = new DateTimeOffset(2027, 3, 14, 7, 14, 0, TimeSpan.Zero);

        var backwards = Assert.Throws<InvalidOperationException>(() => DemoTimeline.EnsureStrictlyIncreasing(
            1042,
            [new("Created", created), new("Assigned", created.AddMinutes(6)), new("Started", created.AddMinutes(-2))]));
        var equal = Assert.Throws<InvalidOperationException>(() => DemoTimeline.EnsureStrictlyIncreasing(
            1039,
            [new("Created", created), new("Assigned", created)]));
        DemoTimeline.EnsureStrictlyIncreasing(1043, [new("Created", created), new("Assigned", created.AddTicks(1))]);

        Assert.Equal(
            "Demo data was not seeded: the timeline of JOB-1042 must increase strictly, but step 3 (Started, 2027-03-14T07:12:00Z) is not after step 2 (Assigned, 2027-03-14T07:20:00Z). Seed at another time of day, or reset the database and start the API again later.",
            backwards.Message);
        Assert.Contains("JOB-1039", equal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReplayBuildsTheWholeSetThroughTheDomainMethodsWithTheReviewedValues()
    {
        var anchor = DemoTimeAnchor.From(new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 10, 42, 0, TimeSpan.Zero)));
        var hashed = new List<string>();

        var data = DemoDataSet.Replay(anchor, user =>
        {
            hashed.Add(user.Email);
            return "hash";
        });

        Assert.Equal(new DemoDataCounts(7, 6, 20, 49, 3, 22, 6), data.Counts);
        Assert.Equal(DemoData.Users.Select(user => user.Email), hashed);
        Assert.All(data.Users, user => Assert.Equal("hash", user.PasswordHash));
        var job = (long number) => data.Jobs.Single(entry => entry.Number == number).Job;
        Assert.Equal("Waiting for replacement gas valve", job(1044).History[^1].Note);
        Assert.Equal("Building management replaced the filters under its own maintenance contract.", job(1036).History[^1].Note);
        Assert.Equal(
            [18.00m, 118.00m, 285.00m, 277.00m, 250.00m, 0.40m],
            data.Inspections.OrderBy(inspection => job(1039).Id == inspection.JobId ? 0 : job(1042).Id == inspection.JobId ? 1 : 2)
                .SelectMany(inspection => inspection.Answers.SelectMany(answer => answer.Readings))
                .Select(reading => reading.Value));
        Assert.All(data.Jobs, entry => Assert.True(entry.Job.History[^1].OccurredAt <= entry.Job.UpdatedAt));
    }

    private static IEnumerable<DateOnly> ChangeDays(TimeZoneInfo zone, DateOnly from, DateOnly to)
    {
        for (var day = from; day < to; day = day.AddDays(1))
        {
            var start = DemoTimeAnchor.ToUtc(day.ToDateTime(TimeOnly.MinValue), zone);
            var end = DemoTimeAnchor.ToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
            if (zone.GetUtcOffset(start) != zone.GetUtcOffset(end))
            {
                yield return day;
            }
        }
    }
}
