using MaarifPlatform.Application.Generation;
using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Tests.Generation;

public class GenerationBlueprintBuilderTests
{
    [Fact]
    public void Build_BalancedDifficulty_DistributesTwentyFiftyThirty()
    {
        var items = GenerationBlueprintBuilder.Build(
            count: 10,
            difficultySelection: GenerationBlueprintBuilder.BalancedDifficulty,
            questionTypes: ["ÇoktanSeçmeli"],
            contentFrameworks: []);

        Assert.Equal(10, items.Count);
        Assert.Equal(2, items.Count(i => i.Difficulty == DifficultyLevel.Easy));
        Assert.Equal(5, items.Count(i => i.Difficulty == DifficultyLevel.Medium));
        Assert.Equal(3, items.Count(i => i.Difficulty == DifficultyLevel.Hard));
    }

    [Fact]
    public void Build_BalancedDifficulty_SumsExactlyToCountEvenWhenNotEvenlyDivisible()
    {
        var items = GenerationBlueprintBuilder.Build(
            count: 7,
            difficultySelection: GenerationBlueprintBuilder.BalancedDifficulty,
            questionTypes: ["ÇoktanSeçmeli"],
            contentFrameworks: []);

        Assert.Equal(7, items.Count);
        // %20/%50/%30 * 7 = 1.4 / 3.5 / 2.1 -> taban 1/3/2 = 6, kalan 1 en büyük kesire (Medium, .5) gider.
        Assert.Equal(1, items.Count(i => i.Difficulty == DifficultyLevel.Easy));
        Assert.Equal(4, items.Count(i => i.Difficulty == DifficultyLevel.Medium));
        Assert.Equal(2, items.Count(i => i.Difficulty == DifficultyLevel.Hard));
    }

    [Fact]
    public void Build_SingleDifficulty_RepeatsForAllItems()
    {
        var items = GenerationBlueprintBuilder.Build(
            count: 5,
            difficultySelection: nameof(DifficultyLevel.Hard),
            questionTypes: ["ÇoktanSeçmeli"],
            contentFrameworks: []);

        Assert.All(items, i => Assert.Equal(DifficultyLevel.Hard, i.Difficulty));
    }

    [Fact]
    public void Build_InvalidDifficulty_Throws()
    {
        Assert.Throws<ArgumentException>(() => GenerationBlueprintBuilder.Build(
            count: 3, difficultySelection: "Imkansiz", questionTypes: ["ÇoktanSeçmeli"], contentFrameworks: []));
    }

    [Fact]
    public void Build_MultipleQuestionTypes_RoundRobinsEvenly()
    {
        var items = GenerationBlueprintBuilder.Build(
            count: 6,
            difficultySelection: nameof(DifficultyLevel.Medium),
            questionTypes: ["ÇoktanSeçmeli", "AçıkUçlu", "KısaCevaplı"],
            contentFrameworks: []);

        Assert.Equal(2, items.Count(i => i.QuestionType == "ÇoktanSeçmeli"));
        Assert.Equal(2, items.Count(i => i.QuestionType == "AçıkUçlu"));
        Assert.Equal(2, items.Count(i => i.QuestionType == "KısaCevaplı"));
    }

    [Fact]
    public void Build_MixedAloneWithNoConcreteTypes_Throws()
    {
        Assert.Throws<ArgumentException>(() => GenerationBlueprintBuilder.Build(
            count: 4, difficultySelection: nameof(DifficultyLevel.Medium),
            questionTypes: [GenerationBlueprintBuilder.MixedQuestionType], contentFrameworks: []));
    }

    [Fact]
    public void Build_NoContentFrameworks_LeavesThemNull()
    {
        var items = GenerationBlueprintBuilder.Build(
            count: 3, difficultySelection: nameof(DifficultyLevel.Medium),
            questionTypes: ["ÇoktanSeçmeli"], contentFrameworks: []);

        Assert.All(items, i => Assert.Null(i.ContentFramework));
    }

    [Fact]
    public void Build_ContentFrameworks_RoundRobinsAcrossSelection()
    {
        var items = GenerationBlueprintBuilder.Build(
            count: 4, difficultySelection: nameof(DifficultyLevel.Medium),
            questionTypes: ["ÇoktanSeçmeli"], contentFrameworks: ["Çerçeve A", "Çerçeve B"]);

        Assert.Equal(2, items.Count(i => i.ContentFramework == "Çerçeve A"));
        Assert.Equal(2, items.Count(i => i.ContentFramework == "Çerçeve B"));
    }

    [Fact]
    public void Build_ZeroCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GenerationBlueprintBuilder.Build(
            0, nameof(DifficultyLevel.Medium), ["ÇoktanSeçmeli"], []));
    }

    [Fact]
    public void Build_NoQuestionTypes_Throws()
    {
        Assert.Throws<ArgumentException>(() => GenerationBlueprintBuilder.Build(
            3, nameof(DifficultyLevel.Medium), [], []));
    }
}
