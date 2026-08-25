using System.Security.Cryptography;
using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Application.Storage;
using MaarifPlatform.Application.Vision;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Infrastructure.Vision;

public sealed record VisionAnalysisResult(
    VisionRoutingDecision Decision,
    VisualObservation? Observation,
    IReadOnlyList<VisualWarning> ValidationWarnings);

/// <summary>§3/§6/§9/§10 Vision analiz orkestrasyonu: routing kararı → (gerekirse) sayfa render →
/// asset cache/persist → birincil provider çağrısı → deterministik doğrulama → (düşük güvende)
/// ikincil provider ile konsensüs kontrolü. Kendi <c>SaveChangesAsync</c>'ini ÇALIŞTIRMAZ —
/// entity'leri DbContext'e ekler, commit çağıranın (AnalysisOrchestrationService) tek
/// transaction'ına bırakılır. requires_visual=false ise hiçbir DB/PDF/Vision işlemi yapmadan
/// erken döner — mevcut metin-only akışı bu servisin varlığından etkilenmez.
/// SecondaryProvider config'te boşsa consensus akışı tamamen devre dışıdır (ek maliyet yok).</summary>
public class VisionAnalysisService(
    MaarifDbContext db,
    IBookFileStorage storage,
    IPdfPageRenderer pageRenderer,
    IVisionRouter visionRouter,
    IVisionProviderFactory providerFactory,
    IOptionsMonitor<VisionRoutingOptions> routingOptions)
{
    public async Task<VisionAnalysisResult> AnalyzeAsync(Question question, QuestionDna originalDna, CancellationToken ct = default)
    {
        // Sprint 11: her çağrıda taze okunur (IOptionsMonitor.CurrentValue) — Admin Ayarlar
        // ekranından değiştirilen Vision:Provider/SecondaryProvider yeniden başlatma
        // gerektirmeden etkili olur.
        var routing = routingOptions.CurrentValue;

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

        var primaryProvider = providerFactory.Get(routing.Provider);
        var primaryObservation = await primaryProvider.AnalyzeQuestionImageAsync(
            rendered.PngBytes, originalDna.OriginalQuestion ?? string.Empty, ct);
        var validationWarnings = (await primaryProvider.ValidateVisualStructureAsync(primaryObservation, ct)).ToList();

        RecordVisionRun(question.Id, primaryProvider.Name, primaryObservation.Usage);

        // §9/§10: yalnızca birincil güven eşiğin altındaysa VE bir ikincil sağlayıcı
        // yapılandırılmışsa ikinci bir Vision çağrısı yapılır — her soru için otomatik
        // çift-provider çalıştırmak maliyeti gereksiz büyütür.
        if (!string.IsNullOrWhiteSpace(routing.SecondaryProvider)
            && primaryObservation.Confidence < routing.ConsensusConfidenceThreshold)
        {
            var secondaryProvider = providerFactory.Get(routing.SecondaryProvider);
            var secondaryObservation = await secondaryProvider.AnalyzeQuestionImageAsync(
                rendered.PngBytes, originalDna.OriginalQuestion ?? string.Empty, ct);

            RecordVisionRun(question.Id, secondaryProvider.Name, secondaryObservation.Usage);

            validationWarnings.AddRange(VisualConsensusChecker.Compare(primaryObservation, secondaryObservation));
        }

        return new VisionAnalysisResult(decision, primaryObservation, validationWarnings);
    }

    private void RecordVisionRun(Guid questionId, string providerName, Application.Providers.AiUsage usage)
    {
        db.AiRuns.Add(new AiRun
        {
            QuestionId = questionId,
            Stage = PipelineStage.Vision,
            ModelTier = providerName == "local-mock-vision" ? ModelTier.Cheap : ModelTier.Strong,
            Provider = usage.Provider,
            Model = usage.Model,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CostUsd = usage.CostUsd,
            LatencyMs = usage.LatencyMs
        });
    }
}
