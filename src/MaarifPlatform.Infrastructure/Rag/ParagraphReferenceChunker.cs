using System.Text;
using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Application.Rag;

namespace MaarifPlatform.Infrastructure.Rag;

/// <summary>§G RAG ingestion — ilk geçiş chunking: sayfa içi paragrafları (boş satırla ayrılmış
/// bloklar) korur, çok uzun paragrafları cümle sınırlarına yakın yerlerden böler.
/// Başlık/kazanım tespiti yapmaz (SectionPath her zaman null döner) — bu, §IReferenceChunker'ın
/// belirttiği gibi ileride daha gelişmiş bir implementasyonla değiştirilebilecek bilinçli bir
/// MVP sınırıdır.</summary>
public class ParagraphReferenceChunker : IReferenceChunker
{
    private const int MaxChunkChars = 1200;
    private const int MinChunkChars = 80;

    public IReadOnlyList<ChunkCandidate> Chunk(IReadOnlyList<ExtractedPage> pages)
    {
        var chunks = new List<ChunkCandidate>();

        foreach (var page in pages)
        {
            foreach (var paragraph in SplitIntoParagraphs(page.RawText))
            {
                foreach (var piece in SplitIfTooLong(paragraph))
                {
                    var trimmed = piece.Trim();
                    if (trimmed.Length >= MinChunkChars)
                    {
                        chunks.Add(new ChunkCandidate(page.PageNo, null, trimmed));
                    }
                }
            }
        }

        return chunks;
    }

    private static IEnumerable<string> SplitIntoParagraphs(string rawText)
    {
        var lines = (rawText ?? string.Empty).Split('\n').Select(l => l.TrimEnd()).ToList();
        var current = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                continue;
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }
            current.Append(line.Trim());
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static IEnumerable<string> SplitIfTooLong(string paragraph)
    {
        if (paragraph.Length <= MaxChunkChars)
        {
            yield return paragraph;
            yield break;
        }

        var sentences = paragraph.Split(". ", StringSplitOptions.None);
        var current = new StringBuilder();

        foreach (var sentence in sentences)
        {
            var candidate = current.Length == 0 ? sentence : current + ". " + sentence;
            if (candidate.Length > MaxChunkChars && current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
                current.Append(sentence);
            }
            else
            {
                current.Clear();
                current.Append(candidate);
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}
