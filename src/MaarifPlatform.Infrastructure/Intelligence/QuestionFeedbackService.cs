using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Intelligence;

/// <summary>§55/§56 Geri Bildirim Öğrenme — Question Intelligence Engine Faz 7. Hangi olayların
/// GERÇEKTEN tetiklendiği <see cref="QuestionLifecycleEventType"/> doc'unda açıklanır.</summary>
public class QuestionFeedbackService(MaarifDbContext db)
{
    // §56 "tek bir reddi genel kural sanma" ilkesi — bir archetype'ın onay/red geçmişi bu
    // örnek sayısına ULAŞMADAN §57 PatternScore'u hiç ETKİLEMEZ (nötr kalır).
    private const int MinInstitutionalSampleCount = 5;

    /// <summary>Yalnızca ekler (bkz. QuestionLifecycleEvent doc'u — bir kalite kapısı değildir),
    /// SaveChangesAsync ÇAĞIRMAZ — çağıran tarafın kendi (genelde Status güncellemesiyle aynı)
    /// SaveChangesAsync'i bu kaydı da aynı atomik işlemde flush eder (AiRun'ların eklenme deseniyle
    /// AYNI — bkz. GenerationOrchestrationService).</summary>
    public void Log(Guid questionId, QuestionLifecycleEventType eventType, Guid? actorUserId = null, string? detailJson = null)
    {
        db.QuestionLifecycleEvents.Add(new QuestionLifecycleEvent
        {
            QuestionId = questionId,
            EventType = eventType,
            ActorUserId = actorUserId,
            DetailJson = detailJson
        });
    }

    /// <summary>§57 PatternScore'un InstitutionalPreference terimi için girdi — bir archetype'ın
    /// üye sorularının onay/red oranı. DİKKAT: bu yalnızca <see cref="AnalysisOrchestrationService"/>
    /// aracılığıyla (kitaptan çıkarılıp Analiz edilmiş) ArchetypeId ataması almış sorular için
    /// anlamlıdır — Soru Üret (§16) akışındaki sorular HENÜZ archetype kümelemesinden geçmiyor
    /// (bkz. ArchetypeClusteringService'in yalnızca AnalysisOrchestrationService'e bağlı olduğu
    /// Faz 3 tasarım notu). Bu, bir hata değil bilinen bir kapsam sınırıdır — Soru Üret akışını da
    /// kümelemeye dahil etmek, bu akışa TAM bir DNA analizi (Faz 2 LLM-B) eklemeyi gerektirir ki bu
    /// bilinçli olarak orijinal tasarımın (GenerationOrchestrationService "puanlama/dönüşüm/yargı
    /// yapmaz" ilkesi) kapsamı dışında bırakılmıştır.
    ///
    /// Yetersiz örnekli (&lt;<see cref="MinInstitutionalSampleCount"/>) bir archetype için null
    /// döner — çağıran taraf bunu NÖTR olarak yorumlamalıdır (bkz. PatternScorer.Score).</summary>
    public async Task<IReadOnlyDictionary<Guid, decimal?>> GetArchetypeApprovalRatesAsync(
        IReadOnlyList<Guid> archetypeIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, decimal?>();
        if (archetypeIds.Count == 0)
        {
            return result;
        }

        var memberRows = await db.QuestionArchetypeMembers
            .Where(m => archetypeIds.Contains(m.ArchetypeId))
            .Join(db.QuestionVersions, m => m.QuestionVersionId, v => v.Id, (m, v) => new { m.ArchetypeId, v.QuestionId })
            .Distinct()
            .ToListAsync(ct);

        var questionIdsByArchetype = memberRows
            .GroupBy(r => r.ArchetypeId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.QuestionId).ToHashSet());

        var allQuestionIds = memberRows.Select(r => r.QuestionId).Distinct().ToList();
        var events = allQuestionIds.Count == 0
            ? []
            : await db.QuestionLifecycleEvents
                .Where(e => allQuestionIds.Contains(e.QuestionId)
                    && (e.EventType == QuestionLifecycleEventType.Approved || e.EventType == QuestionLifecycleEventType.Rejected))
                .Select(e => new { e.QuestionId, e.EventType })
                .ToListAsync(ct);

        foreach (var archetypeId in archetypeIds)
        {
            var questionIds = questionIdsByArchetype.GetValueOrDefault(archetypeId) ?? [];
            var relevant = events.Where(e => questionIds.Contains(e.QuestionId)).ToList();
            var approved = relevant.Count(e => e.EventType == QuestionLifecycleEventType.Approved);
            var rejected = relevant.Count(e => e.EventType == QuestionLifecycleEventType.Rejected);
            var total = approved + rejected;

            result[archetypeId] = total < MinInstitutionalSampleCount ? null : (decimal)approved / total;
        }

        return result;
    }
}
