using FieldService.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldService.Api.Persistence.Configurations;

public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions", table =>
        {
            table.HasCheckConstraint("ck_user_sessions_client", EnumCheck.In<SessionClient>("client"));
            table.HasCheckConstraint(
                "ck_user_sessions_expiry",
                "idle_expires_at > created_at AND absolute_expires_at > created_at AND idle_expires_at <= absolute_expires_at");
            table.HasCheckConstraint(
                "ck_user_sessions_last_refreshed_at",
                "last_refreshed_at IS NULL OR last_refreshed_at >= created_at");
            table.HasCheckConstraint("ck_user_sessions_revocation", "(revoked_at IS NULL) = (revoked_reason IS NULL)");
            table.HasCheckConstraint(
                "ck_user_sessions_revoked_reason",
                EnumCheck.In<SessionRevocationReason>("revoked_reason"));
        });

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(session => session.RefreshTokens)
            .WithOne()
            .HasForeignKey(token => token.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
