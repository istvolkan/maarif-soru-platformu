using MaarifPlatform.Application.Rubric;
using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Tests.Rubric;

public class TransformationModeMapperTests
{
    [Theory]
    [InlineData(TransformationLevel.NoChange, TransformDecision.SkipAiApprove)]
    [InlineData(TransformationLevel.LightEdit, TransformDecision.SkipAiApprove)]
    [InlineData(TransformationLevel.ModerateTransformation, TransformDecision.Conservative)]
    [InlineData(TransformationLevel.MajorTransformation, TransformDecision.Transform)]
    [InlineData(TransformationLevel.Rewrite, TransformDecision.Redesign)]
    public void Decide_KnownLevel_ReturnsExpectedDecision(TransformationLevel level, TransformDecision expected)
    {
        Assert.Equal(expected, TransformationModeMapper.Decide(level));
    }

    [Theory]
    [InlineData(TransformationLevel.ManualReviewRequired)]
    [InlineData(TransformationLevel.Reject)]
    public void Decide_LevelThatShouldNeverReachTransform_Throws(TransformationLevel level)
    {
        Assert.Throws<InvalidOperationException>(() => TransformationModeMapper.Decide(level));
    }
}
