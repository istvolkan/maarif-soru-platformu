using MaarifPlatform.Infrastructure.Vision;

namespace MaarifPlatform.Tests.Vision;

public class HeuristicVisionRouterTests
{
    private readonly HeuristicVisionRouter _sut = new();

    [Fact]
    public async Task DecideAsync_TextOnlyQuestion_DoesNotRequireVisual()
    {
        var decision = await _sut.DecideAsync("48 ve 72 sayılarının EBOB'u kaçtır?", null);

        Assert.False(decision.RequiresVisual);
        Assert.Null(decision.VisualType);
    }

    [Fact]
    public async Task DecideAsync_GeometryKeyword_RequiresVisual_WithGeometryType()
    {
        var decision = await _sut.DecideAsync(
            "Yukarıdaki şekilde verilen ABC üçgeninde |AB| = 5 cm'dir. |BC| kaç cm'dir?", null);

        Assert.True(decision.RequiresVisual);
        Assert.Equal("geometry_diagram", decision.VisualType);
        Assert.True(decision.Confidence > 0.5m);
    }

    [Fact]
    public async Task DecideAsync_KeywordPlusOriginalReference_HasHigherConfidence()
    {
        var withoutRef = await _sut.DecideAsync("Aşağıdaki tabloya göre soruyu cevaplayınız.", null);
        var withRef = await _sut.DecideAsync("Aşağıdaki tabloya göre soruyu cevaplayınız.", "Tablo 3, sayfa 12");

        Assert.True(withRef.Confidence > withoutRef.Confidence);
    }

    [Fact]
    public async Task DecideAsync_NoKeywordButHasReference_RequiresVisual_WithLowConfidence()
    {
        var decision = await _sut.DecideAsync("Bu ifadelerden hangisi doğrudur?", "Şekil 7");

        Assert.True(decision.RequiresVisual);
        Assert.Equal("mixed_visual_question", decision.VisualType);
        Assert.True(decision.Confidence < 0.5m);
    }
}
