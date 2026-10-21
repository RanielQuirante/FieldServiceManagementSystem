using System.Globalization;
using System.Security.Cryptography;
using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using NSubstitute;
using Serilog.Events;

namespace FieldService.Api.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class DemoDataSeederTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string Development = "Development";

    private static readonly DateTimeOffset _designNow = new(2026, 10, 19, 10, 42, 0, TimeSpan.Zero);

    private static readonly DateOnly _designDay = new(2026, 10, 19);

    private static readonly DemoDataCounts _expectedCounts = new(7, 6, 20, 49, 3, 22, 6);

    private static readonly IPasswordHasher<User> _hasher = new PasswordHasher<User>(
        Options.Create(new PasswordHasherOptions { IterationCount = PasswordVerifier.IterationCount }));

    private readonly string _password = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public static TheoryData<string, bool, int?, DemoDataSeedOutcome, string> SkippedRuns => new()
    {
        { Development, false, 16, DemoDataSeedOutcome.Disabled, "Seed:DemoData is false" },
        { "Production", true, 16, DemoDataSeedOutcome.Production, "the environment is Production" },
        { Development, true, 11, DemoDataSeedOutcome.PasswordTooShort, "Seed:DemoPassword has fewer than 12 characters" },
        { "Staging", true, null, DemoDataSeedOutcome.PasswordTooShort, "Seed:DemoPassword has fewer than 12 characters" },
    };

    [Theory]
    [MemberData(nameof(SkippedRuns))]
    public async Task AGuardThatFailsWritesNothingAndLogsOneInformationEventNamingTheReason(
        string environment,
        bool demoData,
        int? passwordLength,
        DemoDataSeedOutcome expected,
        string reason)
    {
        await using var db = Fixture.CreateAppDbContext();
        var logger = new RecordingLogger();
        var password = passwordLength is { } length ? _password.PadRight(16, 'x')[..length] : null;
        var seeder = CreateSeeder(db, Clock(_designNow), logger, environment, demoData, password);

        var result = await seeder.SeedAsync(CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Counts);
        Assert.Equal(new DemoDataCounts(0, 0, 0, 0, 0, 0, 0), await CountRowsAsync());
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(DemoDataLog.SkippedEventName, entry.EventId.Name);
        Assert.Equal($"Demo data seeding skipped: {reason}", entry.Message);
    }

    [Fact]
    public async Task ADatabaseThatAlreadyHasUsersIsNeverExtended()
    {
        var data = new TestData(Fixture, _designNow);
        await data.CreateAdministratorAsync(CancellationToken);
        await using var db = Fixture.CreateAppDbContext();
        var logger = new RecordingLogger();

        var result = await CreateSeeder(db, Clock(_designNow), logger).SeedAsync(CancellationToken);

        Assert.Equal(DemoDataSeedOutcome.DataPresent, result.Outcome);
        Assert.Equal(new DemoDataCounts(1, 0, 0, 0, 0, 0, 0), await CountRowsAsync());
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(DemoDataLog.SkippedEventName, entry.EventId.Name);
        Assert.Equal("Demo data seeding skipped: demo data present", entry.Message);
    }

    [Fact]
    public async Task OneRunWritesTheWholeSetInOneTransactionAndCreateJobContinuesAtJob1056()
    {
        await RestartJobNumbersAsync();
        await using var db = Fixture.CreateAppDbContext();
        var logger = new RecordingLogger();

        var result = await CreateSeeder(db, Clock(_designNow), logger).SeedAsync(CancellationToken);

        Assert.Equal(DemoDataSeedOutcome.Seeded, result.Outcome);
        Assert.Equal(_expectedCounts, result.Counts);
        Assert.Equal(1056, result.NextJobNumber);
        Assert.Equal(_expectedCounts, await CountRowsAsync());
        Assert.Equal(1, await ScalarAsync<long>(
            """
            SELECT count(DISTINCT x) FROM (
                SELECT xmin::text AS x FROM fieldservice.users
                UNION ALL SELECT xmin::text FROM fieldservice.technicians
                UNION ALL SELECT xmin::text FROM fieldservice.jobs
                UNION ALL SELECT xmin::text FROM fieldservice.job_history
                UNION ALL SELECT xmin::text FROM fieldservice.inspections
                UNION ALL SELECT xmin::text FROM fieldservice.inspection_answers
                UNION ALL SELECT xmin::text FROM fieldservice.inspection_readings) AS rows
            """));
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(DemoDataLog.CompletedEventName, entry.EventId.Name);
        Assert.Equal(
            "Demo data seeded: 7 users, 6 technicians, 20 jobs, 49 history entries, 3 inspections, 22 answers and 6 readings; the next job number is 1056",
            entry.Message);
        AssertNoPersonalData(entry.Message);

        await using var check = Fixture.CreateAppDbContext();
        var dana = await check.Users.SingleAsync(user => user.Role == UserRole.Administrator, CancellationToken);
        var next = await new TestData(Fixture, _designNow).CreateJobAsync(dana.Id, TestData.HvacTemplateId, CancellationToken);
        Assert.Equal(1056, next.Number);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.VerifyHashedPassword(dana, dana.PasswordHash, _password));
    }

    [Fact]
    public async Task TheRunTakesTheAdvisoryLockBeforeItWritesAnything()
    {
        await using var holder = await Fixture.OpenAppConnectionAsync(CancellationToken);
        await using var hold = await holder.BeginTransactionAsync(CancellationToken);
        await using (var lockCommand = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({DemoDataSeeder.AdvisoryLockKey})", holder, hold))
        {
            await lockCommand.ExecuteNonQueryAsync(CancellationToken);
        }

        await using var db = Fixture.CreateAppDbContext();
        var seeding = CreateSeeder(db, Clock(_designNow), new RecordingLogger()).SeedAsync(CancellationToken);
        var waited = await WaitForLockWaitAsync();
        var usersWhileWaiting = await ScalarAsync<long>("SELECT count(*) FROM fieldservice.users");
        var finishedWhileWaiting = seeding.IsCompleted;
        await hold.RollbackAsync(CancellationToken);
        var result = await seeding;

        Assert.True(waited, "The seeder never waited on the advisory lock.");
        Assert.False(finishedWhileWaiting);
        Assert.Equal(0, usersWhileWaiting);
        Assert.Equal(DemoDataSeedOutcome.Seeded, result.Outcome);
    }

    [Fact]
    public async Task ASecondRunWritesNothing()
    {
        await using (var first = Fixture.CreateAppDbContext())
        {
            await CreateSeeder(first, Clock(_designNow), new RecordingLogger()).SeedAsync(CancellationToken);
        }

        var before = await SnapshotAsync();
        await using var second = Fixture.CreateAppDbContext();
        var logger = new RecordingLogger();

        var result = await CreateSeeder(second, Clock(_designNow.AddDays(1)), logger).SeedAsync(CancellationToken);

        Assert.Equal(DemoDataSeedOutcome.DataPresent, result.Outcome);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(DemoDataLog.SkippedEventName, Assert.Single(logger.Entries).EventId.Name);
    }

    [Fact]
    public async Task TwoRunsStartedTogetherSeedOnce()
    {
        await using var first = Fixture.CreateAppDbContext();
        await using var second = Fixture.CreateAppDbContext();

        var results = await Task.WhenAll(
            CreateSeeder(first, Clock(_designNow), new RecordingLogger()).SeedAsync(CancellationToken),
            CreateSeeder(second, Clock(_designNow), new RecordingLogger()).SeedAsync(CancellationToken));

        Assert.Equal(
            [DemoDataSeedOutcome.Seeded, DemoDataSeedOutcome.DataPresent],
            results.Select(result => result.Outcome).Order());
        Assert.Equal(_expectedCounts, await CountRowsAsync());
    }

    [Fact]
    public async Task ASeedAtTenFortyTwoReproducesTheFiguresOfTheDesign()
    {
        await using (var seedContext = Fixture.CreateAppDbContext())
        {
            await CreateSeeder(seedContext, Clock(_designNow), new RecordingLogger()).SeedAsync(CancellationToken);
        }

        await using var db = Fixture.CreateAppDbContext();
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(user => user.Id, CancellationToken);
        var name = (Guid? id) => id is { } value ? users[value].DisplayName : null;
        var idOf = (string displayName) => users.Values.Single(user => user.DisplayName == displayName).Id;
        var jobs = await db.Jobs.AsNoTracking().ToListAsync(CancellationToken);
        var history = await db.JobHistory.AsNoTracking().ToListAsync(CancellationToken);
        var numberOf = jobs.ToDictionary(job => job.Id, job => job.Number);
        var job = (long number) => jobs.Single(entry => entry.Number == number);
        var dayStart = new DateTimeOffset(_designDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var isToday = (DateTimeOffset instant) => instant >= dayStart && instant < dayStart.AddDays(1);
        var at = (int day, int hour, int minute) => dayStart.AddDays(day).AddHours(hour).AddMinutes(minute);
        var open = new[] { JobStatus.Assigned, JobStatus.InProgress, JobStatus.OnHold };

        Assert.Equal(7, users.Count);
        Assert.All(users.Values, user =>
        {
            Assert.True(user.IsActive);
            Assert.Null(user.LastSignedInAt);
            Assert.Equal(at(-30, 9, 0), user.CreatedAt);
            Assert.Equal(user.CreatedAt, user.UpdatedAt);
        });
        var technicians = await db.Technicians.AsNoTracking().ToListAsync(CancellationToken);
        Assert.Equal(["Samuel Osei"], technicians.Where(technician => !technician.IsOnShift).Select(technician => name(technician.UserId)));
        Assert.Equal(1, users.Values.Count(user => user.Role == UserRole.Administrator));

        Assert.Equal(
            "Unassigned 4, Assigned 8, InProgress 2, OnHold 3, Completed 2, Cancelled 1",
            string.Join(", ", Enum.GetValues<JobStatus>().Select(status => $"{status} {jobs.Count(entry => entry.Status == status)}")));
        Assert.Equal(17, jobs.Count(entry => entry.Status is JobStatus.Unassigned or JobStatus.Assigned or JobStatus.InProgress or JobStatus.OnHold));
        Assert.Equal(
            "Created 20, Assigned 16, Started 7, PutOnHold 3, Resumed 0, Completed 2, Cancelled 1",
            string.Join(", ", Enum.GetValues<JobHistoryAction>().Select(action => $"{action} {history.Count(entry => entry.Action == action)}")));
        Assert.Equal(Enumerable.Range(1036, 20).Select(number => (long)number), jobs.Select(entry => entry.Number).Order());

        var ordered = jobs.OrderByDescending(entry => entry.UpdatedAt).ThenByDescending(entry => entry.Id).Select(entry => entry.Number).ToList();
        Assert.Equal([1049, 1044, 1051, 1047, 1043, 1042, 1039, 1045, 1052, 1048, 1053, 1050], ordered.Take(12));
        Assert.Equal([1046, 1054, 1055, 1041, 1038, 1040, 1037, 1036], ordered.Skip(12));

        var activity = history
            .Where(entry => isToday(entry.OccurredAt))
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Action == JobHistoryAction.Created ? 1 : 0)
            .ThenByDescending(entry => entry.Id)
            .Take(6)
            .Select(entry => $"{entry.OccurredAt:HH:mm} {name(entry.ActorUserId)} {entry.Action} JOB-{numberOf[entry.JobId]}{(entry.TechnicianId is { } technician ? $" to {name(technician)}" : string.Empty)}{(entry.Note is { } note ? $" ({note})" : string.Empty)}");
        Assert.Equal(
            [
                "10:42 Priya Natarajan Started JOB-1049",
                "10:31 Tom Becker PutOnHold JOB-1044 (Waiting for replacement gas valve)",
                "10:20 Dana Whitfield Created JOB-1051",
                "10:05 Dana Whitfield Assigned JOB-1047 to Marco Reyes",
                "09:58 Aisha Karim Completed JOB-1043",
                "09:12 Marco Reyes Started JOB-1042",
            ],
            activity);

        var unassigned = jobs.Where(entry => entry.Status == JobStatus.Unassigned).OrderBy(entry => entry.ScheduledStart).ToList();
        Assert.Equal([1052, 1051, 1050, 1054], unassigned.Select(entry => entry.Number));
        Assert.Equal(2, unassigned.Count(entry => isToday(entry.ScheduledStart)));

        var openPerTechnician = technicians
            .OrderBy(technician => name(technician.UserId))
            .Select(technician => $"{name(technician.UserId)} {jobs.Count(entry => entry.AssignedTechnicianId == technician.UserId && open.Contains(entry.Status))}");
        Assert.Equal(
            ["Aisha Karim 3", "Lena Park 1", "Marco Reyes 3", "Priya Natarajan 2", "Samuel Osei 0", "Tom Becker 4"],
            openPerTechnician);
        Assert.Equal(13, jobs.Count(entry => open.Contains(entry.Status)));
        Assert.Equal(5, technicians.Count(technician => technician.IsOnShift));

        var currentOrNext = technicians
            .OrderBy(technician => name(technician.UserId))
            .Select(technician =>
            {
                var own = jobs.Where(entry => entry.AssignedTechnicianId == technician.UserId).ToList();
                var current = own.Where(entry => entry.Status == JobStatus.InProgress).OrderByDescending(entry => entry.UpdatedAt).FirstOrDefault()
                    ?? own.Where(entry => entry.Status == JobStatus.OnHold).OrderByDescending(entry => entry.UpdatedAt).FirstOrDefault();
                var next = own.Where(entry => entry.Status == JobStatus.Assigned).OrderBy(entry => entry.ScheduledStart).ThenBy(entry => entry.Number).FirstOrDefault();
                return current is not null
                    ? $"{name(technician.UserId)} current JOB-{current.Number} {current.Status}"
                    : next is not null
                        ? $"{name(technician.UserId)} next JOB-{next.Number} at {next.ScheduledStart:HH:mm}"
                        : $"{name(technician.UserId)} none";
            });
        Assert.Equal(
            [
                "Aisha Karim next JOB-1055 at 13:00",
                "Lena Park next JOB-1045 at 11:30",
                "Marco Reyes current JOB-1042 InProgress",
                "Priya Natarajan current JOB-1049 InProgress",
                "Samuel Osei none",
                "Tom Becker current JOB-1044 OnHold",
            ],
            currentOrNext);
        Assert.Equal(at(0, 12, 0), job(1049).ScheduledEnd);

        var marco = idOf("Marco Reyes");
        var marcoActive = jobs.Where(entry => entry.AssignedTechnicianId == marco && open.Contains(entry.Status)).OrderBy(entry => entry.ScheduledStart).ThenBy(entry => entry.Number).ToList();
        Assert.Equal([1042, 1047, 1053], marcoActive.Select(entry => entry.Number));
        Assert.Equal([0, 0, 1], marcoActive.Select(entry => (int)(entry.ScheduledStart - dayStart).TotalDays));
        Assert.Equal(
            ["JOB-1036 Cancelled"],
            jobs.Where(entry => entry.AssignedTechnicianId == marco && entry.Status is JobStatus.Completed or JobStatus.Cancelled).Select(entry => $"JOB-{entry.Number} {entry.Status}"));

        var inspections = await db.Inspections.AsNoTracking().Include(inspection => inspection.Answers).ThenInclude(answer => answer.Readings).ToListAsync(CancellationToken);
        var inspectionOf = (long number) => inspections.Single(inspection => inspection.JobId == job(number).Id);
        var summary = (long number) =>
        {
            var inspection = inspectionOf(number);
            var counts = InspectionCompleteness.Of(TestData.ChecklistOf(job(number).ChecklistTemplateId), inspection);
            var readings = inspection.Answers.OrderBy(answer => answer.ChecklistItemId).SelectMany(answer => answer.Readings.OrderBy(reading => reading.ReadingFieldId)).Select(reading => reading.Value.ToString("0.00", CultureInfo.InvariantCulture));
            return $"{counts.Answered} of {counts.Total}, {counts.PassCount}/{counts.FailCount}/{counts.NotApplicableCount}, readings {string.Join(" ", readings)}, {(inspection.IsSubmitted ? "submitted" : "draft")}";
        };
        Assert.Equal("6 of 10, 4/1/1, readings 118.00 285.00, draft", summary(1042));
        Assert.Equal("8 of 8, 7/0/1, readings 18.00, submitted", summary(1039));
        Assert.Equal("8 of 8, 7/0/1, readings 277.00 250.00 0.40, submitted", summary(1043));
        Assert.Equal((at(0, 9, 31), at(0, 9, 50)), (inspectionOf(1042).CreatedAt, inspectionOf(1042).UpdatedAt));
        Assert.Equal((at(0, 7, 48), at(0, 8, 52), at(0, 8, 52)), (inspectionOf(1039).CreatedAt, inspectionOf(1039).UpdatedAt, inspectionOf(1039).SubmittedAt));
        Assert.Equal(job(1039).CompletedAt, inspectionOf(1039).SubmittedAt);
        Assert.Equal(
            "Pressure drop traced to a weeping fitting on the return line. Fitting tightened, system refilled to 18 psi and holding.",
            inspectionOf(1039).TechnicianNotes);
        Assert.Equal(
            "Bearing noise at start-up. Recommend motor replacement within 30 days.",
            inspectionOf(1042).Answers.Single(answer => answer.Result == InspectionResult.Fail).Note);

        Assert.Equal("(555) 014-4417", job(1047).ContactPhone);
        Assert.Null(job(1047).ContactName);
        Assert.Equal(marco, job(1047).AssignedTechnicianId);
        Assert.Equal(at(0, 10, 5), job(1047).AssignedAt);
        var assigned1047 = history.Single(entry => entry.JobId == job(1047).Id && entry.Action == JobHistoryAction.Assigned);
        Assert.Equal("Dana Whitfield", name(assigned1047.ActorUserId));
        Assert.Equal(
            ["08:14 Created", "08:20 Assigned", "09:12 Started"],
            history.Where(entry => entry.JobId == job(1042).Id).OrderBy(entry => entry.OccurredAt).Select(entry => $"{entry.OccurredAt:HH:mm} {entry.Action}"));
        Assert.Equal(at(0, 9, 50), job(1042).UpdatedAt);
        Assert.Equal("Nina Alvarez", job(1042).ContactName);

        var assignedJobs = jobs.Where(entry => entry.Status == JobStatus.Assigned).OrderBy(entry => entry.ScheduledStart).ToList();
        Assert.Equal((1045L, at(0, 11, 30)), (assignedJobs[0].Number, assignedJobs[0].ScheduledStart));
        Assert.Equal(2, jobs.Where(entry => entry.Status == JobStatus.InProgress).Select(entry => entry.AssignedTechnicianId).Distinct().Count());
        var onHoldSince = jobs
            .Where(entry => entry.Status == JobStatus.OnHold)
            .Min(entry => history.Where(item => item.JobId == entry.Id && item.Action == JobHistoryAction.PutOnHold).Max(item => item.OccurredAt));
        Assert.Equal(at(-3, 13, 50), onHoldSince);
        var completedToday = jobs.Where(entry => entry.CompletedAt is { } completed && isToday(completed)).ToList();
        Assert.Equal(2, completedToday.Count);
        Assert.Equal(at(0, 9, 58), completedToday.Max(entry => entry.CompletedAt));
        Assert.Equal(0, jobs.Count(entry => entry.CancelledAt is { } cancelled && isToday(cancelled)));
        Assert.Equal(at(-3, 9, 20), job(1036).CancelledAt);
        Assert.Equal(
            "Building management replaced the filters under its own maintenance contract.",
            history.Single(entry => entry.Action == JobHistoryAction.Cancelled).Note);
        Assert.Equal(TimeSpan.FromMinutes(107), job(1039).CompletedAt - job(1039).StartedAt);
        Assert.Null(job(1039).ContactName);
        Assert.Null(job(1039).ContactPhone);
    }

    [Fact]
    public async Task ASeedOnTheSpringChangeDayInNewYorkKeepsEveryTimelineInOrder()
    {
        var clock = Clock(new DateTimeOffset(2027, 3, 14, 9, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        await using var db = Fixture.CreateAppDbContext();

        var result = await CreateSeeder(db, clock, new RecordingLogger()).SeedAsync(CancellationToken);

        Assert.Equal(DemoDataSeedOutcome.Seeded, result.Outcome);
        await using var check = Fixture.CreateAppDbContext();
        var jobs = await check.Jobs.AsNoTracking().ToDictionaryAsync(job => job.Number, CancellationToken);
        var history = await check.JobHistory.AsNoTracking().ToListAsync(CancellationToken);
        var job1042 = jobs[1042];
        Assert.Equal(
            ["06:14 Created", "06:20 Assigned", "07:12 Started"],
            history.Where(entry => entry.JobId == job1042.Id).OrderBy(entry => entry.OccurredAt).Select(entry => $"{entry.OccurredAt:HH:mm} {entry.Action}"));
        Assert.Equal(new DateTimeOffset(2027, 3, 14, 7, 50, 0, TimeSpan.Zero), job1042.UpdatedAt);
        foreach (var demoJob in DemoData.Jobs)
        {
            var actions = history
                .Where(entry => entry.JobId == jobs[demoJob.Number].Id)
                .OrderBy(entry => entry.OccurredAt)
                .Select(entry => entry.Action.ToString());
            var expected = demoJob.Steps.Where(step => step is not DemoStep.InspectionSaved).Select(step => step.Name);
            Assert.Equal(expected, actions);
        }
    }

    [Fact]
    public async Task TheApiSeedsAtStartupInDevelopmentAndLogsOnlyRowCounts()
    {
        var factory = Fixture.CreateFactory(factory =>
        {
            factory.EnvironmentName = Development;
            factory.Settings["Logs:FileEnabled"] = "false";
            factory.Settings["Seed:DemoData"] = "true";
            factory.Settings["Seed:DemoPassword"] = _password;
        });
        try
        {
            _ = factory.Services;

            Assert.Equal(_expectedCounts, await CountRowsAsync());
            var completed = Assert.Single(factory.LogSink.Events, logEvent =>
                logEvent.Properties.TryGetValue("EventId", out var eventId)
                && eventId.ToString().Contains(DemoDataLog.CompletedEventName, StringComparison.Ordinal));
            Assert.Equal(LogEventLevel.Information, completed.Level);
            foreach (var logEvent in factory.LogSink.Events)
            {
                AssertNoPersonalData(logEvent.RenderMessage(CultureInfo.InvariantCulture));
                AssertNoPersonalData(string.Join(" ", logEvent.Properties.Values));
                AssertNoPersonalData(logEvent.Exception?.ToString() ?? string.Empty);
            }
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    private static FakeTimeProvider Clock(DateTimeOffset utcNow) => new(utcNow);

    private static void AssertNoPersonalData(string text)
    {
        foreach (var user in DemoData.Users)
        {
            Assert.DoesNotContain(user.DisplayName, text, StringComparison.Ordinal);
            Assert.DoesNotContain(user.Email, text, StringComparison.OrdinalIgnoreCase);
            if (user.PhoneNumber is { } phone)
            {
                Assert.DoesNotContain(phone, text, StringComparison.Ordinal);
            }
        }

        foreach (var demoJob in DemoData.Jobs)
        {
            Assert.DoesNotContain(demoJob.SiteAddress, text, StringComparison.Ordinal);
            Assert.DoesNotContain(demoJob.CustomerName, text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("(555)", text, StringComparison.Ordinal);
    }

    private DemoDataSeeder CreateSeeder(
        FieldServiceDbContext db,
        TimeProvider time,
        RecordingLogger logger,
        string environmentName = Development,
        bool demoData = true,
        string? password = null)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        var options = Options.Create(new SeedOptions
        {
            DemoData = demoData,
            DemoPassword = password ?? (environmentName == Development && demoData ? _password : null),
        });
        return new DemoDataSeeder(db, options, environment, time, _hasher, logger);
    }

    private async Task<DemoDataCounts> CountRowsAsync()
    {
        return new DemoDataCounts(
            (int)await ScalarAsync<long>("SELECT count(*) FROM fieldservice.users"),
            (int)await ScalarAsync<long>("SELECT count(*) FROM fieldservice.technicians"),
            (int)await ScalarAsync<long>("SELECT count(*) FROM fieldservice.jobs"),
            (int)await ScalarAsync<long>("SELECT count(*) FROM fieldservice.job_history"),
            (int)await ScalarAsync<long>("SELECT count(*) FROM fieldservice.inspections"),
            (int)await ScalarAsync<long>("SELECT count(*) FROM fieldservice.inspection_answers"),
            (int)await ScalarAsync<long>("SELECT count(*) FROM fieldservice.inspection_readings"));
    }

    private async Task<string> SnapshotAsync()
    {
        var counts = await CountRowsAsync();
        var rows = await ScalarAsync<string>(
            """
            SELECT string_agg(row, ';' ORDER BY row) FROM (
                SELECT 'u' || id || xmin::text AS row FROM fieldservice.users
                UNION ALL SELECT 'j' || id || xmin::text FROM fieldservice.jobs
                UNION ALL SELECT 'h' || id || xmin::text FROM fieldservice.job_history
                UNION ALL SELECT 'i' || id || xmin::text FROM fieldservice.inspections) AS rows
            """);
        await using var migrator = await Fixture.OpenMigratorConnectionAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT last_value || ':' || is_called FROM fieldservice.jobs_number_seq", migrator);
        var sequence = (string)(await command.ExecuteScalarAsync(CancellationToken))!;
        return $"{counts} {sequence} {rows}";
    }

    private async Task RestartJobNumbersAsync()
    {
        await using var migrator = await Fixture.OpenMigratorConnectionAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT setval('fieldservice.jobs_number_seq', 1001, false)", migrator);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<bool> WaitForLockWaitAsync()
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var waiting = await ScalarAsync<long>(
                $"SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND objid = {DemoDataSeeder.AdvisoryLockKey}");
            if (waiting > 0)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken);
        }

        return false;
    }

    private async Task<T> ScalarAsync<T>(string statement)
    {
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        await using var command = new NpgsqlCommand(statement, connection);
        return (T)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private sealed class RecordingLogger : ILogger<DemoDataSeeder>
    {
        private readonly Lock _gate = new();

        private readonly List<(LogLevel Level, EventId EventId, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, EventId EventId, string Message)> Entries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
            {
                _entries.Add((logLevel, eventId, formatter(state, exception)));
            }
        }
    }
}
