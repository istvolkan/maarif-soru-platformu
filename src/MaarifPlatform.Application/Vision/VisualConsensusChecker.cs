namespace MaarifPlatform.Application.Vision;

/// <summary>§10 Provider Disagreement — iki bağımsız Vision sağlayıcısının aynı görseli farklı
/// yorumlayıp yorumlamadığını deterministik olarak tespit eder. AI çağrısı yapmaz; ilk sonucu
/// sessizce kabul etmemek için bu karşılaştırma zorunludur (§10: "Bu durumda sistem ilk sonucu
/// sessizce kabul etmemelidir").</summary>
public static class VisualConsensusChecker
{
    public static IReadOnlyList<VisualWarning> Compare(VisualObservation primary, VisualObservation secondary)
    {
        var warnings = new List<VisualWarning>();

        if (!string.Equals(primary.VisualType, secondary.VisualType, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new VisualWarning(
                "VISION_PROVIDER_DISAGREEMENT",
                $"visual_type uyuşmuyor: birincil '{primary.VisualType}', ikincil '{secondary.VisualType}'.",
                Math.Min(primary.Confidence, secondary.Confidence)));
        }

        var secondaryByPair = secondary.Relations
            .GroupBy(r => NormalizedPairKey(r.Subject, r.Object))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var primaryRelation in primary.Relations)
        {
            var key = NormalizedPairKey(primaryRelation.Subject, primaryRelation.Object);
            if (secondaryByPair.TryGetValue(key, out var secondaryRelation)
                && !string.Equals(primaryRelation.Relation, secondaryRelation.Relation, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(new VisualWarning(
                    "VISION_PROVIDER_DISAGREEMENT",
                    $"'{primaryRelation.Subject} {primaryRelation.Relation} {primaryRelation.Object}' (birincil) " +
                    $"ile '{secondaryRelation.Subject} {secondaryRelation.Relation} {secondaryRelation.Object}' " +
                    "(ikincil) uyuşmuyor.",
                    Math.Min(primaryRelation.Confidence, secondaryRelation.Confidence)));
            }
        }

        return warnings;
    }

    // Sıra bağımsız eşleştirme: "AB perpendicular BC" ile "BC perpendicular AB" aynı çift olarak görülür.
    private static (string, string) NormalizedPairKey(string subject, string obj)
    {
        var s = subject.ToUpperInvariant();
        var o = obj.ToUpperInvariant();
        return string.CompareOrdinal(s, o) <= 0 ? (s, o) : (o, s);
    }
}
