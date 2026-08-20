namespace MaarifPlatform.Infrastructure.Extraction;

public sealed record BookExtractionResult(int PagesExtracted, int QuestionsDetected, int LowConfidenceCount);
