using MaarifPlatform.Application.Intelligence;
using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Tests.Intelligence;

public class QuestionPoolClassifierTests
{
    [Theory]
    [InlineData(100, QuestionPoolClassification.MaarifAligned)]
    [InlineData(75, QuestionPoolClassification.MaarifAligned)]
    [InlineData(74, QuestionPoolClassification.Hybrid)]
    [InlineData(50, QuestionPoolClassification.Hybrid)]
    [InlineData(49, QuestionPoolClassification.Traditional)]
    [InlineData(0, QuestionPoolClassification.Traditional)]
    public void Classify_UsesSeventyFiveThresholdForMaarifAligned(int score, QuestionPoolClassification expected)
    {
        Assert.Equal(expected, QuestionPoolClassifier.Classify(score));
    }
}
