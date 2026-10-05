using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Domain.Entities;

/// <summary>§55/§56 Geri Bildirim Öğrenme (Question Intelligence Engine Faz 7) — bir sorunun
/// yaşam döngüsündeki GERÇEK öğretmen/sistem eylemlerinin günlüğü (bkz. QuestionLifecycleEventType
/// doc'u — hangi eylemlerin GERÇEKTEN tetiklendiği orada açıklanır). <see cref="CreatedAt"/>
/// (Entity base) olay zamanını taşır, ayrı bir OccurredAt alanına gerek yok.
///
/// Bu kayıtlar bir onay/kalite KAPISI DEĞİLDİR — yalnızca gözlemdir, hiçbir pipeline kararını
/// etkilemez (tek istisna: §57 PatternScore'un InstitutionalPreference terimi, bkz. PatternScorer
/// ve QuestionFeedbackService.GetArchetypeApprovalRatesAsync — o da yalnızca YETERLİ örnek
/// varsa devreye girer, "tek bir reddi genel kural sanma" ilkesiyle).</summary>
public class QuestionLifecycleEvent : Entity
{
    public Guid QuestionId { get; set; }
    public Question? Question { get; set; }

    public QuestionLifecycleEventType EventType { get; set; }

    // Hangi kullanıcının eylemi tetiklediği — sistem tarafından (ör. otomatik üretim) tetiklenen
    // olaylarda null kalır.
    public Guid? ActorUserId { get; set; }

    // Ör. UsedInBook için {"bookId": "..."} gibi bağlama özgü, serbest biçimli ek bilgi.
    public string? DetailJson { get; set; }
}
