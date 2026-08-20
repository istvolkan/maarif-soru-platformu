using Docnet.Core;
using Docnet.Core.Models;
using MaarifPlatform.Application.Extraction;

namespace MaarifPlatform.Infrastructure.Extraction;

/// <summary>Dijital (metin katmanlı) PDF'ler için sayfa bazlı metin çıkarımı — PDFium'u saran
/// Docnet.Core (MIT, GowenGit) üzerinden. Not: bu proje daha önce "UglyToad.PdfPig" paketiyle
/// denendi, ancak nuget.org'daki paket kaydının ele geçirilmiş/el değiştirmiş olduğuna dair
/// güçlü belirtiler (tanınmayan sahip, jenerik açıklama, tutarsız sürüm sıçraması) bulunduğu için
/// o paket kaldırılıp Docnet.Core'a geçildi.
/// Taranmış/görsel sayfalarda metin boş veya çok kısa döner — bu durum segmenter tarafında
/// düşük güven olarak işaretlenir; OCR bu sürümde yoktur (§elestiri madde 4).</summary>
public class DocnetTextExtractor : IPdfTextExtractor
{
    // Docnet.Core sayfa boyutu render amaçlıdır, metin çıkarımını etkilemez; makul bir varsayılan yeterli.
    private static readonly PageDimensions DefaultDimensions = new(1080, 1920);

    public async Task<IReadOnlyList<ExtractedPage>> ExtractPagesAsync(Stream pdfStream, CancellationToken ct = default)
    {
        using var memory = new MemoryStream();
        await pdfStream.CopyToAsync(memory, ct);
        var bytes = memory.ToArray();

        using var library = DocLib.Instance;
        using var docReader = library.GetDocReader(bytes, DefaultDimensions);

        var pages = new List<ExtractedPage>();
        var pageCount = docReader.GetPageCount();

        for (var i = 0; i < pageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var pageReader = docReader.GetPageReader(i);
            var text = pageReader.GetText() ?? string.Empty;
            pages.Add(new ExtractedPage(i + 1, text));
        }

        return pages;
    }
}
