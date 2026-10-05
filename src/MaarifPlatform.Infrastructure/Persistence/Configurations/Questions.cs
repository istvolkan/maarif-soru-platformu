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
            nameof(QuestionDna.SourceReferencesJson), nameof(QuestionDna.ExtensionsJson),
            nameof(QuestionDna.VisualElementsJson), nameof(QuestionDna.VisualRelationsJson),
            nameof(QuestionDna.VisualTextJson), nameof(QuestionDna.VisualSymbolsJson),
            nameof(QuestionDna.VisualMeasurementsJson), nameof(QuestionDna.VisualWarningsJson)
        })
        {
            b.Property(jsonProp).HasColumnType("jsonb");
        }

        b.Property(e => e.VisualConfidence).HasPrecision(5, 4);

        b.HasIndex(e => e.LearningOutcomeCode);
        // Vision Router'ın "requires_visual=true, henüz işlenmemiş" sorgusu bu indekse dayanır.
        b.HasIndex(e => e.RequiresVisual);
        b.HasIndex(e => e.ArchetypeId);

        // SetNull: bir archetype silinirse (ör. ileride admin bir temizlik yaparsa) üye sorular
        // kaybolmaz, yalnızca kümesiz kalır.
        b.HasOne(e => e.Archetype)
            .WithMany()
            .HasForeignKey(e => e.ArchetypeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class QuestionVisualAssetConfiguration : IEntityTypeConfiguration<QuestionVisualAsset>
{
    public void Configure(EntityTypeBuilder<QuestionVisualAsset> b)
    {
        b.ToTable("question_visual_assets");
        b.HasKey(e => e.Id);
        b.Property(e => e.StorageUri).HasMaxLength(1000).IsRequired();
        b.Property(e => e.BoundingBoxJson).HasColumnType("jsonb");
        b.Property(e => e.AssetHash).HasMaxLength(128).IsRequired();
        b.Property(e => e.ContentType).HasMaxLength(100);
        // §26 cache: aynı görsel aynı sağlayıcı/model/prompt sürümüyle tekrar işlenmesin.
        b.HasIndex(e => e.AssetHash);

        b.HasOne(e => e.Question)
            .WithMany()
            .HasForeignKey(e => e.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(e => e.BookPage)
            .WithMany()
            .HasForeignKey(e => e.BookPageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class QuestionEmbeddingConfiguration : IEntityTypeConfiguration<QuestionEmbedding>
{
    public void Configure(EntityTypeBuilder<QuestionEmbedding> b)
    {
        b.ToTable("question_embeddings");
        b.HasKey(e => e.Id);
        b.Property(e => e.Subject).HasMaxLength(100).IsRequired();
        b.Property(e => e.SourceKind).HasConversion<string>().HasMaxLength(20);
        // ReferenceChunkConfiguration'daki 1536 boyut varsayımıyla AYNI — aynı IEmbeddingProvider paylaşılır.
        b.Property(e => e.Embedding).HasColumnType("vector(1536)").IsRequired();
        b.HasIndex(e => new { e.Grade, e.Subject });
        b.HasIndex(e => e.SourceKind);
        b.HasIndex(e => e.QuestionVersionId).IsUnique();

        b.HasOne(e => e.QuestionVersion)
            .WithMany()
            .HasForeignKey(e => e.QuestionVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuestionArchetypeConfiguration : IEntityTypeConfiguration<QuestionArchetype>
{
    public void Configure(EntityTypeBuilder<QuestionArchetype> b)
    {
        b.ToTable("question_archetypes");
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).HasMaxLength(200).IsRequired();
        b.Property(e => e.Subject).HasMaxLength(100).IsRequired();
        b.Property(e => e.MaarifAffinity).HasPrecision(5, 2);
        b.Property(e => e.QualityScore).HasPrecision(5, 2);
        // QuestionEmbeddingConfiguration'daki 1536 boyut varsayımıyla AYNI — aynı IEmbeddingProvider paylaşılır.
        b.Property(e => e.CentroidEmbedding).HasColumnType("vector(1536)").IsRequired();
        b.HasIndex(e => e.Subject);
    }
}

public class QuestionArchetypeMemberConfiguration : IEntityTypeConfiguration<QuestionArchetypeMember>
{
    public void Configure(EntityTypeBuilder<QuestionArchetypeMember> b)
    {
        b.ToTable("question_archetype_members");
        b.HasKey(e => e.Id);
        // §54 "Pattern P-119, Kitap A → 14 kez" bakımından bir soru versiyonu bir archetype'a
        // yalnızca BİR kez üye olabilir (yeniden analiz edilirse yeni bir QuestionVersion/DNA
        // satırı zaten oluşur, bu eski üyeliği DEĞİŞTİRMEZ).
        b.HasIndex(e => new { e.ArchetypeId, e.QuestionVersionId }).IsUnique();
        b.HasIndex(e => new { e.ArchetypeId, e.BookId });

        b.HasOne(e => e.Archetype)
            .WithMany(e => e.Members)
            .HasForeignKey(e => e.ArchetypeId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(e => e.QuestionVersion)
            .WithMany()
            .HasForeignKey(e => e.QuestionVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
