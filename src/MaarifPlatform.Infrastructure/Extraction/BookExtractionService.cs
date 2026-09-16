using System.Security.Cryptography;
using System.Text.Json;
using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Application.Storage;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Extraction;

/// <summary>§10 PDF İşleme ana orkestrasyonu: PDF → sayfa → soru bloğu → Question DNA (§D) → DB.
/// DbContext'e doğrudan bağımlı olduğu için (repository soyutlaması bu ölçekte gereksiz
/// bir katman olurdu) bilinçli olarak Infrastructure'da tutulur; Api sadece bunu çağırır.</summary>
public class BookExtractionService(
    MaarifDbContext db,
    IBookFileStorage storage,
    IPdfTextExtractor textExtractor,
    IPdfPageRenderer pageRenderer,
    IQuestionSegmenter segmenter)
{
    public async Task<BookExtractionResult> ExtractAsync(Guid bookId, CancellationToken ct = default)
    {
        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == bookId, ct)
            ?? throw new InvalidOperationException($"Kitap bulunamadı: {bookId}");

        var alreadyExtracted = await db.BookPages.AnyAsync(p => p.BookId == bookId, ct);
        if (alreadyExtracted)
        {
            throw new InvalidOperationException("Bu kitap için extraction zaten çalıştırılmış.");
        }

        await using var pdfStream = await storage.OpenReadAsync(book.StorageUri, ct);
        var pages = await textExtractor.ExtractPagesAsync(pdfStream, ct);

        var bookPages = pages.Select(p => new BookPage
        {
            BookId = bookId,
            PageNo = p.PageNo,
            RawText = p.RawText,
            OcrUsed = false
        }).ToList();

        db.BookPages.AddRange(bookPages);
        await db.SaveChangesAsync(ct);

        var pageIdByNo = bookPages.ToDictionary(p => p.PageNo, p => p.Id);
        var blocks = segmenter.Segment(pages);

        var lowConfidenceCount = 0;
        var questionsByPage = new List<(Question Question, int PageNo)>();

        foreach (var block in blocks)
        {
            var question = new Question
            {
                BookId = bookId,
                BookPageId = pageIdByNo.GetValueOrDefault(block.PageNo),
                QuestionNo = block.QuestionNo,
                Status = QuestionStatus.Extracted
            };
            questionsByPage.Add((question, block.PageNo));

            var version = new QuestionVersion
            {
                Question = question,
                QuestionId = question.Id,
                VersionNo = 1,
                Stage = QuestionVersionStage.Original,
                PayloadJson = JsonSerializer.Serialize(block),
                CreatedBy = "extraction-pipeline"
            };

            var qualityFlags = block.IsLowConfidence
                ? new[] { "low_confidence_segmentation" }
                : Array.Empty<string>();

            if (block.IsLowConfidence)
            {
                lowConfidenceCount++;
            }

            var dna = new QuestionDna
            {
                QuestionVersion = version,
                QuestionVersionId = version.Id,
                SourceBook = book.Title,
                SourcePage = block.PageNo,
                Grade = book.Grade,
                Subject = book.Subject,
                OriginalQuestion = block.Stem,
                OriginalOptionsJson = JsonSerializer.Serialize(block.Options),
                DnaSchemaVersion = "1.0",
                QualityFlagsJson = JsonSerializer.Serialize(qualityFlags),
                EditorRequired = block.IsLowConfidence
            };

            db.Questions.Add(question);
            db.QuestionVersions.Add(version);
            db.QuestionDnas.Add(dna);
        }

        book.TotalPages = pages.Count;
        await db.SaveChangesAsync(ct);

        await CaptureOriginalPageImagesAsync(book, questionsByPage, ct);
        await db.SaveChangesAsync(ct);

        return new BookExtractionResult(pages.Count, blocks.Count, lowConfidenceCount);
    }

    /// <summary>Her sorunun kaynak PDF sayfasını, hiçbir AI çağrısı yapmadan, deterministik bir
    /// "orijinal görsel" olarak sabitler (BoundingBoxJson=null — tam sayfa kuralı, bkz.
    /// QuestionVisualAsset). Vision analizi (VisionAnalysisService) artık bu görseli DEĞİŞTİRMEZ;
    /// yalnızca AI'nin bounding-box tahmini soru meta verisine (VisualType/Description) bilgi
    /// amaçlı eklenir. Böylece "incelemeye gönderilen görsel" ile "analizden çıkan görsel" arasında
    /// artık fark olmaz — ikisi de hep aynı, ilk yakalanan sayfa görüntüsüdür.
    /// Sayfalar TEK belge açma oturumunda (RenderPagesAsync) render edilir — yüzlerce sorulu bir
    /// kitapta sayfa başına PDF'i baştan açmanın maliyetini önler.</summary>
    private async Task CaptureOriginalPageImagesAsync(
        Book book, IReadOnlyList<(Question Question, int PageNo)> questionsByPage, CancellationToken ct)
    {
        var distinctPages = questionsByPage.Select(q => q.PageNo).Distinct().OrderBy(p => p).ToList();
        if (distinctPages.Count == 0)
        {
            return;
        }

        IReadOnlyList<RenderedPage> rendered;
        await using (var pdfStream = await storage.OpenReadAsync(book.StorageUri, ct))
        {
            rendered = await pageRenderer.RenderPagesAsync(pdfStream, distinctPages, ct);
        }

        var byPageNo = rendered.ToDictionary(r => r.PageNo);
        var pageIdByNo = await db.BookPages
            .Where(p => p.BookId == book.Id)
            .ToDictionaryAsync(p => p.PageNo, p => p.Id, ct);

        foreach (var (question, pageNo) in questionsByPage)
        {
            if (!byPageNo.TryGetValue(pageNo, out var page))
            {
                continue;
            }

            var assetHash = Convert.ToHexString(SHA256.HashData(page.PngBytes));
            var storageUri = await storage.SaveAsync(
                question.Id, $"page-{pageNo}-original.png", new MemoryStream(page.PngBytes), ct);

            db.QuestionVisualAssets.Add(new QuestionVisualAsset
            {
                QuestionId = question.Id,
                BookPageId = pageIdByNo.GetValueOrDefault(pageNo),
                StorageUri = storageUri,
                BoundingBoxJson = null,
                WidthPx = page.WidthPx,
                HeightPx = page.HeightPx,
                AssetHash = assetHash
            });
        }
    }
}
