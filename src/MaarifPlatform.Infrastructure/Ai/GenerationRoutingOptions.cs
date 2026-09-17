namespace MaarifPlatform.Infrastructure.Ai;

/// <summary>Faz 1 Soru Üret pipeline'ının çalışma-zamanlı ayarları — Ai/Judge/Vision routing
/// options'larıyla AYNI desen (IOptionsMonitor + Admin Ayarlar'dan canlı geçersiz kılma).
/// Curriculum Validator dışındaki roller (Domain Validator/Second Critic/Consensus) mevcut
/// Judge consensus altyapısını (EvaluateQuestionAsync + JudgeRoutingOptions) yeniden kullanır —
/// bkz. GenerationOrchestrationService.</summary>
public class GenerationRoutingOptions
{
    /// <summary>Boşsa Ai:Provider'a (Generator'ın kendisi) düşer — o zaman aynı model kendi
    /// ürettiği soruyu curriculum'a karşı denetler. Farklı bir sağlayıcı seçmek daha güçlü bir
    /// çapraz kontrol sağlar.</summary>
    public string? CurriculumValidatorProvider { get; set; }

    /// <summary>Bir blueprint slotu curriculum/domain/similarity kontrolünden geçemezse en fazla
    /// bu kadar kez yeniden denenir (§15 fail-safe) — tükenirse slot "başarısız" işaretlenir,
    /// düşük kaliteli bir soruyla ASLA doldurulmaz.</summary>
    public int MaxRegenerationAttempts { get; set; } = 2;

    /// <summary>pgvector cosine similarity bu eşiği (0-1) aşan bir soru, mevcut havuzdaki bir
    /// sorunun yalnızca sayı/isim değiştirilmiş kopyası sayılır ve reddedilir (§10).</summary>
    public double SimilarityRejectThreshold { get; set; } = 0.92;

    /// <summary>§3 "Soru Adedi" alanının üst sınırı — Soru Üret formu bunun üzerine çıkan bir
    /// istek göndermez.</summary>
    public int MaxQuestionCount { get; set; } = 50;
}
