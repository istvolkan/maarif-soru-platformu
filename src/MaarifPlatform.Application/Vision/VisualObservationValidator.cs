namespace MaarifPlatform.Application.Vision;

/// <summary>§6 Mathematical/Scientific Fidelity — bir VisualObservation'ı ikinci bir AI çağrısı
/// yapmadan deterministik tutarlılık açısından denetler. RubricEngine'deki "puanlama LLM'in
/// işi değil, kod tabanlı denetim yapılır" felsefesiyle aynı çizgide (§A). Tüm IVisionProvider
/// implementasyonları <c>ValidateVisualStructureAsync</c>'te bu sınıfı çağırmalı — mantığın
/// provider başına tekrar yazılmasını önler.</summary>
public static class VisualObservationValidator
{
    private const decimal LowConfidenceThreshold = 0.5m;

    public static IReadOnlyList<VisualWarning> Validate(VisualObservation observation)
    {
        var warnings = new List<VisualWarning>();

        if (observation.Confidence < LowConfidenceThreshold)
        {
            warnings.Add(new VisualWarning("LOW_CONFIDENCE",
                $"Gözlem güveni düşük ({observation.Confidence:0.00}).", observation.Confidence));
        }

        var elementIds = observation.Elements.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var relation in observation.Relations)
        {
            if (!elementIds.Contains(relation.Subject) || !elementIds.Contains(relation.Object))
            {
                warnings.Add(new VisualWarning("DANGLING_RELATION_REFERENCE",
                    $"'{relation.Subject} {relation.Relation} {relation.Object}' ilişkisi bilinmeyen bir öğeye atıfta bulunuyor.",
                    relation.Confidence));
            }
        }

        if (observation.Elements.Count == 0 && observation.Relations.Count == 0)
        {
            warnings.Add(new VisualWarning("EMPTY_OBSERVATION", "Görselden hiçbir öğe/ilişki çıkarılamadı.", observation.Confidence));
        }

        return warnings;
    }
}
