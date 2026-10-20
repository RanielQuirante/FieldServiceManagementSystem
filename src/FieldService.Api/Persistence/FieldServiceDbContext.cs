using FieldService.Api.Domain;
using FieldService.Api.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Persistence;

public sealed class FieldServiceDbContext(DbContextOptions<FieldServiceDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Technician> Technicians => Set<Technician>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<ChecklistTemplate> ChecklistTemplates => Set<ChecklistTemplate>();

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<JobHistoryEntry> JobHistory => Set<JobHistoryEntry>();

    public DbSet<Inspection> Inspections => Set<Inspection>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<UserRole>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<Trade>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<SessionClient>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<SessionRevocationReason>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<JobPriority>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<JobStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<JobHistoryAction>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<InspectionResult>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(PersistenceRegistration.Schema);
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new TechnicianConfiguration());
        modelBuilder.ApplyConfiguration(new UserSessionConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new ChecklistTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new ChecklistItemConfiguration());
        modelBuilder.ApplyConfiguration(new ChecklistReadingFieldConfiguration());
        modelBuilder.ApplyConfiguration(new JobConfiguration());
        modelBuilder.ApplyConfiguration(new JobHistoryEntryConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionAnswerConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionReadingConfiguration());
    }
}
