using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Application.Rubric;

public enum TransformDecision { SkipAiApprove, Conservative, Transform, Redesign }

/// <summary>§5/§6 köprüsü: RubricEngine'in ürettiği TransformationLevel'i editörün seçtiği
/// TransformationMode'a çevirir. NoChange/LightEdit için Transform hiç çağrılmaz (soru zaten
/// yeterince uyumlu) — doğrudan AiApproved'a geçilir. ManualReviewRequired bu noktaya hiç
/// ulaşmaz (Analysis aşaması zaten orada durdurur); Reject şu an hiçbir yerde üretilmiyor.</summary>
public static class TransformationModeMapper
{
    public static TransformDecision Decide(TransformationLevel level) => level switch
    {
        TransformationLevel.NoChange or TransformationLevel.LightEdit => TransformDecision.SkipAiApprove,
        TransformationLevel.ModerateTransformation => TransformDecision.Conservative,
        TransformationLevel.MajorTransformation => TransformDecision.Transform,
        TransformationLevel.Rewrite => TransformDecision.Redesign,
        _ => throw new InvalidOperationException(
            $"TransformationLevel={level} Transform aşamasına ulaşmamalıydı (Analysis zaten durdurmalıydı).")
    };
}
