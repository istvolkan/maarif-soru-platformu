namespace MaarifPlatform.Application.Generation;

/// <summary>Çoktan seçmeli şık adedi politikası — LLM'e sorulmaz, Grade'e göre deterministik
/// sabitlenir (ortaokul 5-8: 4 şık, lise 9-12: 5 şık; GenerationBlueprintBuilder ile aynı felsefe).
/// Yalnızca §16 Soru Üretim Modülü'ne (sıfırdan üretilen sorular) uygulanır — Transform/Analysis
/// pipeline'ı gerçek kitaplardan çıkarılmış soruların ORİJİNAL şık sayısını bu politikayla
/// DEĞİŞTİRMEZ, kapsam dışıdır.</summary>
public static class MultipleChoiceOptionPolicy
{
    public const string MultipleChoiceType = "Çoktan Seçmeli";

    public static bool IsMultipleChoice(string questionType) =>
        string.Equals(questionType, MultipleChoiceType, StringComparison.OrdinalIgnoreCase);

    public static int RequiredOptionCount(int grade) => grade <= 8 ? 4 : 5;

    /// <summary>Çoktan seçmeli değilse null — çağıranlar bu durumda mevcut esnek (3-6) davranışı korur.</summary>
    public static int? RequiredOptionCountFor(int grade, string questionType) =>
        IsMultipleChoice(questionType) ? RequiredOptionCount(grade) : null;
}
