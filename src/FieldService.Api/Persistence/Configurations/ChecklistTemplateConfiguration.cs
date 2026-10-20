using FieldService.Api.Domain;
using FieldService.Api.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldService.Api.Persistence.Configurations;

public sealed class ChecklistTemplateConfiguration : IEntityTypeConfiguration<ChecklistTemplate>
{
    public void Configure(EntityTypeBuilder<ChecklistTemplate> builder)
    {
        builder.ToTable("checklist_templates", table =>
            table.HasCheckConstraint("ck_checklist_templates_trade", EnumCheck.In<Trade>("trade")));

        builder.HasKey(template => template.Id);
        builder.Property(template => template.Id).ValueGeneratedNever();
        builder.Property(template => template.Name).HasMaxLength(120);
        builder.HasIndex(template => template.Name).IsUnique();
        builder.HasMany(template => template.Items)
            .WithOne()
            .HasForeignKey(item => item.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(ChecklistTemplateSeed.Templates);
    }
}
