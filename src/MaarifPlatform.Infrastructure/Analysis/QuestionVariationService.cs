using System.Text;
using System.Text.Json;
using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Vision;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Infrastructure.Ai;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Infrastructure.Vision;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Infrastructure.Analysis;

/// <summary>Soru Çeşitlendir — GenerationOrchestrationService'in YANINA eklenir, onu değiştirmez.
/// Kasıtlı olarak çok daha ince bir akış: RAG/grounding, placeholder Book, curriculum doğrulaması
/// YOK (bkz. ILLMProvider.VaryQuestionAsync'in doc'u). Sonuç curriculum-validated Soru Havuzu'ndan
/// ayrı bir havuza (QuestionVariationBatch/Item) kaydedilir.
///
/// 2026-10: kaynak soru serbest metin yerine (veya onunla birlikte) bir GÖRSEL ya da PDF olarak da
/// sağlanabilir — bu durumda kaynak içerik, extraction'daki TranscribePageAsync ile AYNI Vision
/// yeteneği (Vision:Provider — Gemini/Anthropic/Local) kullanılarak okunur. Kasıtlı bir yan fayda:
/// bu, yöneticinin Vision:Provider yapılandırmasını (özellikle Gemini'yi) gerçek bir görselle,
/// tüm kitap extraction borusunu çalıştırmadan hızlıca test edebilmesini sağlar.</summary>
public sealed class QuestionVariationService(
    MaarifDbContext db,
    ILLMProviderFactory providerFactory,
    IOptionsMonitor<AiRoutingOptions> aiRouting,
    IPdfPageRenderer pageRenderer,
    IVisionProviderFactory visionProviderFactory,
    IOptionsMonitor<VisionRoutingOptions> visionRouting)
{
    private const string PdfContentType = "application/pdf";

    public async Task<QuestionVariationBatch> GenerateAsync(
        string sourceQuestionText,
        int count,
        Guid? actingUserId,
        byte[]? sourceImageBytes = null,
        string? sourceImageContentType = null,
        CancellationToken ct = default)
    {
        var effectiveSourceText = sourceQuestionText;
        string? visionProviderName = null;
        string? visionModelName = null;
        decimal? visionCostUsd = null;

        if (sourceImageBytes is not null)
        {
            var pngBytes = string.Equals(sourceImageContentType, PdfContentType, StringComparison.OrdinalIgnoreCase)
                ? (await pageRenderer.RenderPageAsync(new MemoryStream(sourceImageBytes), pageNo: 1, ct)).PngBytes
                : sourceImageBytes; // PNG/JPEG olduğu gibi gönderilir — Vision sağlayıcıları şu an yalnızca PNG varsayar.

            var visionProvider = visionProviderFactory.Get(visionRouting.CurrentValue.Provider);
            var transcription = await visionProvider.TranscribePageAsync(pngBytes, pageNo: 1, ct);
            var transcribedText = BuildTextFromTranscription(transcription);

            visionProviderName = transcription.Usage.Provider;
            visionModelName = transcription.Usage.Model;
            visionCostUsd = transcription.Usage.CostUsd;

            effectiveSourceText = string.IsNullOrWhiteSpace(sourceQuestionText)
                ? transcribedText
                : $"{transcribedText}\n\nEK TALİMAT: {sourceQuestionText}";
        }

        // Sprint 11 ile aynı neden: Ayarlar'dan değiştirilen Ai:Provider yeniden başlatma
        // gerektirmeden etkili olsun.
        var llmProvider = providerFactory.Get(aiRouting.CurrentValue.Provider);

        var result = await llmProvider.VaryQuestionAsync(new VaryQuestionRequest(effectiveSourceText, count), ct);

        var batch = new QuestionVariationBatch
        {
            SourceQuestionText = effectiveSourceText,
            VisionProvider = visionProviderName,
            VisionModel = visionModelName,
            VisionCostUsd = visionCostUsd,
            RequestedCount = count,
            CreatedByUserId = actingUserId,
            Provider = result.Usage.Provider,
            Model = result.Usage.Model,
            InputTokens = result.Usage.InputTokens,
            OutputTokens = result.Usage.OutputTokens,
            CostUsd = result.Usage.CostUsd,
            LatencyMs = result.Usage.LatencyMs,
        };

        var orderNo = 1;
        foreach (var variant in result.Variations)
        {
            batch.Items.Add(new QuestionVariationItem
            {
                BatchId = batch.Id,
                OrderNo = orderNo++,
                QuestionText = variant.Question,
                OptionsJson = JsonSerializer.Serialize(variant.Options),
                CorrectAnswer = variant.CorrectAnswer,
                Solution = variant.Solution,
            });
        }

        db.QuestionVariationBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        return batch;
    }

    private static string BuildTextFromTranscription(PageTranscriptionResult transcription)
    {
        var block = transcription.Questions.FirstOrDefault()
            ?? throw new InvalidOperationException("Görselde/PDF'te bir soru tespit edilemedi.");

        var sb = new StringBuilder();
        sb.AppendLine(block.Stem);

        if (block.Options.Count > 0)
        {
            sb.AppendLine();
            foreach (var option in block.Options)
            {
                sb.AppendLine($"{option.Label}) {option.Text}");
            }
        }

        if (!string.IsNullOrWhiteSpace(block.CorrectAnswer))
        {
            sb.AppendLine();
            sb.AppendLine($"Doğru cevap: {block.CorrectAnswer}");
        }

        if (block.HasVisual && !string.IsNullOrWhiteSpace(block.VisualDescription))
        {
            sb.AppendLine();
            sb.AppendLine($"Görsel: {block.VisualDescription}");
        }

        return sb.ToString().Trim();
    }
}
