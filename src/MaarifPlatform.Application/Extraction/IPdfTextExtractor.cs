namespace MaarifPlatform.Application.Extraction;

/// <summary>§10: dijital PDF için sayfa bazlı metin çıkarımı. OCR gerektiren (taranmış)
/// sayfalar için ayrı bir implementasyon ileride bu sözleşmeyi karşılayabilir (§elestiri madde 4:
/// MVP'de görsel/OCR-ağırlıklı içerik kapsam dışı, doğrudan insan incelemesine düşer).</summary>
public interface IPdfTextExtractor
{
    Task<IReadOnlyList<ExtractedPage>> ExtractPagesAsync(Stream pdfStream, CancellationToken ct = default);
}
