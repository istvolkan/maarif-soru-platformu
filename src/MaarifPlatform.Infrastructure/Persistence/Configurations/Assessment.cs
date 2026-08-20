using MaarifPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaarifPlatform.Infrastructure.Persistence.Configurations;

public class LearningOutcomeConfiguration : IEntityTypeConfiguration<LearningOutcome>
{
    public void Configure(EntityTypeBuilder<LearningOutcome> b)
    {
        b.ToTable("learning_outcomes");
        b.HasKey(e => e.Id);
        b.Property(e => e.Code).HasMaxLength(50).IsRequired();
        b.Property(e => e.Subject).HasMaxLength(100).IsRequired();
        b.Property(e => e.Description).IsRequired();
        b.HasIndex(e => new { e.Code, e.MaarifStandardVersionId }).IsUnique();

        b.HasOne(e => e.SourceDocument)
            .WithMany()
            .HasForeignKey(e => e.SourceDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(e => e.MaarifStandardVersion)
            .WithMany()
            .HasForeignKey(e => e.MaarifStandardVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AlignmentScoreConfiguration : IEntityTypeConfiguration<AlignmentScore>
{
    public void Configure(EntityTypeBuilder<AlignmentScore> b)
    {
        b.ToTable("alignment_scores");
        b.HasKey(e => e.Id);
        b.Property(e => e.Criterion).HasMaxLength(100).IsRequired();
        b.Property(e => e.Explanation).IsRequired();
        b.Property(e => e.SourceRef).HasMaxLength(500);
        b.Property(e => e.Score).HasPrecision(5, 2);
        b.Property(e => e.Weight).HasPrecision(5, 2);
    }
}

public class DistractorConfiguration : IEntityTypeConfiguration<Distractor>
{
    public void Configure(EntityTypeBuilder<Distractor> b)
    {
        b.ToTable("distractors");
        b.HasKey(e => e.Id);
        b.Property(e => e.OptionLabel).HasMaxLength(5).IsRequired();
        b.Property(e => e.MisconceptionCode).HasMaxLength(100);
    }
}
