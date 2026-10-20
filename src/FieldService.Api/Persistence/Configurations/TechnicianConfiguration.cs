using FieldService.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldService.Api.Persistence.Configurations;

public sealed class TechnicianConfiguration : IEntityTypeConfiguration<Technician>
{
    public void Configure(EntityTypeBuilder<Technician> builder)
    {
        builder.ToTable("technicians", table =>
        {
            table.HasCheckConstraint("ck_technicians_phone_number_not_blank", "btrim(phone_number) <> ''");
            table.HasCheckConstraint("ck_technicians_trade", EnumCheck.In<Trade>("trade"));
        });

        builder.HasKey(technician => technician.UserId);
        builder.Property(technician => technician.PhoneNumber).HasMaxLength(32);
        builder.HasOne(technician => technician.User)
            .WithOne(user => user.Technician)
            .HasForeignKey<Technician>(technician => technician.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
