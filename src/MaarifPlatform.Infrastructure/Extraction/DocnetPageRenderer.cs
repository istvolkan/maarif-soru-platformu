using Docnet.Core;
using Docnet.Core.Models;
using MaarifPlatform.Application.Extraction;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MaarifPlatform.Infrastructure.Extraction;

/// <summary>Vision mimarisi — sayfa rasterizasyonu. Docnet.Core (PDFium) zaten
/// <see cref="DocnetTextExtractor"/> için kurulu; aynı kütüphane <c>GetImage()</c> ile ham
/// piksel verisi de verebildiği için yeni bir PDF bağımlılığı eklenmedi. PNG kodlaması için
/// SixLabors.ImageSharp kullanılır (nuget.org: sahip "sixlabors", verified, 298M+ indirme —
/// PdfPig olayından sonra kurulan doğrulama alışkanlığı burada da uygulandı).</summary>
public class DocnetPageRenderer : IPdfPageRenderer
{
    private static readonly PageDimensions DefaultDimensions = new(1600, 2200);

    public async Task<RenderedPage> RenderPageAsync(Stream pdfStream, int pageNo, CancellationToken ct = default)
    {
        var pages = await RenderPagesAsync(pdfStream, [pageNo], ct);
        return pages[0];
    }

    public async Task<IReadOnlyList<RenderedPage>> RenderPagesAsync(
        Stream pdfStream, IReadOnlyList<int> pageNumbers, CancellationToken ct = default)
    {
        using var memory = new MemoryStream();
        await pdfStream.CopyToAsync(memory, ct);
        var bytes = memory.ToArray();

        using var library = DocLib.Instance;
        using var docReader = library.GetDocReader(bytes, DefaultDimensions);

        var results = new List<RenderedPage>(pageNumbers.Count);
        foreach (var pageNo in pageNumbers)
        {
            ct.ThrowIfCancellationRequested();

            using var pageReader = docReader.GetPageReader(pageNo - 1);
            var rawBgra = pageReader.GetImage();
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();

            using var image = Image.LoadPixelData<Bgra32>(rawBgra, width, height);
            using var pngStream = new MemoryStream();
            await image.SaveAsPngAsync(pngStream, ct);

            results.Add(new RenderedPage(pageNo, pngStream.ToArray(), width, height));
        }

        return results;
    }
}
