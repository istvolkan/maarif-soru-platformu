using MaarifPlatform.Application.Rag;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Generation;

public sealed record SimilarityCheckResult(bool IsDuplicate, double? MostSimilarScore, Guid? MostSimilarQuestionVersionId);

/// <summary>§10 Benzerlik/Tekrar Kontrolü — ReferenceSearchService'in pgvector CosineDistance
/// deseninin birebir aynısı, yalnızca havuzu ReferenceChunk yerine aynı (Grade,Subject)
/// içindeki daha önce üretilmiş sorular. Yalnızca kelime benzerliğine değil SEMANTİK
/// benzerliğe bakar — "aynı problem yapısının sayı/isim değiştirilmiş versiyonu" da yakalanır.</summary>
public class QuestionSimilarityService(MaarifDbContext db, IEmbeddingProvider embeddingProvider)
{
    public async Task<SimilarityCheckResult> CheckAsync(
        int grade, string subject, string questionText, double rejectThreshold, CancellationToken ct = default)
    {
        var queryEmbedding = new Vector(await embeddingProvider.EmbedAsync(questionText, ct));

        var nearest = await db.QuestionEmbeddings
            .Where(e => e.Grade == grade && e.Subject == subject)
            .OrderBy(e => e.Embedding.CosineDistance(queryEmbedding))
            .Select(e => new { e.QuestionVersionId, Distance = e.Embedding.CosineDistance(queryEmbedding) })
            .FirstOrDefaultAsync(ct);

        if (nearest is null)
        {
            return new SimilarityCheckResult(false, null, null);
        }

        // CosineDistance = 1 - cosine similarity; eşik "benzerlik" cinsinden tanımlı (§10/Ayarlar).
        var similarity = 1 - nearest.Distance;
        return new SimilarityCheckResult(similarity >= rejectThreshold, similarity, nearest.QuestionVersionId);
    }

    public async Task RecordAsync(
        Guid questionVersionId, int grade, string subject, string questionText, CancellationToken ct = default)
    {
        var embedding = new Vector(await embeddingProvider.EmbedAsync(questionText, ct));
        db.QuestionEmbeddings.Add(new QuestionEmbedding
        {
            QuestionVersionId = questionVersionId,
            Grade = grade,
            Subject = subject,
            Embedding = embedding
        });
        await db.SaveChangesAsync(ct);
    }
}
