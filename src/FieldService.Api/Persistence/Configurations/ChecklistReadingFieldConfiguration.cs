using FieldService.Api.Domain;
using FieldService.Api.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldService.Api.Persistence.Configurations;

public sealed class ChecklistReadingFieldConfiguration : IEntityTypeConfiguration<ChecklistReadingField>
{
    public void Configure(EntityTypeBuilder<ChecklistReadingField> builder)
    {
        builder.ToTable("checklist_reading_fields", table =>
            table.HasCheckConstraint("ck_checklist_reading_fields_position", "position BETWEEN 1 AND 9"));

        builder.HasKey(field => field.Id);
        builder.Property(field => field.Id).ValueGeneratedNever();
        builder.HasAlternateKey(field => new { field.ChecklistItemId, field.Id });
        builder.Property(field => field.Label).HasMaxLength(60);
        builder.Property(field => field.Unit).HasMaxLength(16);
        builder.HasIndex(field => new { field.ChecklistItemId, field.Position }).IsUnique();

        builder.HasData(ChecklistTemplateSeed.ReadingFields);
    }
}
