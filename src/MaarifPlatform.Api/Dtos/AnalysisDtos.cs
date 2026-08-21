namespace MaarifPlatform.Api.Dtos;

public record AnalysisSummaryResponse(
    int WeightedScore,
    string TransformationLevel,
    bool ManualReviewRequired,
    int GroundingChunksUsed,
    bool RequiresVisual,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    int LatencyMs);

public record AlignmentScoreResponse(
    string Criterion,
    decimal Score,
    decimal Weight,
    string Explanation,
    string? SourceRef,
    bool IsCriticalGate);

public record QuestionDetailResponse(
    Guid Id,
    int? QuestionNo,
    string Status,
    string? MathematicalCore,
    string? LearningOutcomeCode,
    int? MaarifAlignmentScore,
    string? TransformationLevel,
    bool EditorRequired,
    bool RequiresVisual,
    string? VisualType,
    decimal? VisualConfidence,
    string? VisualDescription,
    IReadOnlyList<AlignmentScoreResponse> AlignmentScores);
