namespace MaarifPlatform.Application.Providers;

/// <summary>§11 Provider Independent Architecture. Somut sağlayıcılar (OpenAI/Anthropic/Gemini)
/// Infrastructure katmanında bu sözleşmeyi implemente eder; Application ve üstü hiçbir katman
/// belirli bir sağlayıcıya bağımlı olmaz. Hangi implementasyonun hangi görevde kullanılacağı
/// §H Model Routing kurallarınca (konfigürasyon üzerinden) belirlenir — bu arayüz sadece
/// "bir görevi yerine getirebilen sağlayıcı" sözleşmesidir, katman sırasına karar vermez.</summary>
public interface ILLMProvider
{
    /// <summary>Sağlayıcı adı (routing konfigürasyonuyla eşleştirmek için, örn. "openai", "anthropic").</summary>
    string Name { get; }

    Task<AnalyzeQuestionResult> AnalyzeQuestionAsync(AnalyzeQuestionRequest request, CancellationToken ct = default);

    Task<TransformQuestionResult> TransformQuestionAsync(TransformQuestionRequest request, CancellationToken ct = default);

    Task<EvaluateQuestionResult> EvaluateQuestionAsync(EvaluateQuestionRequest request, CancellationToken ct = default);

    Task<GenerateQuestionResult> GenerateQuestionAsync(GenerateQuestionRequest request, CancellationToken ct = default);
}
