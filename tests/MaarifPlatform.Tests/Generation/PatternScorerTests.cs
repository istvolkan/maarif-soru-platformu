using MaarifPlatform.Application.Generation;

namespace MaarifPlatform.Tests.Generation;

public class PatternScorerTests
{
    [Fact]
    public void Score_WeightZero_IgnoresAffinityEntirely()
    {
        var highAffinity = PatternScorer.Score(100m, maarifWeightPercent: 0);
        var lowAffinity = PatternScorer.Score(0m, maarifWeightPercent: 0);

        Assert.Equal(0.5, highAffinity);
        Assert.Equal(0.5, lowAffinity);
    }

    [Fact]
    public void Score_WeightHundred_UsesAffinityDirectly()
    {
        Assert.Equal(1.0, PatternScorer.Score(100m, maarifWeightPercent: 100));
        Assert.Equal(0.0, PatternScorer.Score(0m, maarifWeightPercent: 100));
    }

    [Fact]
    public void Score_NullAffinity_DefaultsToNeutralFifty()
    {
        var withNull = PatternScorer.Score(null, maarifWeightPercent: 100);
        var withFifty = PatternScorer.Score(50m, maarifWeightPercent: 100);

        Assert.Equal(withFifty, withNull);
    }

    [Fact]
    public void Score_HigherAffinity_RanksAboveLowerAffinityAtSameWeight()
    {
        var higher = PatternScorer.Score(80m, maarifWeightPercent: 70);
        var lower = PatternScorer.Score(30m, maarifWeightPercent: 70);

        Assert.True(higher > lower);
    }
}
