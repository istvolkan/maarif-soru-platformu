using System.Text.Json;
using MaarifPlatform.Application.Storage;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MaarifPlatform.Infrastructure.Export;

public sealed record ExportableQuestion(
    int? QuestionNo, string Question, IReadOnlyList<string> Options, string CorrectLabel, string? Solution, byte[]? VisualImage);

/// <summary>Kitaptaki AiApproved/EditorApproved/Published sorulardan yeni bir soru kitabı PDF'i
/// üretir — İncelemeye Gönderilmiş (ManualReviewRequired) veya henüz dönüştürülmemiş sorular
/// dahil edilmez (bkz. BookBatchTransformService). Üç bölüm: sorular (varsa görselleriyle),
/// cevap anahtarı, çözümler — tipik Türk soru bankası formatı.</summary>
public class BookPdfExportService(MaarifDbContext db, IBookFileStorage storage)
{
    private static readonly string[] Labels = ["A", "B", "C", "D", "E", "F"];

    public async Task<byte[]> GenerateAsync(Guid bookId, CancellationToken ct = default)
    {
        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == bookId, ct)
            ?? throw new InvalidOperationException($"Kitap bulunamadı: {bookId}");

        var approvedQuestionIds = await db.Questions
            .Where(q => q.BookId == bookId && (
                q.Status == QuestionStatus.AiApproved ||
                q.Status == QuestionStatus.EditorApproved ||
                q.Status == QuestionStatus.Published))
            .OrderBy(q => q.QuestionNo)
            .Select(q => q.Id)
            .ToListAsync(ct);

        var questions = new List<ExportableQuestion>();
        foreach (var questionId in approvedQuestionIds)
        {
            var version = await db.QuestionVersions
                .Include(v => v.Dna)
                .Where(v => v.QuestionId == questionId)
                .OrderByDescending(v => v.VersionNo)
                .FirstOrDefaultAsync(ct);

            var dna = version?.Dna;
            if (dna?.NewQuestion is null || dna.NewOptionsJson is null || dna.CorrectAnswer is null)
                continue;

            var options = JsonSerializer.Deserialize<List<string>>(dna.NewOptionsJson) ?? [];
            var correctIndex = options.FindIndex(o => o == dna.CorrectAnswer);
            var correctLabel = correctIndex >= 0 && correctIndex < Labels.Length ? Labels[correctIndex] : "-";

            var question = await db.Questions.FirstAsync(q => q.Id == questionId, ct);

            byte[]? visualImage = null;
            var visualAsset = await db.QuestionVisualAssets
                .Where(a => a.QuestionId == questionId)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (visualAsset is not null)
            {
                await using var visualStream = await storage.OpenReadAsync(visualAsset.StorageUri, ct);
                using var buffer = new MemoryStream();
                await visualStream.CopyToAsync(buffer, ct);
                visualImage = buffer.ToArray();
            }

            questions.Add(new ExportableQuestion(question.QuestionNo, dna.NewQuestion, options, correctLabel, dna.Solution, visualImage));
        }

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                // "ti" gibi harf ikilileri PDF'in metin katmanında kayboluyordu (kopyalama/arama
                // bozuluyordu) — varsayılan fontun standart ligature'ları aktif olmasından kaynaklanıyor.
                page.DefaultTextStyle(x => x.FontSize(11).DisableFontFeature(QuestPDF.Helpers.FontFeatures.StandardLigatures));

                page.Header().Column(col =>
                {
                    col.Item().Text(book.Title).FontSize(18).Bold();
                    col.Item().Text($"{(book.Grade is null ? "" : $"{book.Grade}. Sınıf")} {book.Subject}".Trim());
                    col.Item().PaddingTop(4).LineHorizontal(1);
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    foreach (var q in questions)
                    {
                        col.Item().PaddingBottom(14).Column(qCol =>
                        {
                            qCol.Item().Text($"{q.QuestionNo}. {q.Question}").Bold();
                            if (q.VisualImage is not null)
                            {
                                qCol.Item().PaddingLeft(15).PaddingTop(4).MaxWidth(300).Image(q.VisualImage);
                            }
                            for (var i = 0; i < q.Options.Count; i++)
                            {
                                qCol.Item().PaddingLeft(15).Text($"{Labels[i]}) {q.Options[i]}");
                            }
                        });
                    }

                    col.Item().PageBreak();
                    col.Item().Text("Cevap Anahtarı").FontSize(16).Bold();
                    col.Item().PaddingTop(8).Column(answerCol =>
                    {
                        foreach (var q in questions)
                        {
                            answerCol.Item().Text($"{q.QuestionNo}. {q.CorrectLabel}");
                        }
                    });

                    col.Item().PageBreak();
                    col.Item().Text("Çözümler").FontSize(16).Bold();
                    col.Item().PaddingTop(8).Column(solutionCol =>
                    {
                        foreach (var q in questions.Where(x => !string.IsNullOrWhiteSpace(x.Solution)))
                        {
                            solutionCol.Item().PaddingBottom(10).Column(sCol =>
                            {
                                sCol.Item().Text($"{q.QuestionNo}.").Bold();
                                sCol.Item().PaddingLeft(15).Text(q.Solution);
                            });
                        }
                    });
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }
}
