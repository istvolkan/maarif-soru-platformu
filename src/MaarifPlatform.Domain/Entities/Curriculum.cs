using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Domain.Entities;

/// <summary>Sınıf+Ders'e bağlı resmi Maarif Modeli teması (§2C). Kod, tema adını hiçbir zaman
/// üretmez — yalnızca CurriculumExtractionService ile gerçek bir ReferenceDocument'ten
/// çıkarılır ve admin onayından geçer (bkz. ApprovalStatus).</summary>
public class Theme : Entity
{
    public int Grade { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public Guid MaarifStandardVersionId { get; set; }
    public MaarifStandardVersion? MaarifStandardVersion { get; set; }

    public Guid? SourceDocumentId { get; set; }
    public ReferenceDocument? SourceDocument { get; set; }
    public int? SourcePage { get; set; }

    public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Draft;

    public ICollection<LearningOutcome> LearningOutcomes { get; set; } = new List<LearningOutcome>();
}

/// <summary>Bir kazanıma bağlı Konu/İçerik Çerçevesi (§2E) — çoklu seçilebilir üretim
/// parametresi. Örn. "Doğrusal Fonksiyonlar ve Nitel Özellikleri".</summary>
public class ContentFramework : Entity
{
    public Guid LearningOutcomeId { get; set; }
    public LearningOutcome? LearningOutcome { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid? SourceDocumentId { get; set; }
    public ReferenceDocument? SourceDocument { get; set; }
    public int? SourcePage { get; set; }

    public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Draft;
}

/// <summary>Bir kazanımın ölçtüğü süreç bileşeni (§12 — üretilen sorunun bunlardan en az
/// birini gerçekten ölçmesi gerekir; bu doğrulamanın gerçek kaynağı, LLM'in "Maarif'e
/// uygun" demesi değil).</summary>
public class ProcessComponent : Entity
{
    public Guid LearningOutcomeId { get; set; }
    public LearningOutcome? LearningOutcome { get; set; }

    public string Description { get; set; } = string.Empty;

    public Guid? SourceDocumentId { get; set; }
    public ReferenceDocument? SourceDocument { get; set; }
    public int? SourcePage { get; set; }

    public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Draft;
}

/// <summary>Ders bazlı Alan Becerisi (§2H, örn. Matematik için MAB1-MAB5). Subject bazlı
/// multi-select üretim parametresi; "Otomatik" seçilirse GenerationBlueprintBuilder
/// kazanıma bağlı bir varsayılan seçer.</summary>
public class FieldSkill : Entity
{
    public string Subject { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public Guid? SourceDocumentId { get; set; }
    public ReferenceDocument? SourceDocument { get; set; }
    public int? SourcePage { get; set; }

    public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Draft;
}
