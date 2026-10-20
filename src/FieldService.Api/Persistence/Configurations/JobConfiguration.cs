using FieldService.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldService.Api.Persistence.Configurations;

public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs", table =>
        {
            table.HasCheckConstraint("ck_jobs_assigned_at", "(assigned_technician_id IS NULL) = (assigned_at IS NULL)");
            table.HasCheckConstraint(
                "ck_jobs_assignee",
                "(status <> 'Unassigned' OR assigned_technician_id IS NULL) AND (status NOT IN ('Assigned', 'InProgress', 'OnHold', 'Completed') OR assigned_technician_id IS NOT NULL)");
            table.HasCheckConstraint("ck_jobs_cancelled_at", "(status = 'Cancelled') = (cancelled_at IS NOT NULL)");
            table.HasCheckConstraint("ck_jobs_completed_at", "(status = 'Completed') = (completed_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_jobs_optional_text_not_blank",
                "(site_access_notes IS NULL OR btrim(site_access_notes) <> '') AND (contact_name IS NULL OR btrim(contact_name) <> '') AND (contact_phone IS NULL OR btrim(contact_phone) <> '') AND (equipment IS NULL OR btrim(equipment) <> '')");
            table.HasCheckConstraint("ck_jobs_priority", EnumCheck.In<JobPriority>("priority"));
            table.HasCheckConstraint(
                "ck_jobs_required_text_not_blank",
                "btrim(title) <> '' AND btrim(description) <> '' AND btrim(customer_name) <> '' AND btrim(site_address) <> ''");
            table.HasCheckConstraint("ck_jobs_schedule", "scheduled_end > scheduled_start");
            table.HasCheckConstraint(
                "ck_jobs_started_at",
                "CASE WHEN status IN ('InProgress', 'OnHold', 'Completed') THEN started_at IS NOT NULL WHEN status IN ('Unassigned', 'Assigned') THEN started_at IS NULL ELSE true END");
            table.HasCheckConstraint("ck_jobs_status", EnumCheck.In<JobStatus>("status"));
            table.HasCheckConstraint(
                "ck_jobs_timestamps",
                "updated_at >= created_at AND (completed_at IS NULL OR completed_at >= started_at)");
        });

        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedNever();
        builder.Property(job => job.Number).UseIdentityByDefaultColumn().HasIdentityOptions(startValue: 1001);
        builder.HasIndex(job => job.Number).IsUnique();
        builder.Property(job => job.Title).HasMaxLength(120);
        builder.Property(job => job.Description).HasMaxLength(2000);
        builder.Property(job => job.CustomerName).HasMaxLength(120);
        builder.Property(job => job.SiteAddress).HasMaxLength(300);
        builder.Property(job => job.SiteAccessNotes).HasMaxLength(500);
        builder.Property(job => job.ContactName).HasMaxLength(100);
        builder.Property(job => job.ContactPhone).HasMaxLength(32);
        builder.Property(job => job.Equipment).HasMaxLength(200);
        builder.Property(job => job.Version).IsRowVersion().HasColumnName("xmin");

        builder.HasOne<ChecklistTemplate>()
            .WithMany()
            .HasForeignKey(job => job.ChecklistTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Technician>()
            .WithMany()
            .HasForeignKey(job => job.AssignedTechnicianId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(job => job.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(job => job.History)
            .WithOne()
            .HasForeignKey(entry => entry.JobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(job => new { job.UpdatedAt, job.Id }).IsDescending(true, true);
        builder.HasIndex(job => new { job.Status, job.UpdatedAt, job.Id }).IsDescending(false, true, true);
        builder.HasIndex(job => new { job.AssignedTechnicianId, job.Status });
        builder.HasIndex(job => job.ScheduledStart);
    }
}
