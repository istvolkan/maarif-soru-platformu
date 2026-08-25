using MaarifPlatform.Application.Providers;

namespace MaarifPlatform.Tests.Providers;

public class JudgeConsensusCheckerTests
{
    private static readonly AiUsage Usage = new("test", "test-model", 10, 10, 0m, 1);

    private static EvaluateQuestionResult Build(int score, bool passed) =>
        new(QualityScore: score, Passed: passed, CriticalFailures: [], QualityFlags: [], Usage: Usage);

    [Fact]
    public void Compare_SameResult_NoWarnings()
    {
        var primary = Build(80, true);
        var secondary = Build(80, true);

        var warnings = JudgeConsensusChecker.Compare(primary, secondary, scoreDeltaThreshold: 20);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Compare_DifferentPassed_EmitsDisagreementWarning()
    {
        var primary = Build(80, true);
        var secondary = Build(80, false);

        var warnings = JudgeConsensusChecker.Compare(primary, secondary, scoreDeltaThreshold: 20);

        Assert.Contains(warnings, w => w.StartsWith("disagreement:"));
    }

    [Fact]
    public void Compare_ScoreDeltaAboveThreshold_EmitsDisagreementWarning()
    {
        var primary = Build(80, true);
        var secondary = Build(50, true);

        var warnings = JudgeConsensusChecker.Compare(primary, secondary, scoreDeltaThreshold: 20);

        Assert.Contains(warnings, w => w.StartsWith("disagreement:"));
    }

    [Fact]
    public void Compare_ScoreDeltaAtOrBelowThreshold_NoWarning()
    {
        var primary = Build(80, true);
        var secondary = Build(60, true);

        var warnings = JudgeConsensusChecker.Compare(primary, secondary, scoreDeltaThreshold: 20);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Compare_BothConditionsViolated_EmitsTwoWarnings()
    {
        var primary = Build(90, true);
        var secondary = Build(30, false);

        var warnings = JudgeConsensusChecker.Compare(primary, secondary, scoreDeltaThreshold: 20);

        Assert.Equal(2, warnings.Count);
    }
}
