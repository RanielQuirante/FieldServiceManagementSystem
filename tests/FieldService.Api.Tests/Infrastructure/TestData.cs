using System.Security.Cryptography;
using FieldService.Api.Domain;
using FieldService.Api.Persistence.Seeding;

namespace FieldService.Api.Tests.Infrastructure;

public sealed class TestData(DatabaseFixture fixture, DateTimeOffset now)
{
    public DateTimeOffset Now { get; } = now;

    public async Task<User> CreateAdministratorAsync(CancellationToken cancellationToken)
    {
        var user = NewUser(UserRole.Administrator, "Test Administrator");
        await using var context = fixture.CreateAppDbContext();
        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<Technician> CreateTechnicianAsync(Trade trade, CancellationToken cancellationToken)
    {
        var user = NewUser(UserRole.Technician, "Test Technician");
        var technician = Technician.Create(user, trade, $"+1 555 01{RandomNumberGenerator.GetInt32(100):D2}", isOnShift: true);
        await using var context = fixture.CreateAppDbContext();
        context.Users.Add(user);
        context.Technicians.Add(technician);
        await context.SaveChangesAsync(cancellationToken);
        return technician;
    }

    public async Task<Job> CreateJobAsync(
        Guid createdByUserId,
        Guid checklistTemplateId,
        CancellationToken cancellationToken,
        long? number = null)
    {
        var marker = Marker();
        var job = Job.Create(
            new JobDetails(
                $"Test job {marker}",
                "Created by the API tests.",
                JobPriority.Normal,
                $"Test Customer {marker}",
                $"Test Site {marker}",
                SiteAccessNotes: null,
                ContactName: null,
                ContactPhone: null,
                Equipment: null,
                Now.AddDays(1),
                Now.AddDays(1).AddHours(2),
                checklistTemplateId),
            createdByUserId,
            Now);
        await using var context = fixture.CreateAppDbContext();
        context.Jobs.Add(job);
        if (number is { } explicitNumber)
        {
            context.Entry(job).Property(entry => entry.Number).CurrentValue = explicitNumber;
        }

        await context.SaveChangesAsync(cancellationToken);
        return job;
    }

    public async Task<Inspection> CreateInspectionAsync(
        Job job,
        Technician technician,
        bool submitted,
        CancellationToken cancellationToken)
    {
        var inspection = Inspection.Create(job.Id, technician.UserId, Now);
        await using var context = fixture.CreateAppDbContext();
        context.Inspections.Add(inspection);
        if (submitted)
        {
            context.Entry(inspection).Property(entry => entry.SubmittedAt).CurrentValue = Now;
        }

        foreach (var item in ChecklistTemplateSeed.Items.Where(item => item.TemplateId == job.ChecklistTemplateId))
        {
            context.Add(InspectionAnswer.Create(inspection.Id, item.Id, InspectionResult.Pass, note: null, Now));
            foreach (var field in ChecklistTemplateSeed.ReadingFields.Where(field => field.ChecklistItemId == item.Id))
            {
                context.Add(InspectionReading.Create(inspection.Id, item.Id, field.Id, 12.5m));
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return inspection;
    }

    private User NewUser(UserRole role, string displayName)
    {
        return User.Create(
            $"user-{Guid.NewGuid():N}@fieldservice.example",
            $"{displayName} {Marker()}",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            role,
            Now);
    }

    private static string Marker()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(2));
    }
}
