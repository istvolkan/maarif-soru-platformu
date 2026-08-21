using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Domain.Entities;

/// <summary>§D Question DNA — çekirdek ilişkisel alanlar + <see cref="ExtensionsJson"/> ile
/// esnek büyüme. Şema versiyonu her kayıtta taşınır (§18 versioning ile karıştırılmamalı:
/// bu, DNA şemasının kendi versiyonu).</summary>
public class QuestionDna : Entity
{
    public Guid QuestionVersionId { get; set; }
    public QuestionVersion? QuestionVersion { get; set; }

    // Kimlik & Kaynak
    public string? SourceBook { get; set; }
    public int? SourcePage { get; set; }
    public int? Grade { get; set; }
    public string? Subject { get; set; }
    public string? Theme { get; set; }
    public string? Topic { get; set; }
    public string? Subtopic { get; set; }

    // Orijinal içerik
    public string? OriginalQuestion { get; set; }
    public string? OriginalOptionsJson { get; set; }
    public string? OriginalAnswer { get; set; }
    public string? OriginalVisualReference { get; set; }

    // Matematiksel öz & pedagojik sınıflandırma
    public string? MathematicalCore { get; set; }
    public string? LearningOutcome { get; set; }
    public string? LearningOutcomeCode { get; set; }
    public string? FieldSkill { get; set; }
    public string? ConceptualSkill { get; set; }
    public string? ProcessComponent { get; set; }
    public string? QuestionType { get; set; }
    public string? ContextType { get; set; }
    public string? ContextQuality { get; set; }
    public string? RepresentationTypesJson { get; set; }
    public string? CognitiveLevel { get; set; }
    public string? ReasoningTypesJson { get; set; }

    // Çözüm & zorluk
    public string? ExpectedSolutionSteps { get; set; }
    public DifficultyLevel? Difficulty { get; set; }
    public int? AiEstimatedStudentTimeMinutes { get; set; }

    // Maarif değerlendirmesi
    public int? MaarifAlignmentScore { get; set; }
    public string? AlignmentIssuesJson { get; set; }
    public TransformationLevel? TransformationLevel { get; set; }

    // Dönüşüm çıktısı
    public string? NewQuestion { get; set; }
    public string? NewOptionsJson { get; set; }
    public string? CorrectAnswer { get; set; }
    public string? Solution { get; set; }

    // Kalite & süreç
    public int? QualityScore { get; set; }
    public string? QualityFlagsJson { get; set; }
    public bool EditorRequired { get; set; }
    public string? SourceReferencesJson { get; set; }
    public string DnaSchemaVersion { get; set; } = "1.0";

    // Görsel (Vision) — Multimodal Question Processing eklentisi. Sık filtrelenen/raporlanan
    // alanlar doğrudan kolon; nadiren sorgulanan detaylar jsonb. Tüm alanlar nullable/false
    // varsayılan olduğu için mevcut (text-only) satırlar hiç etkilenmez.
    public bool RequiresVisual { get; set; }
    public string? VisualType { get; set; }
    public bool? VisualRequiredForSolution { get; set; }
    public string? VisualDescription { get; set; }
    public decimal? VisualConfidence { get; set; }
    public int? VisualDependencyScore { get; set; }
    public string? VisualReusability { get; set; }
    public string? VisualElementsJson { get; set; }
    public string? VisualRelationsJson { get; set; }
    public string? VisualTextJson { get; set; }
    public string? VisualSymbolsJson { get; set; }
    public string? VisualMeasurementsJson { get; set; }
    public string? VisualWarningsJson { get; set; }

    /// <summary>Şema henüz olgunlaşmamış yeni alanlar için serbest jsonb alanı (§elestiri madde 12).</summary>
    public string? ExtensionsJson { get; set; }
}
