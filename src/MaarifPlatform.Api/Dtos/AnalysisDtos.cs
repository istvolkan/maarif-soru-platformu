namespace MaarifPlatform.Api.Dtos;

public record AnalysisSummaryResponse(
    int WeightedScore,
    string TransformationLevel,
    bool ManualReviewRequired,
    int GroundingChunksUsed,
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
    IReadOnlyList<AlignmentScoreResponse> AlignmentScores);
