using FieldService.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldService.Api.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", table =>
        {
            table.HasCheckConstraint("ck_users_display_name_not_blank", "btrim(display_name) <> ''");
            table.HasCheckConstraint("ck_users_email_format", "email LIKE '_%@_%'");
            table.HasCheckConstraint("ck_users_email_lower_case", "email = lower(email)");
            table.HasCheckConstraint("ck_users_role", EnumCheck.In<UserRole>("role"));
            table.HasCheckConstraint("ck_users_updated_at", "updated_at >= created_at");
        });

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.Email).HasMaxLength(256);
        builder.Property(user => user.DisplayName).HasMaxLength(100);
        builder.HasIndex(user => user.Email).IsUnique();
    }
}
