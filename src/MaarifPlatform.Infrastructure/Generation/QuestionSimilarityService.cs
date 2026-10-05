using MaarifPlatform.Application.Rag;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Generation;

public sealed record SimilarityCheckResult(
    bool IsDuplicate, double? MostSimilarScore, Guid? MostSimilarQuestionVersionId,
    QuestionEmbeddingSourceKind? MostSimilarSourceKind);

/// <summary>§10/§52 Benzerlik/Tekrar Kontrolü (Orijinallik Kontrolü/Originality Firewall) —
/// ReferenceSearchService'in pgvector CosineDistance deseninin birebir aynısı, yalnızca havuzu
/// ReferenceChunk yerine aynı (Grade,Subject) içindeki <see cref="QuestionEmbedding"/> satırları.
/// Yalnızca kelime benzerliğine değil SEMANTİK benzerliğe bakar — "aynı problem yapısının
/// sayı/isim değiştirilmiş versiyonu" da yakalanır. Havuz hem AI-ÜRETİLMİŞ hem kitaptan
/// ÇIKARILMIŞ gerçek kaynak sorulardan oluşur (bkz. QuestionEmbeddingSourceKind) — bu yüzden bu
/// kontrol artık yalnızca üretim havuzuna karşı değil, GERÇEK KAYNAK METNE karşı da çalışır.</summary>
public class QuestionSimilarityService(MaarifDbContext db, IEmbeddingProvider embeddingProvider)
{
    public async Task<SimilarityCheckResult> CheckAsync(
        int grade, string subject, string questionText, double rejectThreshold, CancellationToken ct = default)
    {
        var queryEmbedding = new Vector(await embeddingProvider.EmbedAsync(questionText, ct));

        var nearest = await db.QuestionEmbeddings
            .Where(e => e.Grade == grade && e.Subject == subject)
            .OrderBy(e => e.Embedding.CosineDistance(queryEmbedding))
            .Select(e => new { e.QuestionVersionId, e.SourceKind, Distance = e.Embedding.CosineDistance(queryEmbedding) })
            .FirstOrDefaultAsync(ct);

        if (nearest is null)
        {
            return new SimilarityCheckResult(false, null, null, null);
        }

        // CosineDistance = 1 - cosine similarity; eşik "benzerlik" cinsinden tanımlı (§10/Ayarlar).
        var similarity = 1 - nearest.Distance;
        return new SimilarityCheckResult(similarity >= rejectThreshold, similarity, nearest.QuestionVersionId, nearest.SourceKind);
    }

    public async Task RecordAsync(
        Guid questionVersionId, int grade, string subject, string questionText,
        QuestionEmbeddingSourceKind sourceKind, CancellationToken ct = default)
    {
        var embedding = new Vector(await embeddingProvider.EmbedAsync(questionText, ct));
        db.QuestionEmbeddings.Add(new QuestionEmbedding
        {
            QuestionVersionId = questionVersionId,
            Grade = grade,
            Subject = subject,
            SourceKind = sourceKind,
            Embedding = embedding
        });
        await db.SaveChangesAsync(ct);
    }
}
