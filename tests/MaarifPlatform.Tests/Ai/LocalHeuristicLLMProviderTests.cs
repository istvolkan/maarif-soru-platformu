using MaarifPlatform.Application.Providers;
using MaarifPlatform.Infrastructure.Ai;

namespace MaarifPlatform.Tests.Ai;

public class LocalHeuristicLLMProviderTests
{
    private readonly LocalHeuristicLLMProvider _sut = new();

    [Fact]
    public async Task AnalyzeQuestionAsync_NoGrounding_RequiresManualReview()
    {
        var request = new AnalyzeQuestionRequest(
            "48 ve 72 sayılarının EBOB'u kaçtır?", ["6", "8", "12", "24", "36"], "A",
            9, "Matematik", []);

        var result = await _sut.AnalyzeQuestionAsync(request);

        Assert.True(result.ManualReviewRequired);
        Assert.Null(result.LearningOutcomeCode);
        Assert.NotEmpty(result.CriterionEvaluations);
        Assert.Contains(result.CriterionEvaluations, e => e.Criterion == "mathematical_accuracy" && e.Score == 100);
    }

    [Fact]
    public async Task AnalyzeQuestionAsync_WithGrounding_DoesNotRequireManualReview()
    {
        var grounding = new[] { new GroundingReference(Guid.NewGuid(), 12, "M.9.1.3.2", "EBOB ve EKOK kazanımı...") };
        var request = new AnalyzeQuestionRequest(
            "48 ve 72 sayılarının EBOB'u kaçtır?", ["6", "8", "12", "24", "36"], "A",
            9, "Matematik", grounding);

        var result = await _sut.AnalyzeQuestionAsync(request);

        Assert.False(result.ManualReviewRequired);
        Assert.All(result.CriterionEvaluations, e => Assert.InRange(e.Score, 0, 100));
    }

    private static readonly AnalyzeQuestionResult SampleAnalysis = new(
        "EBOB hesaplama", "M.9.1.3.2", "sayılar", "bölünebilirlik", false, [], false, null,
        new AiUsage("local-heuristic", "mock-v1", 10, 10, 0m, 5));

    [Fact]
    public async Task TransformQuestionAsync_ReturnsFourOptionsWithModePrefix()
    {
        var request = new TransformQuestionRequest("48 ve 72 sayılarının EBOB'u kaçtır?", "Conservative", SampleAnalysis, []);

        var result = await _sut.TransformQuestionAsync(request);

        Assert.StartsWith("[MOCK-Conservative]", result.NewQuestion);
        Assert.Equal(4, result.NewOptions.Count);
        Assert.Equal(3, result.Distractors.Count);
        Assert.Contains(result.CorrectAnswer, result.NewOptions);
    }

    [Fact]
    public async Task EvaluateQuestionAsync_WellFormedInput_Passes()
    {
        var grounding = new[] { new GroundingReference(Guid.NewGuid(), 12, "M.9.1.3.2", "EBOB ve EKOK kazanımı...") };
        var request = new EvaluateQuestionRequest(
            "Dönüştürülmüş soru", ["A", "B", "C", "D"], "A", "Adım adım çözüm", grounding);

        var result = await _sut.EvaluateQuestionAsync(request);

        Assert.True(result.Passed);
        Assert.Empty(result.CriticalFailures);
        Assert.NotEmpty(result.QualityFlags);
    }

    [Fact]
    public async Task EvaluateQuestionAsync_MissingSolutionAndGrounding_LowerScore()
    {
        var withExtras = await _sut.EvaluateQuestionAsync(new EvaluateQuestionRequest(
            "Soru", ["A", "B", "C", "D"], "A", "Çözüm", [new GroundingReference(Guid.NewGuid(), null, null, "x")]));
        var withoutExtras = await _sut.EvaluateQuestionAsync(new EvaluateQuestionRequest(
            "Soru", ["A", "B", "C", "D"], "", "", []));

        Assert.True(withoutExtras.QualityScore < withExtras.QualityScore);
    }
}
