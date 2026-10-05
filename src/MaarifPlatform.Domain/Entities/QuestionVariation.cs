namespace MaarifPlatform.Domain.Entities;

/// <summary>Soru Çeşitlendir (bilinçli olarak Question/QuestionVersion'dan AYRI bir havuz —
/// kullanıcının sağladığı örnek soru hiçbir Türkiye Yüzyılı Maarif Modeli kazanım/beceri
/// doğrulamasından geçmez, bu nedenle curriculum-validated Soru Havuzu ile KARIŞTIRILMAMALI,
/// bkz. kullanıcı talebi "maarif uyumlu olanlar farklı bir yerde tutulmalı"). Kullanım/maliyet
/// burada AiRun'a değil, doğrudan bu entity'ye yazılır — AiRun.QuestionId zorunlu bir Question
/// FK'sine bağlıdır ve bu akışın hiç Question satırı yoktur.</summary>
public class QuestionVariationBatch : Entity
{
    public string SourceQuestionText { get; set; } = string.Empty;
    public int RequestedCount { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public int LatencyMs { get; set; }

    /// <summary>Yalnızca kaynak soru bir görsel/PDF'ten sağlandığında dolar (2026-10) — metin
    /// LLM'inin VaryQuestionAsync kullanımından (Provider/Model/CostUsd) AYRI, çünkü iki farklı
    /// sağlayıcı/çağrı: Vision (transkripsiyon) + LLM (çeşitlendirme). Admin'in Vision:Provider
    /// yapılandırmasını (örn. Gemini) gerçekten test edip etmediğini bu alanlardan görebilir.</summary>
    public string? VisionProvider { get; set; }
    public string? VisionModel { get; set; }
    public decimal? VisionCostUsd { get; set; }

    public ICollection<QuestionVariationItem> Items { get; set; } = new List<QuestionVariationItem>();
}

/// <summary>Tek bir çeşitlendirilmiş soru. Options boşsa (açık uçlu kaynak soru) CorrectAnswer
/// doğrudan cevap metnidir, şık referansı değildir.</summary>
public class QuestionVariationItem : Entity
{
    public Guid BatchId { get; set; }
    public QuestionVariationBatch? Batch { get; set; }

    public int OrderNo { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string OptionsJson { get; set; } = "[]";
    public string CorrectAnswer { get; set; } = string.Empty;
    public string Solution { get; set; } = string.Empty;
}
