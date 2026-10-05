using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Application.Intelligence;

/// <summary>§41/§48 Soru Havuzu sınıflandırması — Question Intelligence Engine Faz 4.
/// MultipleChoiceOptionPolicy ile AYNI felsefe: LLM'e "bu soru hangi havuza ait" diye SORULMAZ,
/// zaten deterministik olarak hesaplanmış MaarifAlignmentScore'dan (RubricEngine) kodda türetilir.</summary>
public static class QuestionPoolClassifier
{
    /// <summary>§41'in 5 bandını (0-29 Geleneksel, 30-49 Düşük, 50-69 Kısmi, 70-84 Yüksek,
    /// 85-100 Güçlü) §48'in 3 havuzuna daraltır: Yüksek+Güçlü → MaarifAligned (Pool A),
    /// Kısmi → Hybrid (Pool C), Geleneksel+Düşük → Traditional (Pool B).</summary>
    public static QuestionPoolClassification Classify(int maarifAlignmentScore) => maarifAlignmentScore switch
    {
        >= 70 => QuestionPoolClassification.MaarifAligned,
        >= 50 => QuestionPoolClassification.Hybrid,
        _ => QuestionPoolClassification.Traditional
    };
}
