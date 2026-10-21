using System.Globalization;
using FieldService.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace FieldService.Api.Persistence.Seeding;

public sealed class DemoDataSeeder(
    FieldServiceDbContext db,
    IOptions<SeedOptions> options,
    IHostEnvironment environment,
    TimeProvider time,
    IPasswordHasher<User> passwordHasher,
    ILogger<DemoDataSeeder> logger)
{
    public const long AdvisoryLockKey = 20261019;

    public static async Task<DemoDataSeedResult> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(cancellationToken);
    }

    public async Task<DemoDataSeedResult> SeedAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.DemoData)
        {
            return Skip(DemoDataSeedOutcome.Disabled, "Seed:DemoData is false");
        }

        if (environment.IsProduction())
        {
            return Skip(DemoDataSeedOutcome.Production, "the environment is Production");
        }

        if (settings.DemoPassword is not { Length: >= SeedOptions.MinimumDemoPasswordLength } password)
        {
            return Skip(
                DemoDataSeedOutcome.PasswordTooShort,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Seed:DemoPassword has fewer than {SeedOptions.MinimumDemoPasswordLength} characters"));
        }

        if (await db.Users.AnyAsync(cancellationToken))
        {
            return Skip(DemoDataSeedOutcome.DataPresent, "demo data present");
        }

        var data = DemoDataSet.Replay(
            DemoTimeAnchor.From(time),
            user => passwordHasher.HashPassword(user, password));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", cancellationToken);
        if (await db.Users.AnyAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Skip(DemoDataSeedOutcome.DataPresent, "demo data present");
        }

        data.AddTo(db);
        await db.SaveChangesAsync(cancellationToken);
        var nextJobNumber = await AdvanceJobNumberAsync(transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var counts = data.Counts;
        DemoDataLog.Completed(
            logger,
            counts.Users,
            counts.Technicians,
            counts.Jobs,
            counts.HistoryEntries,
            counts.Inspections,
            counts.Answers,
            counts.Readings,
            nextJobNumber);
        return new DemoDataSeedResult(DemoDataSeedOutcome.Seeded, counts, nextJobNumber);
    }

    private async Task<long> AdvanceJobNumberAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT fieldservice.advance_job_number()";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private DemoDataSeedResult Skip(DemoDataSeedOutcome outcome, string reason)
    {
        DemoDataLog.Skipped(logger, reason);
        return DemoDataSeedResult.Skipped(outcome);
    }
}
