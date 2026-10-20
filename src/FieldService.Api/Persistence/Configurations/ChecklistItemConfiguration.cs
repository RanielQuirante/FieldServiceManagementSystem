using FieldService.Api.Domain;
using FieldService.Api.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldService.Api.Persistence.Configurations;

public sealed class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    public void Configure(EntityTypeBuilder<ChecklistItem> builder)
    {
        builder.ToTable("checklist_items", table =>
            table.HasCheckConstraint("ck_checklist_items_position", "position BETWEEN 1 AND 99"));

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Prompt).HasMaxLength(200);
        builder.HasIndex(item => new { item.TemplateId, item.Position }).IsUnique();
        builder.HasMany(item => item.ReadingFields)
            .WithOne()
            .HasForeignKey(field => field.ChecklistItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(ChecklistTemplateSeed.Items);
    }
}
