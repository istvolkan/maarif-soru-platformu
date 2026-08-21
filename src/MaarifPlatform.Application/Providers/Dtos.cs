namespace MaarifPlatform.Application.Providers;

// §4 Eski Soru Analizi — ANALYSIS ENGINE girdi/çıktısı.
public sealed record AnalyzeQuestionRequest(
    string OriginalQuestion,
    IReadOnlyList<string> OriginalOptions,
    string? OriginalAnswer,
    int Grade,
    string Subject,
    IReadOnlyList<GroundingReference> Grounding);

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

// §16 Yeni Soru Üretim Modülü.
public sealed record GenerateQuestionRequest(
    int Grade,
    string Subject,
    string Theme,
    string LearningOutcomeCode,
    string Difficulty,
    string QuestionType,
    string Context,
    string ReasoningType,
    IReadOnlyList<GroundingReference> Grounding);

public sealed record GenerateQuestionResult(
    string Question,
    IReadOnlyList<string> Options,
    string CorrectAnswer,
    string Solution,
    IReadOnlyList<DistractorDto> Distractors,
    AiUsage Usage);
