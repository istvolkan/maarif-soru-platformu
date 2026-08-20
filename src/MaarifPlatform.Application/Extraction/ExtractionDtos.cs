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
