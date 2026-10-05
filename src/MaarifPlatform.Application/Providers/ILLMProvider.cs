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

    /// <summary>§60 LLM-B: Question DNA Analysis — AnalyzeQuestionAsync'ten (LLM-A, curriculum
    /// eşleme/rubrik) BİLİNÇLİ OLARAK AYRI bir rol (bkz. AnalyzeQuestionDnaRequest doc).</summary>
    Task<AnalyzeQuestionDnaResult> AnalyzeQuestionDnaAsync(AnalyzeQuestionDnaRequest request, CancellationToken ct = default);

    Task<TransformQuestionResult> TransformQuestionAsync(TransformQuestionRequest request, CancellationToken ct = default);

    Task<EvaluateQuestionResult> EvaluateQuestionAsync(EvaluateQuestionRequest request, CancellationToken ct = default);

    Task<GenerateQuestionResult> GenerateQuestionAsync(GenerateQuestionRequest request, CancellationToken ct = default);

    Task<RecommendRevisionResult> RecommendRevisionAsync(RecommendRevisionRequest request, CancellationToken ct = default);

    Task<ExtractCurriculumResult> ExtractCurriculumStructureAsync(ExtractCurriculumRequest request, CancellationToken ct = default);

    Task<CurriculumAlignmentResult> ValidateCurriculumAlignmentAsync(ValidateCurriculumAlignmentRequest request, CancellationToken ct = default);

    /// <summary>§61 Independent Solver — soruyu Generator'ın cevabından BAĞIMSIZ olarak sıfırdan
    /// çözer (bkz. SolveQuestionRequest'teki tasarım notu). Yalnızca şıklı sorularda anlamlıdır.</summary>
    Task<SolveQuestionResult> SolveQuestionAsync(SolveQuestionRequest request, CancellationToken ct = default);

    /// <summary>Soru Çeşitlendir — Maarif Modeli müfredat doğrulaması gerektirmeyen, kullanıcının
    /// sağladığı örnek sorudan küçük mantıksal değişikliklerle çoğaltma.</summary>
    Task<VaryQuestionResult> VaryQuestionAsync(VaryQuestionRequest request, CancellationToken ct = default);
}
