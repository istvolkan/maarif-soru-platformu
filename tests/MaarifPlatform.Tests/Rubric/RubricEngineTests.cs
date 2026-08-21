using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Rubric;
using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Tests.Rubric;

public class RubricEngineTests
{
    private static CriterionEvaluation Eval(string criterion, int score, bool criticalGateViolated = false) =>
        new(criterion, score, "test", null, criticalGateViolated);

    [Fact]
    public void Evaluate_AllCriteriaHighScore_ReturnsNoChange()
    {
        var evaluations = MaarifRubric.Criteria.Select(c => Eval(c.Key, 95)).ToList();

        var result = RubricEngine.Evaluate(evaluations);

        Assert.Equal(95, result.WeightedScore);
        Assert.Equal(TransformationLevel.NoChange, result.Level);
        Assert.False(result.CriticalGateFailed);
        Assert.Empty(result.MissingCriteria);
    }

    [Fact]
    public void Evaluate_AllCriteriaMidScore_ReturnsModerateTransformation()
    {
        var evaluations = MaarifRubric.Criteria.Select(c => Eval(c.Key, 60)).ToList();

        var result = RubricEngine.Evaluate(evaluations);

        Assert.Equal(60, result.WeightedScore);
        Assert.Equal(TransformationLevel.ModerateTransformation, result.Level);
        Assert.False(result.CriticalGateFailed);
    }

    [Fact]
    public void Evaluate_CriticalGateViolated_ForcesManualReviewRegardlessOfScore()
    {
        var evaluations = MaarifRubric.Criteria
            .Select(c => Eval(c.Key, 95, criticalGateViolated: c.Key == "mathematical_accuracy"))
            .ToList();

        var result = RubricEngine.Evaluate(evaluations);

        Assert.True(result.CriticalGateFailed);
        Assert.Equal(TransformationLevel.ManualReviewRequired, result.Level);
        Assert.Contains(result.Issues, i => i.StartsWith("CRITICAL[mathematical_accuracy]"));
    }

    [Fact]
    public void Evaluate_MissingCriteria_ContributesZeroAndIsReported()
    {
        var provided = MaarifRubric.Criteria.Take(7).Select(c => Eval(c.Key, 100)).ToList();
        var expectedMissing = MaarifRubric.Criteria.Skip(7).Select(c => c.Key).ToList();
        var expectedScore = (int)MaarifRubric.Criteria.Take(7).Sum(c => c.Weight);

        var result = RubricEngine.Evaluate(provided);

        Assert.Equal(expectedScore, result.WeightedScore);
        Assert.Equal(expectedMissing.Count, result.MissingCriteria.Count);
        Assert.All(expectedMissing, m => Assert.Contains(m, result.MissingCriteria));
    }

    [Fact]
    public void Evaluate_VeryLowScores_ReturnsRewrite()
    {
        var evaluations = MaarifRubric.Criteria.Select(c => Eval(c.Key, 10)).ToList();

        var result = RubricEngine.Evaluate(evaluations);

        Assert.Equal(TransformationLevel.Rewrite, result.Level);
    }
}
