namespace MaarifPlatform.Application.Extraction;

public sealed record RenderedPage(int PageNo, byte[] PngBytes, int WidthPx, int HeightPx);

/// <summary>Vision mimarisi — sayfa rasterizasyonu. <see cref="IPdfTextExtractor"/>'a bilinçli
/// olarak dokunulmaz (Interface Segregation): metin çıkarımı ihtiyacı olan kodlar görüntü
/// üretmeye zorlanmaz. Docnet.Core (PDFium) zaten hem metin hem render yapabildiği için
/// implementasyon yeni bir PDF kütüphanesi eklemez.</summary>
public interface IPdfPageRenderer
{
    Task<RenderedPage> RenderPageAsync(Stream pdfStream, int pageNo, CancellationToken ct = default);
}
