using System.Security.Cryptography;
using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Application.Storage;
using MaarifPlatform.Application.Vision;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Vision;

public sealed record VisionAnalysisResult(
    VisionRoutingDecision Decision,
    VisualObservation? Observation,
    IReadOnlyList<VisualWarning> ValidationWarnings);

/// <summary>§3/§6 Vision analiz orkestrasyonu: routing kararı → (gerekirse) sayfa render →
/// asset cache/persist → provider çağrısı → deterministik doğrulama. Kendi <c>SaveChangesAsync</c>'ini
/// ÇALIŞTIRMAZ — entity'leri DbContext'e ekler, commit çağıranın (AnalysisOrchestrationService)
/// tek transaction'ına bırakılır. requires_visual=false ise hiçbir DB/PDF/Vision işlemi yapmadan
/// erken döner — mevcut metin-only akışı bu servisin varlığından etkilenmez.</summary>
public class VisionAnalysisService(
    MaarifDbContext db,
    IBookFileStorage storage,
    IPdfPageRenderer pageRenderer,
    IVisionRouter visionRouter,
    IVisionProvider visionProvider)
{
    public async Task<VisionAnalysisResult> AnalyzeAsync(Question question, QuestionDna originalDna, CancellationToken ct = default)
    {
        var decision = await visionRouter.DecideAsync(
            originalDna.OriginalQuestion ?? string.Empty, originalDna.OriginalVisualReference, ct);

        if (!decision.RequiresVisual || originalDna.SourcePage is null)
        {
            return new VisionAnalysisResult(decision, null, []);
        }

        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == question.BookId, ct)
            ?? throw new InvalidOperationException($"Soru için kitap bulunamadı: {question.BookId}");

        await using var pdfStream = await storage.OpenReadAsync(book.StorageUri, ct);
        var rendered = await pageRenderer.RenderPageAsync(pdfStream, originalDna.SourcePage.Value, ct);

        var assetHash = Convert.ToHexString(SHA256.HashData(rendered.PngBytes));

        // §26 cache: aynı görüntü bu soru için zaten kaydedilmişse tekrar diske yazma.
        // (Tam Vision SONUCU cache'i — aynı hash+provider+prompt_version — Phase 2 kapsamı.)
        var alreadyStored = await db.QuestionVisualAssets
            .AnyAsync(a => a.QuestionId == question.Id && a.AssetHash == assetHash, ct);

        if (!alreadyStored)
        {
            var storageUri = await storage.SaveAsync(
                question.Id, $"page-{originalDna.SourcePage}.png", new MemoryStream(rendered.PngBytes), ct);

            db.QuestionVisualAssets.Add(new QuestionVisualAsset
            {
                QuestionId = question.Id,
                BookPageId = question.BookPageId,
                StorageUri = storageUri,
                WidthPx = rendered.WidthPx,
                HeightPx = rendered.HeightPx,
                AssetHash = assetHash
            });
        }

        var observation = await visionProvider.AnalyzeQuestionImageAsync(
            rendered.PngBytes, originalDna.OriginalQuestion ?? string.Empty, ct);

        var validationWarnings = await visionProvider.ValidateVisualStructureAsync(observation, ct);

        db.AiRuns.Add(new AiRun
        {
            QuestionId = question.Id,
            Stage = PipelineStage.Vision,
            ModelTier = visionProvider.Name == "local-mock-vision" ? ModelTier.Cheap : ModelTier.Strong,
            Provider = observation.Usage.Provider,
            Model = observation.Usage.Model,
            InputTokens = observation.Usage.InputTokens,
            OutputTokens = observation.Usage.OutputTokens,
            CostUsd = observation.Usage.CostUsd,
            LatencyMs = observation.Usage.LatencyMs
        });

        return new VisionAnalysisResult(decision, observation, validationWarnings);
    }
}
