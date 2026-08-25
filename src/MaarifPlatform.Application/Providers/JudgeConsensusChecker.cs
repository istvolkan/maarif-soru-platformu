namespace MaarifPlatform.Application.Providers;

/// <summary>§8/§10 Judge Provider Disagreement — VisualConsensusChecker'ın Judge karşılığı, saf
/// statik, AI çağrısı yapmaz. Birincil sonuç kanonik kalır (Vision'ın primary observation'ı
/// kanonik tutmasıyla aynı ilke); bu yalnızca uyarı üretir, sonucu DEĞİŞTİRMEZ.</summary>
public static class JudgeConsensusChecker
{
    public static IReadOnlyList<string> Compare(EvaluateQuestionResult primary, EvaluateQuestionResult secondary, int scoreDeltaThreshold)
    {
        var warnings = new List<string>();

        if (primary.Passed != secondary.Passed)
        {
            warnings.Add($"disagreement:passed uyuşmuyor (primary={primary.Passed}, secondary={secondary.Passed})");
        }

        var diff = Math.Abs(primary.QualityScore - secondary.QualityScore);
        if (diff > scoreDeltaThreshold)
        {
            warnings.Add($"disagreement:kalite puanı {diff} puan farklı (primary={primary.QualityScore}, secondary={secondary.QualityScore})");
        }

        return warnings;
    }
}
