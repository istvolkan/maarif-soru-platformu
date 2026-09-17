using MaarifPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaarifPlatform.Infrastructure.Persistence.Configurations;

public class ThemeConfiguration : IEntityTypeConfiguration<Theme>
{
    public void Configure(EntityTypeBuilder<Theme> b)
    {
        b.ToTable("themes");
        b.HasKey(e => e.Id);
        b.Property(e => e.Subject).HasMaxLength(100).IsRequired();
        b.Property(e => e.Name).HasMaxLength(300).IsRequired();
        b.Property(e => e.ApprovalStatus).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(e => new { e.Grade, e.Subject, e.ApprovalStatus });

        b.HasOne(e => e.MaarifStandardVersion)
            .WithMany()
            .HasForeignKey(e => e.MaarifStandardVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(e => e.SourceDocument)
            .WithMany()
            .HasForeignKey(e => e.SourceDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ContentFrameworkConfiguration : IEntityTypeConfiguration<ContentFramework>
{
    public void Configure(EntityTypeBuilder<ContentFramework> b)
    {
        b.ToTable("content_frameworks");
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).HasMaxLength(300).IsRequired();
        b.Property(e => e.ApprovalStatus).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(e => new { e.LearningOutcomeId, e.ApprovalStatus });

        b.HasOne(e => e.LearningOutcome)
            .WithMany(lo => lo.ContentFrameworks)
            .HasForeignKey(e => e.LearningOutcomeId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(e => e.SourceDocument)
            .WithMany()
            .HasForeignKey(e => e.SourceDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProcessComponentConfiguration : IEntityTypeConfiguration<ProcessComponent>
{
    public void Configure(EntityTypeBuilder<ProcessComponent> b)
    {
        b.ToTable("process_components");
        b.HasKey(e => e.Id);
        b.Property(e => e.Description).IsRequired();
        b.Property(e => e.ApprovalStatus).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(e => new { e.LearningOutcomeId, e.ApprovalStatus });

        b.HasOne(e => e.LearningOutcome)
            .WithMany(lo => lo.ProcessComponents)
            .HasForeignKey(e => e.LearningOutcomeId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(e => e.SourceDocument)
            .WithMany()
            .HasForeignKey(e => e.SourceDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class FieldSkillConfiguration : IEntityTypeConfiguration<FieldSkill>
{
    public void Configure(EntityTypeBuilder<FieldSkill> b)
    {
        b.ToTable("field_skills");
        b.HasKey(e => e.Id);
        b.Property(e => e.Subject).HasMaxLength(100).IsRequired();
        b.Property(e => e.Code).HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasMaxLength(300).IsRequired();
        b.Property(e => e.ApprovalStatus).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(e => new { e.Subject, e.Code }).IsUnique();

        b.HasOne(e => e.SourceDocument)
            .WithMany()
            .HasForeignKey(e => e.SourceDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
