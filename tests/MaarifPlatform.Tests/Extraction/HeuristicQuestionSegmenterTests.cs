using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Infrastructure.Extraction;

namespace MaarifPlatform.Tests.Extraction;

public class HeuristicQuestionSegmenterTests
{
    private readonly HeuristicQuestionSegmenter _sut = new();

    [Fact]
    public void Segment_DetectsNumberedQuestionsAndOptions_AcrossMixedTypes()
    {
        var pageText = string.Join('\n',
            "1. 48 ve 72 sayılarının EBOB'u kaçtır?",
            "A) 6",
            "B) 8",
            "C) 12",
            "D) 24",
            "E) 36",
            "",
            "2. x + 5 = 12 denklemini sağlayan x değerini bulunuz.",
            "",
            "3. Bir sınıfta 24 öğrenci vardır. Öğrencilerin 3/8'i kız öğrencidir. Kız öğrenci sayısı kaçtır?",
            "A) 6",
            "B) 8",
            "C) 9",
            "D) 10");

        var pages = new[] { new ExtractedPage(27, pageText) };

        var blocks = _sut.Segment(pages);

        Assert.Equal(3, blocks.Count);

        Assert.Equal(1, blocks[0].QuestionNo);
        Assert.Equal(5, blocks[0].Options.Count);
        Assert.False(blocks[0].IsLowConfidence);
        Assert.Equal("48 ve 72 sayılarının EBOB'u kaçtır?", blocks[0].Stem);
        Assert.Equal("36", blocks[0].Options[4].Text);

        Assert.Equal(2, blocks[1].QuestionNo);
        Assert.Empty(blocks[1].Options);
        Assert.False(blocks[1].IsLowConfidence);

        Assert.Equal(3, blocks[2].QuestionNo);
        Assert.Equal(4, blocks[2].Options.Count);
        Assert.All(blocks, b => Assert.Equal(27, b.PageNo));
    }

    [Fact]
    public void Segment_FlagsLowConfidence_WhenOnlyOneOptionDetectedOrStemTooShort()
    {
        var pageText = string.Join('\n',
            "5. Kısa.",
            "A) tek şık");

        var pages = new[] { new ExtractedPage(3, pageText) };

        var blocks = _sut.Segment(pages);

        Assert.Single(blocks);
        Assert.True(blocks[0].IsLowConfidence);
    }

    [Fact]
    public void Segment_ReturnsNoBlocks_WhenPageHasNoNumberedQuestions()
    {
        var pages = new[] { new ExtractedPage(1, "İÇİNDEKİLER\nBölüm 1 ......... 5\nBölüm 2 ......... 12") };

        var blocks = _sut.Segment(pages);

        Assert.Empty(blocks);
    }
}
