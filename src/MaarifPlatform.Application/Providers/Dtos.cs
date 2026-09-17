using MaarifPlatform.Application.Vision;
using MaarifPlatform.Application.Visuals;

namespace MaarifPlatform.Application.Providers;

// §4 Eski Soru Analizi — ANALYSIS ENGINE girdi/çıktısı.
// Visual alanı Vision mimarisi eklentisidir (sona, opsiyonel eklendi — mevcut pozisyonel
// çağrılar bozulmaz). requires_visual=false olan sorularda null kalır; §29 kritik kural:
// bu alan bir GÖZLEMdir, reasoning modeline "ground truth" olarak değil veri olarak sunulur.
public sealed record AnalyzeQuestionRequest(
    string OriginalQuestion,
    IReadOnlyList<string> OriginalOptions,
    string? OriginalAnswer,
    int Grade,
    string Subject,
    IReadOnlyList<GroundingReference> Grounding,
    VisualObservation? Visual = null);

/// <summary>§E rubrik kriterlerinden biri için LLM'in verdiği HAM değerlendirme. Ağırlıklandırma
/// ve nihai TransformationLevel kararı burada değil, deterministik RubricEngine'de (Application/Rubric)
/// hesaplanır — §A tasarım kararı: "LLM'e puanla değil, kriter bazlı ham değerlendirme sor".</summary>
public sealed record CriterionEvaluation(
    string Criterion,
    int Score,
    string Explanation,
    string? SourceRef,
    bool CriticalGateViolated);

public sealed record AnalyzeQuestionResult(
    string MathematicalCore,
    string? LearningOutcomeCode,
    string FieldSkill,
    string ConceptualSkill,
    bool ContextIsDecorative,
    IReadOnlyList<CriterionEvaluation> CriterionEvaluations,
    bool ManualReviewRequired,
    string? ManualReviewReason,
    AiUsage Usage);

// §6 Dönüşüm modları — CONSERVATIVE / TRANSFORM / REDESIGN.
public sealed record TransformQuestionRequest(
    string OriginalQuestion,
    string TransformationMode,
    AnalyzeQuestionResult Analysis,
    IReadOnlyList<GroundingReference> Grounding);

public sealed record TransformQuestionResult(
    string NewQuestion,
    IReadOnlyList<string> NewOptions,
    string CorrectAnswer,
    string Solution,
    IReadOnlyList<DistractorDto> Distractors,
    AiUsage Usage);

public sealed record DistractorDto(string OptionLabel, string? MisconceptionCode, string? Explanation);

// §8 AI Quality Judge — çapraz sağlayıcı değerlendirmesi.
public sealed record EvaluateQuestionRequest(
    string TransformedQuestion,
    IReadOnlyList<string> Options,
    string CorrectAnswer,
    string Solution,
    IReadOnlyList<GroundingReference> Grounding);

public sealed record EvaluateQuestionResult(
    int QualityScore,
    bool Passed,
    IReadOnlyList<string> CriticalFailures,
    IReadOnlyList<string> QualityFlags,
    AiUsage Usage);

/// <summary>§6/§2J Görsel Kullanımı — Faz 2a kapsamındaki gerçek değerler. "None" (Görsel
/// Kullanma) ve "Auto" (Otomatik — LLM pedagojik gereklilik kararı verir) dışındaki her değer
/// belirli bir <see cref="VisualSpecTypes"/> türünü ZORUNLU kılar.</summary>
public static class GenerationVisualUsage
{
    public const string None = "None";
    public const string Auto = "Auto";
    public const string FunctionGraph = "FunctionGraph";
    public const string CoordinateSystem = "CoordinateSystem";
    public const string GeometricShape = "GeometricShape";
    public const string Table = "Table";
}

// §16 Yeni Soru Üretim Modülü. Faz 1 cascading form alanları (SkillCodes/ContentFrameworks/
// ProcessComponents/VisualUsage) sona OPSİYONEL eklendi — eski POST /api/questions/generate
// (serbest metin, tek soru) çağrı şekli hiç değişmeden derlenir.
public sealed record GenerateQuestionRequest(
    int Grade,
    string Subject,
    string Theme,
    string LearningOutcomeCode,
    string Difficulty,
    string QuestionType,
    string Context,
    string ReasoningType,
    IReadOnlyList<GroundingReference> Grounding,
    IReadOnlyList<string>? SkillCodes = null,
    IReadOnlyList<string>? ContentFrameworks = null,
    IReadOnlyList<string>? ProcessComponents = null,
    string VisualUsage = "None");

// §6 Görsel Soru Motoru (Faz 2) — VisualRequired/VisualSpec yalnızca VisualUsage != "None"
// istendiğinde dolu gelir. VisualSpec LLM'in ürettiği bir TARİFTİR, gerçek görsel değildir —
// gerçek SVG'yi VisualSpecRenderer (Application/Visuals) deterministik olarak üretir.
public sealed record GenerateQuestionResult(
    string Question,
    IReadOnlyList<string> Options,
    string CorrectAnswer,
    string Solution,
    IReadOnlyList<DistractorDto> Distractors,
    AiUsage Usage,
    bool VisualRequired = false,
    VisualSpec? VisualSpec = null);

// ManualReviewRequired'a düşmüş, henüz Transform'a girmemiş sorular için editöre yönelik
// aksiyona dönük düzeltme önerisi — rubric.Issues'un (RubricEngine) teşhis odaklı açıklamalarından
// farklı olarak "nasıl düzeltilir" sorusuna cevap verir.
public sealed record RecommendRevisionRequest(
    string OriginalQuestion,
    int MaarifAlignmentScore,
    IReadOnlyList<string> Issues,
    IReadOnlyList<GroundingReference> Grounding);

public sealed record RecommendRevisionResult(string RevisionSuggestion, AiUsage Usage);

// Curriculum yapı çıkarımı (§2C-E/§9/§H) — LLM burada dokümanda YAZILI olanı yapılandırıyor,
// "invention" değil: girdi yalnızca gerçek ReferenceDocument chunk'ları (Grounding), kaynakta
// bulunmayan hiçbir tema/kazanım/beceri döndürülemez. Sonuç doğrudan kullanılmaz — Draft olarak
// kaydedilir, yalnızca admin onayından geçince (bkz. ApprovalStatus) dropdown'larda görünür.
public sealed record ExtractCurriculumRequest(
    int Grade,
    string Subject,
    IReadOnlyList<GroundingReference> DocumentChunks);

public sealed record CurriculumLearningOutcomeCandidate(
    string Code,
    string Description,
    IReadOnlyList<string> ContentFrameworks,
    IReadOnlyList<string> ProcessComponents,
    int? SourcePage);

public sealed record CurriculumThemeCandidate(
    string Name,
    IReadOnlyList<CurriculumLearningOutcomeCandidate> LearningOutcomes,
    int? SourcePage);

public sealed record CurriculumFieldSkillCandidate(string Code, string Name, int? SourcePage);

public sealed record ExtractCurriculumResult(
    IReadOnlyList<CurriculumThemeCandidate> Themes,
    IReadOnlyList<CurriculumFieldSkillCandidate> FieldSkills,
    AiUsage Usage);

// §7/§12 Curriculum Validator rolü — üretilen sorunun GERÇEK kazanım açıklaması + süreç
// bileşenlerinden en az birini ölçüp ölçmediğini kontrol eder. Bu, Judge'ın genel kalite/
// grounding kontrolünden FARKLI bir amaç taşır (bkz. TransformationOrchestrationService'teki
// EvaluateQuestionAsync) — o yüzden ayrı bir rol/metot olarak modellenir.
public sealed record ValidateCurriculumAlignmentRequest(
    string QuestionText,
    string LearningOutcomeCode,
    string LearningOutcomeDescription,
    IReadOnlyList<string> ProcessComponents);

public sealed record CurriculumAlignmentResult(
    bool MeasuresProcessComponent,
    int LearningOutcomeAlignmentScore,
    int SkillAlignmentScore,
    IReadOnlyList<string> Issues,
    AiUsage Usage);
