using MaarifPlatform.Application.Rag;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Rag;

/// <summary>§G Retrieval — pgvector CosineDistance ile top-k arama. Grounding güven eşiği
/// (§elestiri madde 9) burada uygulanmaz; çağıran (Analysis/Judge pipeline'ı) dönen
/// <see cref="RetrievedChunk.Distance"/> değerine bakarak MANUAL_REVIEW_REQUIRED kararını verir.</summary>
public class ReferenceSearchService(MaarifDbContext db, IEmbeddingProvider embeddingProvider)
{
    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string query,
        int topK = 5,
        int? grade = null,
        string? subject = null,
        CancellationToken ct = default)
    {
        var queryEmbedding = new Vector(await embeddingProvider.EmbedAsync(query, ct));

        var chunks = db.ReferenceChunks
            .Include(c => c.ReferenceDocument)
            .Where(c => c.ReferenceDocument!.Active);

        if (grade is not null)
        {
            chunks = chunks.Where(c => c.ReferenceDocument!.Grade == null || c.ReferenceDocument!.Grade == grade);
        }

        if (!string.IsNullOrWhiteSpace(subject))
        {
            chunks = chunks.Where(c => c.ReferenceDocument!.Subject == subject);
        }

        var results = await chunks
            .OrderBy(c => c.Embedding!.CosineDistance(queryEmbedding))
            .Take(topK)
            .Select(c => new
            {
                c.ReferenceDocumentId,
                DocumentTitle = c.ReferenceDocument!.Title,
                c.Page,
                c.SectionPath,
                c.ChunkText,
                Distance = c.Embedding!.CosineDistance(queryEmbedding)
            })
            .ToListAsync(ct);

        return results
            .Select(r => new RetrievedChunk(r.ReferenceDocumentId, r.DocumentTitle, r.Page, r.SectionPath, r.ChunkText, (float)r.Distance))
            .ToList();
    }
}
