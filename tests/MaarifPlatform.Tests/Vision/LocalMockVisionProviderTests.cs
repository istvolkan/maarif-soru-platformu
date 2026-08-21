using MaarifPlatform.Infrastructure.Vision;

namespace MaarifPlatform.Tests.Vision;

public class LocalMockVisionProviderTests
{
    private readonly LocalMockVisionProvider _sut = new();

    [Fact]
    public async Task AnalyzeQuestionImageAsync_AlwaysReturnsZeroConfidenceAndMockWarning()
    {
        var observation = await _sut.AnalyzeQuestionImageAsync([1, 2, 3], "test question");

        Assert.Equal(0m, observation.Confidence);
        Assert.Contains(observation.Warnings, w => w.Type == "MOCK_NO_REAL_VISION");
    }

    [Fact]
    public async Task ValidateVisualStructureAsync_AlwaysFlagsMockLimitation()
    {
        var observation = await _sut.AnalyzeQuestionImageAsync([1, 2, 3], "test question");

        var warnings = await _sut.ValidateVisualStructureAsync(observation);

        Assert.Contains(warnings, w => w.Type == "MOCK_NO_REAL_VALIDATION");
    }
}
