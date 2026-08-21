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
}
