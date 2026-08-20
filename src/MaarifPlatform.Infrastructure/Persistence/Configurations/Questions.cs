using MaarifPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaarifPlatform.Infrastructure.Persistence.Configurations;

public class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> b)
    {
        b.ToTable("questions");
        b.HasKey(e => e.Id);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(e => e.Status);

        b.HasOne(e => e.BookPage)
            .WithMany()
            .HasForeignKey(e => e.BookPageId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(e => e.MaarifStandardVersion)
            .WithMany()
            .HasForeignKey(e => e.MaarifStandardVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(e => e.Versions)
            .WithOne(e => e.Question)
            .HasForeignKey(e => e.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuestionVersionConfiguration : IEntityTypeConfiguration<QuestionVersion>
{
    public void Configure(EntityTypeBuilder<QuestionVersion> b)
    {
        b.ToTable("question_versions");
        b.HasKey(e => e.Id);
        b.Property(e => e.Stage).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.PayloadJson).HasColumnType("jsonb");
        b.Property(e => e.CreatedBy).HasMaxLength(200);
        b.HasIndex(e => new { e.QuestionId, e.VersionNo }).IsUnique();

        b.HasOne(e => e.Dna)
            .WithOne(e => e.QuestionVersion)
            .HasForeignKey<QuestionDna>(e => e.QuestionVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuestionDnaConfiguration : IEntityTypeConfiguration<QuestionDna>
{
    public void Configure(EntityTypeBuilder<QuestionDna> b)
    {
        b.ToTable("question_dna");
        b.HasKey(e => e.Id);
        b.Property(e => e.Difficulty).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.TransformationLevel).HasConversion<string>().HasMaxLength(30);

        foreach (var jsonProp in new[]
        {
            nameof(QuestionDna.OriginalOptionsJson), nameof(QuestionDna.RepresentationTypesJson),
            nameof(QuestionDna.ReasoningTypesJson), nameof(QuestionDna.AlignmentIssuesJson),
            nameof(QuestionDna.NewOptionsJson), nameof(QuestionDna.QualityFlagsJson),
            nameof(QuestionDna.SourceReferencesJson), nameof(QuestionDna.ExtensionsJson)
        })
        {
            b.Property(jsonProp).HasColumnType("jsonb");
        }

        b.HasIndex(e => e.LearningOutcomeCode);
    }
}
