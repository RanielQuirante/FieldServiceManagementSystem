using FieldService.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldService.Api.Persistence.Configurations;

public sealed class JobHistoryEntryConfiguration : IEntityTypeConfiguration<JobHistoryEntry>
{
    public void Configure(EntityTypeBuilder<JobHistoryEntry> builder)
    {
        builder.ToTable("job_history", table =>
        {
            table.HasCheckConstraint("ck_job_history_action", EnumCheck.In<JobHistoryAction>("action"));
            table.HasCheckConstraint("ck_job_history_from_status", EnumCheck.In<JobStatus>("from_status"));
            table.HasCheckConstraint(
                "ck_job_history_from_status_presence",
                "(action = 'Created') = (from_status IS NULL)");
            table.HasCheckConstraint(
                "ck_job_history_note",
                "CASE WHEN action IN ('PutOnHold', 'Cancelled') THEN note IS NOT NULL AND btrim(note) <> '' ELSE note IS NULL END");
            table.HasCheckConstraint("ck_job_history_technician", "(action = 'Assigned') = (technician_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_job_history_to_status",
                "to_status = CASE action WHEN 'Created' THEN 'Unassigned' WHEN 'Assigned' THEN 'Assigned' WHEN 'Started' THEN 'InProgress' WHEN 'PutOnHold' THEN 'OnHold' WHEN 'Resumed' THEN 'InProgress' WHEN 'Completed' THEN 'Completed' WHEN 'Cancelled' THEN 'Cancelled' END");
        });

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Note).HasMaxLength(500);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(entry => entry.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Technician>()
            .WithMany()
            .HasForeignKey(entry => entry.TechnicianId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entry => new { entry.JobId, entry.OccurredAt });
        builder.HasIndex(entry => entry.OccurredAt).IsDescending();
    }
}
