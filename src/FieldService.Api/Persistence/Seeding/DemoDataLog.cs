namespace FieldService.Api.Persistence.Seeding;

public static partial class DemoDataLog
{
    public const string SkippedEventName = "seed.demo-data.skipped";

    public const string CompletedEventName = "seed.demo-data.completed";

    [LoggerMessage(
        EventId = 3001,
        EventName = SkippedEventName,
        Level = LogLevel.Information,
        Message = "Demo data seeding skipped: {Reason}")]
    public static partial void Skipped(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 3002,
        EventName = CompletedEventName,
        Level = LogLevel.Information,
        Message = "Demo data seeded: {Users} users, {Technicians} technicians, {Jobs} jobs, {HistoryEntries} history entries, {Inspections} inspections, {Answers} answers and {Readings} readings; the next job number is {NextJobNumber}")]
    public static partial void Completed(
        ILogger logger,
        int users,
        int technicians,
        int jobs,
        int historyEntries,
        int inspections,
        int answers,
        int readings,
        long nextJobNumber);
}
