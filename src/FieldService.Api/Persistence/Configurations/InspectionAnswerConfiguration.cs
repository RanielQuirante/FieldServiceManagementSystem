using FieldService.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldService.Api.Persistence.Configurations;

public sealed class InspectionAnswerConfiguration : IEntityTypeConfiguration<InspectionAnswer>
{
    public void Configure(EntityTypeBuilder<InspectionAnswer> builder)
    {
        builder.ToTable("inspection_answers", table =>
        {
            table.HasCheckConstraint(
                "ck_inspection_answers_note",
                "(note IS NULL OR btrim(note) <> '') AND (result <> 'Fail' OR note IS NOT NULL)");
            table.HasCheckConstraint("ck_inspection_answers_result", EnumCheck.In<InspectionResult>("result"));
        });

        builder.HasKey(answer => new { answer.InspectionId, answer.ChecklistItemId });
        builder.Property(answer => answer.Note).HasMaxLength(1000);
        builder.HasOne<ChecklistItem>()
            .WithMany()
            .HasForeignKey(answer => answer.ChecklistItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(answer => answer.Readings)
            .WithOne()
            .HasForeignKey(reading => new { reading.InspectionId, reading.ChecklistItemId })
            .HasConstraintName("fk_inspection_readings_inspection_answers")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
