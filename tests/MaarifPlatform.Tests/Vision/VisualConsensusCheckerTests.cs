using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Vision;

namespace MaarifPlatform.Tests.Vision;

public class VisualConsensusCheckerTests
{
    private static readonly AiUsage Usage = new("test", "test-model", 10, 10, 0m, 1);

    private static VisualObservation Build(string visualType, IReadOnlyList<VisualRelation>? relations = null) =>
        new(
            VisualType: visualType,
            Description: "test",
            Confidence: 0.9m,
            Elements: [],
            Relations: relations ?? [],
            VisualText: [],
            Symbols: [],
            Measurements: [],
            Warnings: [],
            Usage: Usage);

    [Fact]
    public void Compare_SameVisualTypeAndRelations_NoWarnings()
    {
        var relations = new List<VisualRelation> { new("A", "perpendicular", "B", 0.9m) };
        var primary = Build("geometric_figure", relations);
        var secondary = Build("geometric_figure", relations);

        var warnings = VisualConsensusChecker.Compare(primary, secondary);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Compare_DifferentVisualType_EmitsDisagreementWarning()
    {
        var primary = Build("geometric_figure");
        var secondary = Build("graph_chart");

        var warnings = VisualConsensusChecker.Compare(primary, secondary);

        Assert.Contains(warnings, w => w.Type == "VISION_PROVIDER_DISAGREEMENT");
    }

    [Fact]
    public void Compare_VisualTypeDiffersOnlyByCase_NoDisagreementWarning()
    {
        var primary = Build("Geometric_Figure");
        var secondary = Build("geometric_figure");

        var warnings = VisualConsensusChecker.Compare(primary, secondary);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Compare_SameRelationPairDifferentRelationLabel_EmitsDisagreementWarning()
    {
        var primary = Build("geometric_figure", [new VisualRelation("A", "perpendicular", "B", 0.9m)]);
        var secondary = Build("geometric_figure", [new VisualRelation("A", "parallel", "B", 0.9m)]);

        var warnings = VisualConsensusChecker.Compare(primary, secondary);

        Assert.Contains(warnings, w => w.Type == "VISION_PROVIDER_DISAGREEMENT");
    }

    [Fact]
    public void Compare_SameRelationReversedSubjectObjectOrder_NoDisagreementWarning()
    {
        var primary = Build("geometric_figure", [new VisualRelation("A", "perpendicular", "B", 0.9m)]);
        var secondary = Build("geometric_figure", [new VisualRelation("B", "perpendicular", "A", 0.9m)]);

        var warnings = VisualConsensusChecker.Compare(primary, secondary);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Compare_RelationOnlyInSecondary_NoWarningForThatRelation()
    {
        var primary = Build("geometric_figure", []);
        var secondary = Build("geometric_figure", [new VisualRelation("A", "perpendicular", "B", 0.9m)]);

        var warnings = VisualConsensusChecker.Compare(primary, secondary);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Compare_WarningConfidence_IsMinimumOfBothObservations()
    {
        var primaryRelation = new VisualRelation("A", "perpendicular", "B", 0.9m);
        var secondaryRelation = new VisualRelation("A", "parallel", "B", 0.4m);
        var primary = Build("geometric_figure", [primaryRelation]);
        var secondary = Build("geometric_figure", [secondaryRelation]);

        var warnings = VisualConsensusChecker.Compare(primary, secondary);

        var relationWarning = Assert.Single(warnings, w => w.Message.Contains("perpendicular"));
        Assert.Equal(0.4m, relationWarning.Confidence);
    }
}
