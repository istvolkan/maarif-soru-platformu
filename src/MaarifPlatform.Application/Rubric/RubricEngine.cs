using MaarifPlatform.Application.Providers;
using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Application.Rubric;

public sealed record RubricResult(
    int WeightedScore,
    TransformationLevel Level,
    bool CriticalGateFailed,
    IReadOnlyList<string> MissingCriteria,
    IReadOnlyList<string> Issues);

/// <summary>§A tasarım kararı: puanlama mantığı kodda, deterministik ve denetlenebilir tutulur.
/// LLM sadece kriter başına ham bir değerlendirme verir. §8 AI Quality Judge BU MOTORU
/// KULLANMAZ — EvaluateQuestionResult, LLM'in doğrudan döndürdüğü nihai bir skordur (kriter
/// bazlı ayrıştırma yok); orijinal tasarım dokümanının §8 bölümü repoda mevcut değil, bu
/// nedenle spesifikasyonu olmayan bir kriter/ağırlık tablosu icat edilmedi (bkz. Sprint 8).</summary>
public static class RubricEngine
{
    private const int LowScoreIssueThreshold = 60;

    public static RubricResult Evaluate(IReadOnlyList<CriterionEvaluation> evaluations)
    {
        var byKey = evaluations
            .GroupBy(e => e.Criterion)
            .ToDictionary(g => g.Key, g => g.First());

        decimal weightedSum = 0;
        var missing = new List<string>();
        var issues = new List<string>();
        var criticalGateFailed = false;

        foreach (var def in MaarifRubric.Criteria)
        {
            if (!byKey.TryGetValue(def.Key, out var evaluation))
            {
                missing.Add(def.Key);
                continue;
            }

            var clampedScore = Math.Clamp(evaluation.Score, 0, 100);
            weightedSum += clampedScore / 100m * def.Weight;

            if (clampedScore < LowScoreIssueThreshold)
            {
                issues.Add($"{def.Key}: {evaluation.Explanation}");
            }

            if (def.IsCriticalGate && evaluation.CriticalGateViolated)
            {
                criticalGateFailed = true;
                issues.Add($"CRITICAL[{def.Key}]: {evaluation.Explanation}");
            }
        }

        var weightedScore = (int)Math.Round(weightedSum, MidpointRounding.AwayFromZero);
        var level = criticalGateFailed ? TransformationLevel.ManualReviewRequired : DecideLevel(weightedScore);

        return new RubricResult(weightedScore, level, criticalGateFailed, missing, issues);
    }

    // §5 Dönüşüm Kararı eşikleri.
    private static TransformationLevel DecideLevel(int score) => score switch
    {
        >= 90 => TransformationLevel.NoChange,
        >= 75 => TransformationLevel.LightEdit,
        >= 55 => TransformationLevel.ModerateTransformation,
        >= 35 => TransformationLevel.MajorTransformation,
        _ => TransformationLevel.Rewrite
    };
}
