using FieldService.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldService.Api.Persistence.Configurations;

public sealed class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    public void Configure(EntityTypeBuilder<Inspection> builder)
    {
        builder.ToTable("inspections", table =>
        {
            table.HasCheckConstraint(
                "ck_inspections_technician_notes",
                "technician_notes IS NULL OR (submitted_at IS NOT NULL AND btrim(technician_notes) <> '')");
            table.HasCheckConstraint(
                "ck_inspections_timestamps",
                "updated_at >= created_at AND (submitted_at IS NULL OR submitted_at >= created_at)");
        });

        builder.HasKey(inspection => inspection.Id);
        builder.Property(inspection => inspection.Id).ValueGeneratedNever();
        builder.Property(inspection => inspection.TechnicianNotes).HasMaxLength(2000);
        builder.HasOne<Job>()
            .WithOne()
            .HasForeignKey<Inspection>(inspection => inspection.JobId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Technician>()
            .WithMany()
            .HasForeignKey(inspection => inspection.TechnicianId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(inspection => inspection.Answers)
            .WithOne()
            .HasForeignKey(answer => answer.InspectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
