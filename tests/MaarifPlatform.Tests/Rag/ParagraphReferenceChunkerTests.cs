using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Infrastructure.Rag;

namespace MaarifPlatform.Tests.Rag;

public class ParagraphReferenceChunkerTests
{
    private readonly ParagraphReferenceChunker _sut = new();

    [Fact]
    public void Chunk_SplitsOnBlankLines_AndDropsTooShortParagraphs()
    {
        var pageText = string.Join('\n',
            "Bu bölümde öğrenciler doğal sayılarda EBOB ve EKOK kavramlarını,",
            "ortak bölen ve ortak kat kavramlarıyla ilişkilendirerek öğrenirler.",
            "",
            "Kısa.",
            "",
            "İkinci paragraf burada başlar ve yeterince uzun olduğu için",
            "bir chunk olarak korunması beklenir, en az seksen karakter olmalı.");

        var pages = new[] { new ExtractedPage(12, pageText) };

        var chunks = _sut.Chunk(pages);

        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(12, c.Page));
        Assert.Contains(chunks, c => c.Text.StartsWith("Bu bölümde"));
        Assert.Contains(chunks, c => c.Text.StartsWith("İkinci paragraf"));
        Assert.DoesNotContain(chunks, c => c.Text == "Kısa.");
    }

    [Fact]
    public void Chunk_SplitsVeryLongParagraph_IntoMultiplePieces()
    {
        var sentence = "Bu cümle EBOB ve EKOK kavramlarını gerçek yaşam bağlamında ele almaktadır";
        var longParagraph = string.Join(". ", Enumerable.Repeat(sentence, 30)) + ".";

        var pages = new[] { new ExtractedPage(5, longParagraph) };

        var chunks = _sut.Chunk(pages);

        Assert.True(chunks.Count > 1, "Uzun paragraf birden fazla chunk'a bölünmeli.");
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 1300));
    }
}
