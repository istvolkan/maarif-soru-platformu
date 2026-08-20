using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Application.Rag;
using MaarifPlatform.Application.Storage;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace MaarifPlatform.Infrastructure.Rag;

public sealed record IngestionResult(int ChunksCreated);

/// <summary>§G RAG ingestion orkestrasyonu: PDF → sayfa → chunk → embedding → pgvector.
/// Aynı dokümanın tekrar tekrar gömülmesi, upload aşamasında <see cref="ReferenceDocument.DocumentHash"/>
/// üzerindeki benzersiz indeksle engellenir (§9); bu servis yalnızca "henüz chunk'lanmamış" bir
/// dokümanı işler.</summary>
public class ReferenceIngestionService(
    MaarifDbContext db,
    IBookFileStorage storage,
    IPdfTextExtractor textExtractor,
    IReferenceChunker chunker,
    IEmbeddingProvider embeddingProvider)
{
    public async Task<IngestionResult> IngestAsync(Guid referenceDocumentId, CancellationToken ct = default)
    {
        var document = await db.ReferenceDocuments.FirstOrDefaultAsync(d => d.Id == referenceDocumentId, ct)
            ?? throw new InvalidOperationException($"Referans doküman bulunamadı: {referenceDocumentId}");

        var alreadyIngested = await db.ReferenceChunks.AnyAsync(c => c.ReferenceDocumentId == referenceDocumentId, ct);
        if (alreadyIngested)
        {
            throw new InvalidOperationException("Bu doküman için ingestion zaten çalıştırılmış.");
        }

        await using var pdfStream = await storage.OpenReadAsync(document.StorageUri, ct);
        var pages = await textExtractor.ExtractPagesAsync(pdfStream, ct);
        var candidates = chunker.Chunk(pages);

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var embedding = await embeddingProvider.EmbedAsync(candidate.Text, ct);

            db.ReferenceChunks.Add(new ReferenceChunk
            {
                ReferenceDocumentId = referenceDocumentId,
                Page = candidate.Page,
                SectionPath = candidate.SectionPath,
                ChunkText = candidate.Text,
                Embedding = new Vector(embedding)
            });
        }

        await db.SaveChangesAsync(ct);

        return new IngestionResult(candidates.Count);
    }
}
