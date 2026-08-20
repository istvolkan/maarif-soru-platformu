namespace MaarifPlatform.Application.Extraction;

/// <summary>§10 QUESTION DETECTION. Bu sözleşmenin arkasında bugün deterministik bir heuristic
/// (bkz. Infrastructure/Extraction/HeuristicQuestionSegmenter) çalışıyor; §H model routing
/// tablosunda bu görev "ucuz model" katmanına atanmıştır — düşük güvenli sayfalarda ileride
/// bir LLM tabanlı implementasyonla değiştirilebilir/desteklenebilir, arayüz bunun için sabit tutulur.</summary>
public interface IQuestionSegmenter
{
    IReadOnlyList<QuestionBlock> Segment(IReadOnlyList<ExtractedPage> pages);
}
