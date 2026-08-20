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

        foreach (var block in blocks)
        {
            var question = new Question
            {
                BookId = bookId,
                BookPageId = pageIdByNo.GetValueOrDefault(block.PageNo),
                QuestionNo = block.QuestionNo,
                Status = QuestionStatus.Extracted
            };

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

        return new BookExtractionResult(pages.Count, blocks.Count, lowConfidenceCount);
    }
}
