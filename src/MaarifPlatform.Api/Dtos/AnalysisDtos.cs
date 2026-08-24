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

public record TransformationSummaryResponse(
    string TransformationLevel,
    string Decision,
    bool Skipped,
    int? QualityScore,
    bool? Passed,
    string? TransformProvider,
    string? TransformModel,
    decimal? TransformCostUsd,
    string? JudgeProvider,
    string? JudgeModel,
    decimal? JudgeCostUsd);

public record AlignmentScoreResponse(
    string Criterion,
    decimal Score,
    decimal Weight,
    string Explanation,
    string? SourceRef,
    bool IsCriticalGate);

public record DistractorResponse(string OptionLabel, string? MisconceptionCode, string? Explanation);

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
    IReadOnlyList<AlignmentScoreResponse> AlignmentScores,
    string? NewQuestion,
    IReadOnlyList<string> NewOptions,
    string? CorrectAnswer,
    string? Solution,
    IReadOnlyList<DistractorResponse> Distractors,
    int? QualityScore,
    bool? Passed,
    IReadOnlyList<string> QualityFlags,
    IReadOnlyList<string> CriticalFailures);
