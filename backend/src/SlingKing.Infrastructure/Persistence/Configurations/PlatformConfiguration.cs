using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SlingKing.Domain.Platform;

namespace SlingKing.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.Email).IsUnique().HasDatabaseName("ux_users_email");
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_hash");
        b.HasOne(x => x.User).WithMany(u => u.RefreshTokens)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> b)
    {
        b.ToTable("number_sequences");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(32);
        b.Property(x => x.Prefix).HasMaxLength(8).IsRequired();
        b.Property(x => x.NextValue).IsRequired();
        b.Property(x => x.Padding).IsRequired();
    }
}

public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.ToTable("app_settings");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(64);
        b.Property(x => x.Value).IsRequired();
    }
}
