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

    /// <summary>§49/§57 Question Intelligence Engine Faz 4 — kullanıcıya SUNULAN bir seçim
    /// DEĞİLDİR (bilinçli tasarım kararı: "kullanıcıya bırakma bu tarz konuları", bkz.
    /// feedback_no_manual_strategy_toggles hafıza notu). Sistem her üretimde bu ağırlığı OTOMATİK
    /// uygular; yalnızca admin Ayarlar'dan değiştirilebilir. 0-100 arası, "Maarif Modeli Uyumlu"
    /// (Pool A) havuzdan ne kadar öncelikli örnek/pattern çekileceğini ifade eder (§57'deki
    /// PatternScore'un MaarifAffinity terimini ağırlıklandırır). DİKKAT: bu alan şu an YALNIZCA
    /// bir ayar olarak var — gerçek üretim zamanı tüketicisi (exemplar/pattern retrieval,
    /// §50/§51/§58) henüz Faz 6'da inşa edilecek; bu fazda yalnızca §48'in havuz ayrımı
    /// (QuestionPoolClassifier) ve bu ayarın admin panelinde var olması tamamlanmıştır.</summary>
    public int MaarifWeight { get; set; } = 70;
}
