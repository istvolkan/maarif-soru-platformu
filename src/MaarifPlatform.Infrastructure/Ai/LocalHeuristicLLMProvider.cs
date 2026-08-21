using MaarifPlatform.Application.Providers;

namespace MaarifPlatform.Infrastructure.Ai;

/// <summary>Dış API anahtarı GEREKTİRMEZ. Yalnızca Analysis pipeline'ının borusunu (grounding →
/// AiRun kaydı → RubricEngine → AlignmentScore → durum geçişi) gerçek bir AI çağrısı olmadan
/// doğrulamak içindir. GERÇEK MATEMATİKSEL/PEDAGOJİK DEĞERLENDİRME YAPMAZ — tüm skorlar
/// yapısal sinyallerden (bağlam/grounding varlığı, şık sayısı, gövde uzunluğu) türetilir.
/// Üretimde <see cref="AnthropicLLMProvider"/> gibi gerçek bir sağlayıcıyla değiştirilmelidir.</summary>
public class LocalHeuristicLLMProvider : ILLMProvider
{
    public string Name => "local-heuristic";

    public Task<AnalyzeQuestionResult> AnalyzeQuestionAsync(AnalyzeQuestionRequest request, CancellationToken ct = default)
    {
        var hasGrounding = request.Grounding.Count > 0;
        var hasWellFormedOptions = request.OriginalOptions.Count is >= 3 and <= 6;
        var stemLength = request.OriginalQuestion.Trim().Length;

        int Score(int baseScore, int groundingBonus = 0, int optionsBonus = 0, int lengthBonus = 0)
        {
            var score = baseScore;
            if (hasGrounding) score += groundingBonus;
            if (hasWellFormedOptions) score += optionsBonus;
            if (stemLength > 60) score += lengthBonus;
            return Math.Clamp(score, 0, 100);
        }

        string Note(string criterion) =>
            $"[MOCK] {criterion} — gerçek değerlendirme yapılmadı; yapısal sinyallere " +
            "(grounding varlığı, şık sayısı, gövde uzunluğu) dayanan bir tahmindir.";

        var evaluations = new List<CriterionEvaluation>
        {
            new("mathematical_accuracy", 100, "[MOCK] Doğruluk kontrolü yapılmadı, varsayılan tam puan verildi.", null, false),
            new("learning_outcome_alignment", Score(40, groundingBonus: 30), Note("learning_outcome_alignment"),
                hasGrounding ? request.Grounding[0].SectionPath : null, false),
            new("field_skill_alignment", Score(55, groundingBonus: 10), Note("field_skill_alignment"), null, false),
            new("process_component", Score(50), Note("process_component"), null, false),
            new("reasoning", Score(50), Note("reasoning"), null, false),
            new("problem_solving", Score(55, optionsBonus: 10), Note("problem_solving"), null, false),
            new("modeling", Score(45), Note("modeling"), null, false),
            new("context_quality", Score(45, groundingBonus: 15), Note("context_quality"), null, false),
            new("representation_usage", Score(50, optionsBonus: 10), Note("representation_usage"), null, false),
            new("grade_level_fit", Score(65), Note("grade_level_fit"), null, false),
            new("language_clarity", Score(60, lengthBonus: 10), Note("language_clarity"), null, false),
            new("measurability", Score(55, optionsBonus: 15), Note("measurability"), null, false),
            new("distractor_quality", Score(50, optionsBonus: 15), Note("distractor_quality"), null, false),
            new("cognitive_load_balance", Score(60), Note("cognitive_load_balance"), null, false)
        };

        var result = new AnalyzeQuestionResult(
            MathematicalCore: Truncate(request.OriginalQuestion, 60),
            LearningOutcomeCode: null,
            FieldSkill: "unknown",
            ConceptualSkill: "unknown",
            ContextIsDecorative: !hasGrounding,
            CriterionEvaluations: evaluations,
            ManualReviewRequired: !hasGrounding,
            ManualReviewReason: hasGrounding
                ? null
                : "[MOCK] RAG referans kaynağı bulunamadı; kazanım/beceri alanları doğrulanamadı.",
            Usage: new AiUsage("local-heuristic", "mock-v1", EstimateTokens(request), 220, 0m, 5));

        return Task.FromResult(result);
    }

    public Task<TransformQuestionResult> TransformQuestionAsync(TransformQuestionRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("Transformation Sprint 5'te eklenecek.");

    public Task<EvaluateQuestionResult> EvaluateQuestionAsync(EvaluateQuestionRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("Quality Judge Sprint 5'te eklenecek.");

    public Task<GenerateQuestionResult> GenerateQuestionAsync(GenerateQuestionRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("Soru üretimi ileriki bir sprintte eklenecek.");

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";

    private static int EstimateTokens(AnalyzeQuestionRequest request) =>
        (request.OriginalQuestion.Length + request.OriginalOptions.Sum(o => o.Length)) / 4;
}
