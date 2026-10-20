using FieldService.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldService.Api.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", table =>
        {
            table.HasCheckConstraint(
                "ck_refresh_tokens_replacement",
                "replaced_by_id IS NULL OR (consumed_at IS NOT NULL AND replaced_by_id <> id)");
            table.HasCheckConstraint("ck_refresh_tokens_token_hash_length", "octet_length(token_hash) = 32");
        });

        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasOne<RefreshToken>()
            .WithMany()
            .HasForeignKey(token => token.ReplacedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
