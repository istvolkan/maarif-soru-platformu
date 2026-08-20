using MaarifPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaarifPlatform.Infrastructure.Persistence.Configurations;

public class ReferenceDocumentConfiguration : IEntityTypeConfiguration<ReferenceDocument>
{
    public void Configure(EntityTypeBuilder<ReferenceDocument> b)
    {
        b.ToTable("reference_documents");
        b.HasKey(e => e.Id);
        b.Property(e => e.Title).HasMaxLength(500).IsRequired();
        b.Property(e => e.DocumentType).HasMaxLength(100).IsRequired();
        b.Property(e => e.Subject).HasMaxLength(100).IsRequired();
        b.Property(e => e.Version).HasMaxLength(50);
        b.Property(e => e.Authority).HasMaxLength(200);
        b.Property(e => e.StorageUri).HasMaxLength(1000);
        b.Property(e => e.DocumentHash).HasMaxLength(128);
        // §9: aynı dokümanın tekrar tekrar embed edilmesini engelleyen idempotent ingestion anahtarı.
        b.HasIndex(e => e.DocumentHash).IsUnique();

        b.HasMany(e => e.Chunks)
            .WithOne(e => e.ReferenceDocument)
            .HasForeignKey(e => e.ReferenceDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ReferenceChunkConfiguration : IEntityTypeConfiguration<ReferenceChunk>
{
    public void Configure(EntityTypeBuilder<ReferenceChunk> b)
    {
        b.ToTable("reference_chunks");
        b.HasKey(e => e.Id);
        b.Property(e => e.SectionPath).HasMaxLength(500);
        b.Property(e => e.ChunkText).IsRequired();

        // §G: embedding modeli boyutu pilotta netleşecek; 1536 (ör. text-embedding-3-small
        // sınıfı modeller) başlangıç varsayımı, migration ile değiştirilebilir.
        b.Property(e => e.Embedding).HasColumnType("vector(1536)");
    }
}
