using FieldService.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldService.Api.Persistence.Configurations;

public sealed class InspectionReadingConfiguration : IEntityTypeConfiguration<InspectionReading>
{
    public void Configure(EntityTypeBuilder<InspectionReading> builder)
    {
        builder.ToTable("inspection_readings");

        builder.HasKey(reading => new { reading.InspectionId, reading.ChecklistItemId, reading.ReadingFieldId });
        builder.Property(reading => reading.Value).HasPrecision(10, 2);
        builder.HasOne<ChecklistReadingField>()
            .WithMany()
            .HasForeignKey(reading => new { reading.ChecklistItemId, reading.ReadingFieldId })
            .HasPrincipalKey(field => new { field.ChecklistItemId, field.Id })
            .HasConstraintName("fk_inspection_readings_checklist_reading_fields")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
