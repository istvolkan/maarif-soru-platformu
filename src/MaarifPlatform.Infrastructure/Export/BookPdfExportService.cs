using System.Text.Json;
using MaarifPlatform.Application.Extraction;
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

public sealed record RevisionReportItem(
    int? QuestionNo, string OriginalQuestion, int Score, IReadOnlyList<string> Issues, string? RevisionSuggestion);

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
            // §16 doğrudan üretilen sorular (GenerationOrchestrationService.PersistGeneratedQuestionAsync)
            // hiç Transform'dan geçmez — Original* alanlarına yazılır, New* alanları hep null kalır
            // (bu bir veri bozukluğu değil, tasarım gereği: Transform yalnızca kitaptan çıkarılmış
            // soruları "yükseltir"). Bu export eskiden yalnızca New*'ı okuduğu için AiApproved/
            // EditorApproved durumuna gelmiş TÜM üretilmiş sorular sessizce atlanıyordu — PDF'te
            // başlık/cevap anahtarı/çözümler bölümleri neredeyse boş kalıyordu (gerçek kullanıcı
            // raporu, 2026-10). Artık New* yoksa Original*'a düşülür. DİKKAT: NewOptionsJson düz
            // string listesi iken OriginalOptionsJson (hem extraction hem generation'da) Label+Text
            // içeren OptionCandidate listesi — iki alan FARKLI ŞEKİLLİ, aynı List<string> ile
            // deserialize edilemez.
            var questionText = dna?.NewQuestion ?? dna?.OriginalQuestion;
            var correctAnswer = dna?.CorrectAnswer ?? dna?.OriginalAnswer;
            List<string>? options = dna?.NewOptionsJson is { } newOptionsJson
                ? JsonSerializer.Deserialize<List<string>>(newOptionsJson)
                : dna?.OriginalOptionsJson is { } originalOptionsJson
                    ? JsonSerializer.Deserialize<List<OptionCandidate>>(originalOptionsJson)?.Select(o => o.Text).ToList()
                    : null;
            if (dna is null || questionText is null || options is null || correctAnswer is null)
                continue;

            var correctIndex = options.FindIndex(o => o == correctAnswer);
            var correctLabel = correctIndex >= 0 && correctIndex < Labels.Length ? Labels[correctIndex] : "-";

            var question = await db.Questions.FirstAsync(q => q.Id == questionId, ct);

            byte[]? visualImage = null;
            // 2026-10 düzeltmesi: bu sorgu eskiden yalnızca BoundingBoxJson dolu (kırpılmış) bir
            // varlık ararken, VisionAnalysisService bir önceki sürümde kırpılmış asset üretmeyi
            // bırakmıştı (bkz. CaptureOriginalPageImagesAsync'in doc'u — artık TEK varlık hep tam
            // sayfa, BoundingBoxJson=null) — bu yüzden filtre HİÇBİR ZAMAN eşleşmiyordu ve PDF'te
            // görsel gerektiren sorularda bile görsel hiç basılmıyordu. Artık BoundingBoxJson'a
            // bakılmaksızın en son varlık alınır; yalnızca gerçekten görsele ihtiyacı olan sorularda
            // (RequiresVisual) basılır — aksi halde her soruda (ihtiyacı olmasa bile) kaynak sayfanın
            // tam görüntüsü dolup PDF'i anlamsızca şişirirdi.
            if (dna.RequiresVisual)
            {
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
            }

            questions.Add(new ExportableQuestion(question.QuestionNo, questionText, options, correctLabel, dna.Solution, visualImage));
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
                                // Kırpılmış bir şekil değil, kaynak PDF sayfasının TAMAMI (bkz.
                                // GenerateAsync'teki not) — bu yüzden okunabilir kalması için biraz
                                // daha geniş basılır ve ne olduğu açıkça belirtilir.
                                qCol.Item().PaddingLeft(15).PaddingTop(4).Text("Kaynak sayfa görüntüsü:").FontSize(9).Italic();
                                qCol.Item().PaddingLeft(15).MaxWidth(420).Image(q.VisualImage);
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

    private const int RevisionScoreThreshold = 50;

    /// <summary>ManualReviewRequired'a düşmüş ve Maarif Uyum Puanı 50'nin altında kalmış sorular
    /// için ayrı bir rapor — bu sorular BookBatchTransformService.ProcessManualReviewAsync
    /// tarafından otomatik Transform'a gönderilmez (puan güvenilecek kadar sağlam değil), bu
    /// yüzden editörün elle işlemesi için önerilen düzeltmeyle birlikte burada listelenir.</summary>
    public async Task<byte[]> GenerateRevisionReportAsync(Guid bookId, CancellationToken ct = default)
    {
        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == bookId, ct)
            ?? throw new InvalidOperationException($"Kitap bulunamadı: {bookId}");

        var reviewQuestionIds = await db.Questions
            .Where(q => q.BookId == bookId && q.Status == QuestionStatus.ManualReviewRequired)
            .OrderBy(q => q.QuestionNo)
            .Select(q => q.Id)
            .ToListAsync(ct);

        var items = new List<RevisionReportItem>();
        foreach (var questionId in reviewQuestionIds)
        {
            var version = await db.QuestionVersions
                .Include(v => v.Dna)
                .Where(v => v.QuestionId == questionId && v.Stage == QuestionVersionStage.Analyzed)
                .OrderByDescending(v => v.VersionNo)
                .FirstOrDefaultAsync(ct);

            var dna = version?.Dna;
            if (dna is null)
                continue;

            var score = dna.MaarifAlignmentScore ?? 0;
            // BookBatchTransformService.ProcessManualReviewAsync ile aynı "otomatik dönüştürmeye
            // uygun mu" kuralı: puan eşiğin altındaysa VEYA TransformationLevel critical gate
            // ihlalini işaret ediyorsa (ManualReviewRequired) bu soru burada listelenir — aksi
            // halde ne dönüştürülmüş ne de raporlanmış, editöre görünmez kalırdı.
            if (score >= RevisionScoreThreshold && dna.TransformationLevel != TransformationLevel.ManualReviewRequired)
                continue;

            var question = await db.Questions.FirstAsync(q => q.Id == questionId, ct);
            var issues = string.IsNullOrWhiteSpace(dna.AlignmentIssuesJson)
                ? []
                : JsonSerializer.Deserialize<List<string>>(dna.AlignmentIssuesJson) ?? [];

            string? suggestion = null;
            if (!string.IsNullOrWhiteSpace(dna.ExtensionsJson))
            {
                var extensions = JsonSerializer.Deserialize<Dictionary<string, string>>(dna.ExtensionsJson);
                extensions?.TryGetValue("revisionSuggestion", out suggestion);
            }

            items.Add(new RevisionReportItem(question.QuestionNo, dna.OriginalQuestion ?? "", score, issues, suggestion));
        }

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(11).DisableFontFeature(QuestPDF.Helpers.FontFeatures.StandardLigatures));

                page.Header().Column(col =>
                {
                    col.Item().Text(book.Title).FontSize(18).Bold();
                    col.Item().Text($"Revizyon Raporu — Maarif Uyum Puanı {RevisionScoreThreshold} Altı Sorular").FontSize(13);
                    col.Item().PaddingTop(4).LineHorizontal(1);
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    if (items.Count == 0)
                    {
                        col.Item().Text($"Bu kitapta {RevisionScoreThreshold} puan altında incelemeye düşmüş soru yok.");
                    }

                    foreach (var item in items)
                    {
                        col.Item().PaddingBottom(16).Column(qCol =>
                        {
                            qCol.Item().Text($"{item.QuestionNo}. {item.OriginalQuestion}").Bold();
                            qCol.Item().PaddingTop(2).Text($"Maarif Uyum Puanı: {item.Score}").FontColor(Colors.Red.Darken2);

                            if (item.Issues.Count > 0)
                            {
                                qCol.Item().PaddingTop(4).Text("Tespit Edilen Sorunlar:").Bold();
                                foreach (var issue in item.Issues)
                                {
                                    qCol.Item().PaddingLeft(15).Text($"• {issue}");
                                }
                            }

                            qCol.Item().PaddingTop(4).Text("Revizyon Önerisi:").Bold();
                            qCol.Item().PaddingLeft(15).Text(item.RevisionSuggestion ?? "(henüz oluşturulmadı — Kitap sayfasından \"İncelemedekileri İşle\"yi çalıştırın)");
                        });
                    }
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
