namespace MaarifPlatform.Domain.Entities;

/// <summary>§E Maarif Uyum Rubriği — kriter bazlı puan. Her satır bir kaynağa (source_ref)
/// atıfla gelmek zorundadır; atıfsız kriter kaydedilmemelidir (uygulama katmanında zorunlu kılınır).</summary>
public class AlignmentScore : Entity
{
    public Guid QuestionVersionId { get; set; }
    public QuestionVersion? QuestionVersion { get; set; }

    public string Criterion { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public decimal Weight { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string? SourceRef { get; set; }
    public bool IsCriticalGate { get; set; }
}

/// <summary>§15 Çeldirici Motoru — her çeldirici bir öğrenci hata tipine (misconception) bağlanır.
/// Gerçek öğrenci verisi olmadan bu eşleme hipotezdir, bkz. §elestiri madde 5.</summary>
public class Distractor : Entity
{
    public Guid QuestionVersionId { get; set; }
    public QuestionVersion? QuestionVersion { get; set; }

    public string OptionLabel { get; set; } = string.Empty;
    public string? MisconceptionCode { get; set; }
    public string? Explanation { get; set; }
    public bool IsHypothesis { get; set; } = true;
}
