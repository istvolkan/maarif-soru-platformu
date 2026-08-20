using MaarifPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaarifPlatform.Infrastructure.Persistence.Configurations;

public class AiRunConfiguration : IEntityTypeConfiguration<AiRun>
{
    public void Configure(EntityTypeBuilder<AiRun> b)
    {
        b.ToTable("ai_runs");
        b.HasKey(e => e.Id);
        b.Property(e => e.Stage).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.ModelTier).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Provider).HasMaxLength(50).IsRequired();
        b.Property(e => e.Model).HasMaxLength(100).IsRequired();
        b.Property(e => e.PromptVersion).HasMaxLength(50);
        b.Property(e => e.CostUsd).HasPrecision(12, 6);
        // §M Cost Dashboard: kitap/model/aşama bazlı agregasyonlar bu indekslere dayanır.
        b.HasIndex(e => e.Stage);
        b.HasIndex(e => e.Provider);

        b.HasOne(e => e.Question)
            .WithMany()
            .HasForeignKey(e => e.QuestionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class PromptTemplateConfiguration : IEntityTypeConfiguration<PromptTemplate>
{
    public void Configure(EntityTypeBuilder<PromptTemplate> b)
    {
        b.ToTable("prompt_templates");
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).HasMaxLength(200).IsRequired();
        b.Property(e => e.Stage).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.Content).IsRequired();
        b.HasIndex(e => new { e.Name, e.Version }).IsUnique();
    }
}

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> b)
    {
        b.ToTable("audit_log");
        b.HasKey(e => e.Id);
        b.Property(e => e.ActorType).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.ActorId).HasMaxLength(200);
        b.Property(e => e.Action).HasMaxLength(100).IsRequired();
        b.Property(e => e.EntityName).HasMaxLength(100).IsRequired();
        b.Property(e => e.EntityId).HasMaxLength(100).IsRequired();
        b.Property(e => e.BeforeJson).HasColumnType("jsonb");
        b.Property(e => e.AfterJson).HasColumnType("jsonb");
        b.HasIndex(e => new { e.EntityName, e.EntityId });
        // §N: append-only — güncelleme/silme uygulama katmanında engellenir; burada sadece indeks.
    }
}
