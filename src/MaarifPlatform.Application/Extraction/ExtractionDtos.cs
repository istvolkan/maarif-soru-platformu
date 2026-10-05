using MaarifPlatform.Application.Providers;

namespace MaarifPlatform.Application.Extraction;

/// <summary>§10 PDF İşleme — PAGE EXTRACTION adımının çıktısı.</summary>
public sealed record ExtractedPage(int PageNo, string RawText);

/// <summary>§10 — QUESTION DETECTION adımının çıktısı: bir soru bloğu adayı.
/// <paramref name="Confidence"/> düşükse (örn. şık bulunamadı, gövde çok kısa) editöre
/// düşürülmesi gerekir — bu heuristic bir ilk geçiştir, AI destekli sınıflandırma değildir.</summary>
public sealed record QuestionBlock(
    int? QuestionNo,
    int PageNo,
    string RawBlockText,
    string Stem,
    IReadOnlyList<OptionCandidate> Options,
    bool IsLowConfidence);

public sealed record OptionCandidate(string Label, string Text);

/// <summary>Görsel (sayfa görüntüsü) tabanlı extraction — ham PDF metni + regex heuristic'in
/// (QuestionBlock/IQuestionSegmenter) yerine, sayfanın GERÇEK görüntüsünü bir Vision modeline
/// okutarak üretilen soru bloğu. Matematiksel gösterim (kesir/üs) ve diyagram içeren sayfalarda
/// ham metin çıkarımı sistematik olarak bozuluyor (örn. bir cevap anahtarı sayfasının soru
/// sanılması, ya da bir geometri sorusunun metninin yalnızca diyagramdaki nokta etiketlerine
/// indirgenmesi) — bkz. BookExtractionService.ExtractWithVisionAsync'in doc'u.</summary>
public sealed record TranscribedQuestionBlock(
    int? QuestionNo,
    string Stem,
    IReadOnlyList<OptionCandidate> Options,
    string? CorrectAnswer,
    bool HasVisual,
    string? VisualDescription);

public sealed record PageTranscriptionResult(
    IReadOnlyList<TranscribedQuestionBlock> Questions,
    AiUsage Usage);
