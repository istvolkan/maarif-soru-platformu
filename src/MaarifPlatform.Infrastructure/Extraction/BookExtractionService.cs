using System.Security.Cryptography;
using System.Text.Json;
using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Application.Storage;
using MaarifPlatform.Application.Vision;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Infrastructure.Vision;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Infrastructure.Extraction;

/// <summary>Bir sayfadan persist edilecek TEK normalize soru bloğu — ister ham metin + regex
/// heuristic'inden (IQuestionSegmenter), ister Vision transkripsiyonundan (IVisionProvider.
/// TranscribePageAsync) gelsin, kalıcılaştırma adımı ikisini de AYNI şekilde işler.</summary>
internal sealed record NormalizedBlock(
    int? QuestionNo,
    int PageNo,
    string Stem,
    IReadOnlyList<OptionCandidate> Options,
    string? CorrectAnswer,
    bool HasVisual,
    string? VisualDescription,
    bool IsLowConfidence,
    string Source,
    object RawPayload);

/// <summary>§10 PDF İşleme ana orkestrasyonu: PDF → sayfa → soru bloğu → Question DNA (§D) → DB.
/// DbContext'e doğrudan bağımlı olduğu için (repository soyutlaması bu ölçekte gereksiz
/// bir katman olurdu) bilinçli olarak Infrastructure'da tutulur; Api sadece bunu çağırır.
///
/// Tek bir giriş noktası vardır (ExtractAsync) — kullanıcıya "hangi yöntem" diye bir seçim
/// SUNULMAZ (2026-10 kullanıcı talebi: "kullanıcıya bırakma bu tarz konuları"). Karar otomatik ve
/// sayfa bazlıdır: önce ücretsiz ham metin + regex heuristic (IQuestionSegmenter) çalışır; bir
/// sayfadaki herhangi bir blok "görsele muhtaç" görünüyorsa (mevcut HeuristicVisionRouter'ın aynı
/// anahtar-kelime/referans mantığıyla) VEYA heuristic düşük güvenle işaretlediyse (segmentasyon
/// bozuk/eksik), YALNIZCA o sayfa yeniden, gerçek görüntüsü üzerinden bir Vision modeline
/// (IVisionProvider.TranscribePageAsync) okutulur ve o sayfanın blokları vision sonucuyla
/// DEĞİŞTİRİLİR. Matematiksel gösterim (kesir/üs) ve diyagram içeren sayfalarda ham metin çıkarımı
/// sistematik olarak bozuluyordu (2026-10-02 teşhisi: bir cevap anahtarı sayfası soru sanılmış, bir
/// geometri sorusunun metni yalnızca diyagramdaki nokta etiketlerine indirgenmişti) — bu otomatik
/// yükseltme bunu, her sayfa için gereksiz yere Vision'a gitmeden (maliyet) kökten çözer.</summary>
public class BookExtractionService(
    MaarifDbContext db,
    IBookFileStorage storage,
    IPdfTextExtractor textExtractor,
    IPdfPageRenderer pageRenderer,
    IQuestionSegmenter segmenter,
    IVisionRouter visionRouter,
    IVisionProviderFactory visionProviderFactory,
    IOptionsMonitor<VisionRoutingOptions> visionRouting)
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

        var heuristicBlocks = segmenter.Segment(pages).ToList();
        var blocksByPage = heuristicBlocks
            .GroupBy(b => b.PageNo)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<NormalizedBlock>)g.Select(ToNormalized).ToList());

        var escalatedPageNumbers = new List<int>();
        foreach (var pageGroup in heuristicBlocks.GroupBy(b => b.PageNo))
        {
            var needsVision = false;
            foreach (var block in pageGroup)
            {
                var decision = await visionRouter.DecideAsync(block.Stem, null, ct);

                // HeuristicVisionRouter yalnızca belirli Türkçe tetikleyici KALIPLARI (örn.
                // "çemberinde") tam eşleştirir — bir diyagramın etiketlerinin tek tek satırlara
                // dağılmış, "çemberin üzerinde" gibi farklı bir çekim eki alan gerçek bir sayfa, bu
                // kalıp eşleşmese bile anahtar-kelimesiz kaçabilir (2026-10-05 teşhisi: sayfa 3'teki
                // "d/¬/E/F/L/KABCD" gibi tek-karakterlik satır dökümü hiçbir kalıba uymadı). Bu yüzden
                // içerik-bağımsız, ücretsiz bir ikinci sinyal de eklenir: satırların büyük kısmı çok
                // kısaysa (diyagram etiketi dökümüne işaret eder) sayfa yine de yükseltilir.
                if (decision.RequiresVisual || block.IsLowConfidence || HasManyShortLines(block.RawBlockText))
                {
                    needsVision = true;
                    break;
                }
            }

            if (needsVision)
            {
                escalatedPageNumbers.Add(pageGroup.Key);
            }
        }

        if (escalatedPageNumbers.Count > 0)
        {
            var visionProvider = visionProviderFactory.Get(visionRouting.CurrentValue.Provider);

            IReadOnlyList<RenderedPage> escalatedRendered;
            await using (var renderStream = await storage.OpenReadAsync(book.StorageUri, ct))
            {
                escalatedRendered = await pageRenderer.RenderPagesAsync(renderStream, escalatedPageNumbers, ct);
            }

            foreach (var page in escalatedRendered)
            {
                var transcription = await visionProvider.TranscribePageAsync(page.PngBytes, page.PageNo, ct);

                // Transkripsiyon boş dönerse (örn. sayfa gerçekten sorusuzsa) ORİJİNAL heuristic
                // bloklarını koru — hiçbir şeye dönüştürmek, var olanı kaybetmekten kötüdür.
                if (transcription.Questions.Count > 0)
                {
                    blocksByPage[page.PageNo] = transcription.Questions
                        .Select(t => ToNormalized(t, page.PageNo, visionProvider.Name))
                        .ToList();
                }
            }
        }

        var lowConfidenceCount = 0;
        var totalBlocks = 0;
        var questionsByPage = new List<(Question Question, int PageNo)>();

        foreach (var pageNo in blocksByPage.Keys.OrderBy(p => p))
        {
            foreach (var block in blocksByPage[pageNo])
            {
                totalBlocks++;
                var question = new Question
                {
                    BookId = bookId,
                    BookPageId = pageIdByNo.GetValueOrDefault(pageNo),
                    QuestionNo = block.QuestionNo,
                    Status = QuestionStatus.Extracted
                };
                questionsByPage.Add((question, pageNo));

                var version = new QuestionVersion
                {
                    Question = question,
                    QuestionId = question.Id,
                    VersionNo = 1,
                    Stage = QuestionVersionStage.Original,
                    PayloadJson = JsonSerializer.Serialize(block.RawPayload),
                    CreatedBy = block.Source
                };

                if (block.IsLowConfidence)
                {
                    lowConfidenceCount++;
                }

                var dna = new QuestionDna
                {
                    QuestionVersion = version,
                    QuestionVersionId = version.Id,
                    SourceBook = book.Title,
                    SourcePage = pageNo,
                    Grade = book.Grade,
                    Subject = book.Subject,
                    OriginalQuestion = block.Stem,
                    OriginalOptionsJson = JsonSerializer.Serialize(block.Options),
                    OriginalAnswer = block.CorrectAnswer,
                    OriginalVisualReference = block.HasVisual ? block.VisualDescription : null,
                    DnaSchemaVersion = "1.0",
                    QualityFlagsJson = JsonSerializer.Serialize(block.IsLowConfidence
                        ? [$"low_confidence_{block.Source}"]
                        : Array.Empty<string>()),
                    EditorRequired = block.IsLowConfidence,
                    RequiresVisual = block.HasVisual
                };

                db.Questions.Add(question);
                db.QuestionVersions.Add(version);
                db.QuestionDnas.Add(dna);
            }
        }

        book.TotalPages = pages.Count;
        await db.SaveChangesAsync(ct);

        await CaptureOriginalPageImagesAsync(book, questionsByPage, ct);
        await db.SaveChangesAsync(ct);

        return new BookExtractionResult(pages.Count, totalBlocks, lowConfidenceCount);
    }

    /// <summary>İçerik-bağımsız, ücretsiz bir escalation sinyali: bir diyagramın nokta/köşe
    /// etiketleri ham metin çıkarımında genellikle tek tek satırlara düşer (bkz. ExtractAsync'teki
    /// doc). Satırların yarısından fazlası ≤3 karakterse (ve en az birkaç satır varsa — tek satırlık
    /// kısa bir soru kökü yanlış pozitif vermemeli) bu durumun belirtisi sayılır.</summary>
    private static bool HasManyShortLines(string rawBlockText)
    {
        var lines = rawBlockText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count < 4)
        {
            return false;
        }

        var shortLineCount = lines.Count(l => l.Length <= 3);
        return shortLineCount >= lines.Count / 2;
    }

    private static NormalizedBlock ToNormalized(QuestionBlock block) => new(
        block.QuestionNo, block.PageNo, block.Stem, block.Options,
        CorrectAnswer: null, HasVisual: false, VisualDescription: null,
        IsLowConfidence: block.IsLowConfidence, Source: "extraction-pipeline", RawPayload: block);

    private static NormalizedBlock ToNormalized(TranscribedQuestionBlock block, int pageNo, string providerName)
    {
        var isLowConfidence = string.IsNullOrWhiteSpace(block.Stem)
            || (block.HasVisual && string.IsNullOrWhiteSpace(block.VisualDescription));

        return new NormalizedBlock(
            block.QuestionNo, pageNo, block.Stem, block.Options,
            block.CorrectAnswer, block.HasVisual, block.VisualDescription,
            isLowConfidence, $"vision-extraction-pipeline:{providerName}", block);
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
