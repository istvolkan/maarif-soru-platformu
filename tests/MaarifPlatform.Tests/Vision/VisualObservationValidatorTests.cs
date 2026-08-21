using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Vision;

namespace MaarifPlatform.Tests.Vision;

public class VisualObservationValidatorTests
{
    private static readonly AiUsage Usage = new("test", "test-model", 10, 10, 0m, 1);

    private static VisualObservation Build(
        decimal confidence = 0.9m,
        IReadOnlyList<VisualElement>? elements = null,
        IReadOnlyList<VisualRelation>? relations = null) =>
        new(
            VisualType: "geometric_figure",
            Description: "test",
            Confidence: confidence,
            Elements: elements ?? [new VisualElement("A", "point", null, null, null, 0.9m)],
            Relations: relations ?? [],
            VisualText: [],
            Symbols: [],
            Measurements: [],
            Warnings: [],
            Usage: Usage);

    [Fact]
    public void Validate_LowConfidence_EmitsLowConfidenceWarning()
    {
        var observation = Build(confidence: 0.3m);

        var warnings = VisualObservationValidator.Validate(observation);

        Assert.Contains(warnings, w => w.Type == "LOW_CONFIDENCE");
    }

    [Fact]
    public void Validate_HighConfidence_DoesNotEmitLowConfidenceWarning()
    {
        var observation = Build(confidence: 0.9m);

        var warnings = VisualObservationValidator.Validate(observation);

        Assert.DoesNotContain(warnings, w => w.Type == "LOW_CONFIDENCE");
    }

    [Fact]
    public void Validate_RelationReferencesUnknownElement_EmitsDanglingRelationWarning()
    {
        var elements = new List<VisualElement> { new("A", "point", null, null, null, 0.9m) };
        var relations = new List<VisualRelation> { new("A", "perpendicular", "B", 0.9m) };
        var observation = Build(elements: elements, relations: relations);

        var warnings = VisualObservationValidator.Validate(observation);

        Assert.Contains(warnings, w => w.Type == "DANGLING_RELATION_REFERENCE");
    }

    [Fact]
    public void Validate_RelationReferencesKnownElements_DoesNotEmitDanglingWarning()
    {
        var elements = new List<VisualElement>
        {
            new("A", "point", null, null, null, 0.9m),
            new("B", "point", null, null, null, 0.9m)
        };
        var relations = new List<VisualRelation> { new("A", "perpendicular", "B", 0.9m) };
        var observation = Build(elements: elements, relations: relations);

        var warnings = VisualObservationValidator.Validate(observation);

        Assert.DoesNotContain(warnings, w => w.Type == "DANGLING_RELATION_REFERENCE");
    }

    [Fact]
    public void Validate_NoElementsAndNoRelations_EmitsEmptyObservationWarning()
    {
        var observation = Build(elements: [], relations: []);

        var warnings = VisualObservationValidator.Validate(observation);

        Assert.Contains(warnings, w => w.Type == "EMPTY_OBSERVATION");
    }

    [Fact]
    public void Validate_HasElements_DoesNotEmitEmptyObservationWarning()
    {
        var observation = Build();

        var warnings = VisualObservationValidator.Validate(observation);

        Assert.DoesNotContain(warnings, w => w.Type == "EMPTY_OBSERVATION");
    }
}
