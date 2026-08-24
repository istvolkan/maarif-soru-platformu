using MaarifPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaarifPlatform.Infrastructure.Persistence.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("users");
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).HasMaxLength(200).IsRequired();
        b.Property(e => e.Email).HasMaxLength(320).IsRequired();
        b.HasIndex(e => e.Email).IsUnique();
        b.Property(e => e.Role).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.PasswordHash).HasMaxLength(500).IsRequired();
    }
}

public class MaarifStandardVersionConfiguration : IEntityTypeConfiguration<MaarifStandardVersion>
{
    public void Configure(EntityTypeBuilder<MaarifStandardVersion> b)
    {
        b.ToTable("maarif_standard_versions");
        b.HasKey(e => e.Id);
        b.Property(e => e.Code).HasMaxLength(100).IsRequired();
        b.HasIndex(e => e.Code).IsUnique();
    }
}
