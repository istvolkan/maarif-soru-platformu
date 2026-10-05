using MaarifPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaarifPlatform.Infrastructure.Persistence.Configurations;

public class QuestionVariationBatchConfiguration : IEntityTypeConfiguration<QuestionVariationBatch>
{
    public void Configure(EntityTypeBuilder<QuestionVariationBatch> b)
    {
        b.ToTable("question_variation_batches");
        b.HasKey(e => e.Id);
        b.Property(e => e.SourceQuestionText).IsRequired();
        b.Property(e => e.Provider).HasMaxLength(50).IsRequired();
        b.Property(e => e.Model).HasMaxLength(100).IsRequired();
        b.Property(e => e.VisionProvider).HasMaxLength(50);
        b.Property(e => e.VisionModel).HasMaxLength(100);

        b.HasMany(e => e.Items)
            .WithOne(e => e.Batch)
            .HasForeignKey(e => e.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuestionVariationItemConfiguration : IEntityTypeConfiguration<QuestionVariationItem>
{
    public void Configure(EntityTypeBuilder<QuestionVariationItem> b)
    {
        b.ToTable("question_variation_items");
        b.HasKey(e => e.Id);
        b.Property(e => e.QuestionText).IsRequired();
        b.Property(e => e.OptionsJson).HasColumnType("jsonb");
        b.Property(e => e.CorrectAnswer).IsRequired();
        b.Property(e => e.Solution).IsRequired();
        b.HasIndex(e => new { e.BatchId, e.OrderNo }).IsUnique();
    }
}
