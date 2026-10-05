using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Application.Intelligence;

/// <summary>§41/§48 Soru Havuzu sınıflandırması — Question Intelligence Engine Faz 4.
/// MultipleChoiceOptionPolicy ile AYNI felsefe: LLM'e "bu soru hangi havuza ait" diye SORULMAZ,
/// zaten deterministik olarak hesaplanmış MaarifAlignmentScore'dan (RubricEngine) kodda türetilir.</summary>
public static class QuestionPoolClassifier
{
    /// <summary>2026-10 kullanıcı kararı: "Maarif Uyumlu" eşiği AÇIKÇA 75 olarak belirlendi
    /// (§41'in orijinal 70 eşiğinin YERİNE geçer — Soru Havuzu'nda sorular bu eşiğe göre
    /// görsel olarak işaretlenip, öğretmen "yalnızca Maarif Uyumlu olanları onayla" gibi toplu
    /// işlemler yapabilsin diye). ≥75 → MaarifAligned (Pool A), 50-74 → Hybrid (Pool C),
    /// &lt;50 → Traditional (Pool B). Hiçbir bant ATILMAZ — yalnızca bir ETİKETTİR.</summary>
    public const int MaarifAlignedThreshold = 75;

    public static QuestionPoolClassification Classify(int maarifAlignmentScore) => maarifAlignmentScore switch
    {
        >= MaarifAlignedThreshold => QuestionPoolClassification.MaarifAligned,
        >= 50 => QuestionPoolClassification.Hybrid,
        _ => QuestionPoolClassification.Traditional
    };
}
