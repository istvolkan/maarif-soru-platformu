using MaarifPlatform.Application.Rag;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Intelligence;

public sealed record ArchetypeClassification(Guid ArchetypeId, string ArchetypeName, bool IsNewArchetype);

/// <summary>§45/§46/§54 Question Archetype/Pattern Library kümeleme — Question Intelligence
/// Engine Faz 3. Hangi archetype'a katılacağına LLM değil, <see cref="QuestionArchetype.CentroidEmbedding"/>'e
/// karşı DETERMİNİSTİK en-yakın-komşu arama karar verir (bkz. MultipleChoiceOptionPolicy/
/// GenerationBlueprintBuilder'daki "LLM'e sorma, kodda hesapla" felsefesi — burada da aynısı
/// uygulanır: LLM yalnızca bir kez, Faz 2'nin DNA analizinde, bu sorunun reasoning_pattern'ını
/// TÜRETİR; hangi kümeye ait olduğuna matematik karar verir).
///
/// Kümeleme "DNA özellik metni" üzerinden embed edilir — HAM SORU METNİ ÜZERİNDEN DEĞİL (bkz.
/// BuildFeatureText). Amaç ifade/kelime benzerliğini değil YAPISAL benzerliği yakalamaktır: iki
/// soru hiç ortak kelime paylaşmasa bile aynı reasoning_pattern + representation_types + context_type
/// kombinasyonuna sahipse aynı archetype'a düşmelidir.</summary>
public class ArchetypeClusteringService(MaarifDbContext db, IEmbeddingProvider embeddingProvider)
{
    // Başlangıç değeri — admin-configurable yapmak (SystemSettingsService üzerinden) bu ilk
    // sürümde BİLİNÇLİ OLARAK ertelendi (MultipleChoiceOptionPolicy'nin sabit kuralıyla aynı
    // felsefe: önce gerçek kitap verisiyle gözlemle, erken optimize etme).
    private const double SimilarityThreshold = 0.85;

    /// <summary>Bu sorunun DNA'sını en yakın archetype'a atar; yeterince yakın bir archetype
    /// yoksa yeni bir tane oluşturur. <paramref name="featureText"/> boşsa (Faz 2 DNA analizi
    /// hiçbir alan döndürmediyse) null döner — kümeleme için anlamlı veri yoktur.</summary>
    public async Task<ArchetypeClassification?> ClassifyAsync(
        string featureText, int grade, string subject, string? visualType, int? maarifAlignmentScore,
        Guid bookId, Guid questionVersionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(featureText) || string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var embedding = new Vector(await embeddingProvider.EmbedAsync(featureText, ct));

        var nearest = await db.QuestionArchetypes
            .Where(a => a.Subject == subject)
            .Select(a => new { Archetype = a, Distance = a.CentroidEmbedding.CosineDistance(embedding) })
            .OrderBy(x => x.Distance)
            .FirstOrDefaultAsync(ct);

        var similarity = nearest is null ? 0 : 1 - nearest.Distance;
        var isNew = nearest is null || similarity < SimilarityThreshold;

        QuestionArchetype archetype;
        if (isNew)
        {
            archetype = new QuestionArchetype
            {
                Name = BuildDefaultName(featureText),
                Subject = subject,
                GradeRangeMin = grade,
                GradeRangeMax = grade,
                VisualType = visualType,
                CentroidEmbedding = embedding,
                SourceSupportCount = 1,
                DistinctBookSupportCount = 1,
                MaarifAffinity = maarifAlignmentScore
            };
            db.QuestionArchetypes.Add(archetype);
        }
        else
        {
            archetype = nearest!.Archetype;
            UpdateMembershipStats(archetype, embedding, grade, maarifAlignmentScore);

            var isNewBookForArchetype = !await db.QuestionArchetypeMembers
                .AnyAsync(m => m.ArchetypeId == archetype.Id && m.BookId == bookId, ct);
            if (isNewBookForArchetype)
            {
                archetype.DistinctBookSupportCount++;
            }
        }

        db.QuestionArchetypeMembers.Add(new QuestionArchetypeMember
        {
            Archetype = archetype,
            QuestionVersionId = questionVersionId,
            BookId = bookId
        });

        return new ArchetypeClassification(archetype.Id, archetype.Name, isNew);
    }

    /// <summary>Merkezi ağırlıklı ortalamayla günceller (yeni üye merkezi kendine doğru azar azar
    /// çeker) — bkz. sınıf doc'undaki "LLM'siz, matematikle büyüyen kümeler" ilkesi.</summary>
    private static void UpdateMembershipStats(
        QuestionArchetype archetype, Vector newEmbedding, int grade, int? maarifAlignmentScore)
    {
        var oldCount = archetype.SourceSupportCount;
        var newCount = oldCount + 1;

        var oldCentroid = archetype.CentroidEmbedding.ToArray();
        var newVector = newEmbedding.ToArray();
        var updatedCentroid = new float[oldCentroid.Length];
        for (var i = 0; i < updatedCentroid.Length; i++)
        {
            updatedCentroid[i] = (oldCentroid[i] * oldCount + newVector[i]) / newCount;
        }

        archetype.CentroidEmbedding = new Vector(updatedCentroid);
        archetype.SourceSupportCount = newCount;
        archetype.GradeRangeMin = Math.Min(archetype.GradeRangeMin, grade);
        archetype.GradeRangeMax = Math.Max(archetype.GradeRangeMax, grade);

        if (maarifAlignmentScore is int score)
        {
            archetype.MaarifAffinity = archetype.MaarifAffinity is decimal existing
                ? (existing * oldCount + score) / newCount
                : score;
        }
    }

    /// <summary>Yeni bir archetype için ekstra bir LLM çağrısı yapmadan (bkz. sınıf doc'u)
    /// deterministik, okunabilir bir varsayılan isim türetir — DNA analizinin "REASONING: ..."
    /// bloğundan türetilir (mevcutsa), yoksa özellik metninin ilk parçası kısaltılır.</summary>
    private static string BuildDefaultName(string featureText)
    {
        var reasoningPart = featureText
            .Split('|')
            .Select(p => p.Trim())
            .FirstOrDefault(p => p.StartsWith("REASONING:", StringComparison.Ordinal));

        var label = reasoningPart is not null ? reasoningPart["REASONING:".Length..].Trim() : featureText;
        return Truncate(label, 150);
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";
}
