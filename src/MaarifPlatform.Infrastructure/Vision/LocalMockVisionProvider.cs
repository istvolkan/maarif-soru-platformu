using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Vision;

namespace MaarifPlatform.Infrastructure.Vision;

/// <summary>Dış API anahtarı GEREKTİRMEZ. Yalnızca Vision pipeline'ının mekaniğini (rasterizasyon →
/// provider çağrısı → VisualObservation kaydı) doğrulamak içindir. GERÇEK GÖRSEL ANALİZ YAPMAZ —
/// her çağrıda confidence=0 ve açık bir MOCK uyarısı döner, bu yüzden downstream mantık
/// (AnalysisOrchestrationService) bu sağlayıcıyla üretilen sonuçları asla "Analyzed" olarak
/// kapatmamalı, her zaman ManualReviewRequired'a düşürmelidir.</summary>
public class LocalMockVisionProvider : IVisionProvider
{
    public string Name => "local-mock-vision";

    public Task<VisualObservation> AnalyzePageAsync(byte[] pageImagePng, CancellationToken ct = default) =>
        Task.FromResult(BuildMockObservation("page", pageImagePng.Length));

    public Task<VisualObservation> AnalyzeQuestionImageAsync(byte[] questionImagePng, string questionText, CancellationToken ct = default) =>
        Task.FromResult(BuildMockObservation("question_image", questionImagePng.Length));

    public Task<VisualObservation> ExtractVisualStructureAsync(byte[] imagePng, string visualType, CancellationToken ct = default) =>
        Task.FromResult(BuildMockObservation(visualType, imagePng.Length));

    public Task<IReadOnlyList<VisualWarning>> ValidateVisualStructureAsync(VisualObservation observation, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<VisualWarning>>(
        [
            new VisualWarning("MOCK_NO_REAL_VALIDATION", "[MOCK] LocalMockVisionProvider gerçek doğrulama yapmaz.", 1.0m)
        ]);

    /// <summary>Gerçek transkripsiyon yapmaz — yalnızca ExtractWithVisionAsync borusunun mekaniğini
    /// (sayfa render → provider çağrısı → Question/DNA kaydı) ücretsiz doğrulamak için tek, açıkça
    /// işaretli bir [MOCK] soru bloğu döner.</summary>
    public Task<PageTranscriptionResult> TranscribePageAsync(byte[] pageImagePng, int pageNo, CancellationToken ct = default)
    {
        var block = new TranscribedQuestionBlock(
            QuestionNo: null,
            Stem: $"[MOCK] Sayfa {pageNo} ({pageImagePng.Length} bayt) — gerçek transkripsiyon yapılmadı.",
            Options: [],
            CorrectAnswer: null,
            HasVisual: false,
            VisualDescription: null);

        return Task.FromResult(new PageTranscriptionResult(
            [block], new AiUsage("local-mock-vision", "mock-v1", 0, 0, 0m, 1)));
    }

    private static VisualObservation BuildMockObservation(string visualType, int imageByteCount)
    {
        var warning = new VisualWarning(
            "MOCK_NO_REAL_VISION",
            "[MOCK] LocalMockVisionProvider gerçek görsel analiz yapmaz — yalnızca Vision pipeline " +
            "mekaniğini doğrulamak içindir. Bu sonucu asla nihai kabul etme.",
            1.0m);

        return new VisualObservation(
            VisualType: visualType,
            Description: $"[MOCK] {imageByteCount} bayt boyutunda görüntü alındı, gerçek içerik analiz edilmedi.",
            Confidence: 0m,
            Elements: [],
            Relations: [],
            VisualText: [],
            Symbols: [],
            Measurements: [],
            Warnings: [warning],
            Usage: new AiUsage("local-mock-vision", "mock-v1", 0, 0, 0m, 1));
    }
}
