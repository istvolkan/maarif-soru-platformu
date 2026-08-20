using System.Text;
using System.Text.RegularExpressions;
using MaarifPlatform.Application.Extraction;

namespace MaarifPlatform.Infrastructure.Extraction;

/// <summary>§10 QUESTION DETECTION — deterministik, regex tabanlı ilk geçiş. AI kullanmaz
/// (§9: her görevi pahalı modele göndermeme ilkesi burada en uç noktasına taşınmıştır — bu görev
/// için önce ücretsiz bir heuristic denenir, LLM sadece düşük güvenli sayfalarda devreye girebilir).
///
/// Bilinen sınırlamalar (bilinçli MVP kapsam kararı, §elestiri):
/// - Bir soru bloğunun sayfa sınırını aşması desteklenmez; her sayfa bağımsız işlenir.
/// - Numaralandırma "12." / "12)" biçimindeki satır başı kalıplarına dayanır; gövde içindeki
///   ondalık sayılar (örn. "3.14") satır başında değilse yanlış eşleşmez, ama düzensiz
///   dizgilenmiş PDF'lerde yanlış pozitif üretebilir — bu yüzden her blok IsLowConfidence
///   bayrağı taşır ve nihai karar insan editöre bırakılır.</summary>
public class HeuristicQuestionSegmenter : IQuestionSegmenter
{
    private static readonly Regex QuestionStart = new(@"^\s*(\d{1,3})[.\)]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex OptionStart = new(@"^\s*([A-E])[.\)]\s+(.*)$", RegexOptions.Compiled);

    public IReadOnlyList<QuestionBlock> Segment(IReadOnlyList<ExtractedPage> pages)
    {
        var blocks = new List<QuestionBlock>();

        foreach (var page in pages)
        {
            var lines = (page.RawText ?? string.Empty)
                .Split('\n')
                .Select(l => l.TrimEnd())
                .Where(l => l.Length > 0)
                .ToList();

            int? currentNo = null;
            var stem = new StringBuilder();
            var rawBlock = new StringBuilder();
            var options = new List<OptionCandidate>();
            string? currentOptionLabel = null;
            var optionTextBuffer = new StringBuilder();

            void CommitOption()
            {
                if (currentOptionLabel is not null)
                {
                    options.Add(new OptionCandidate(currentOptionLabel, optionTextBuffer.ToString().Trim()));
                    optionTextBuffer.Clear();
                    currentOptionLabel = null;
                }
            }

            void FlushBlock()
            {
                if (currentNo is null && stem.Length == 0)
                {
                    return;
                }

                CommitOption();

                var stemText = stem.ToString().Trim();
                var block = new QuestionBlock(
                    currentNo,
                    page.PageNo,
                    rawBlock.ToString().Trim(),
                    stemText,
                    options.ToList(),
                    IsLowConfidence(stemText, options));

                blocks.Add(block);

                stem.Clear();
                rawBlock.Clear();
                options = new List<OptionCandidate>();
            }

            foreach (var line in lines)
            {
                var qMatch = QuestionStart.Match(line);
                if (qMatch.Success)
                {
                    CommitOption();
                    FlushBlock();

                    currentNo = int.Parse(qMatch.Groups[1].Value);
                    rawBlock.AppendLine(line);
                    stem.AppendLine(qMatch.Groups[2].Value);
                    continue;
                }

                if (currentNo is null)
                {
                    // Sayfa numarasız/başlıksız içerikle başlıyorsa (üstbilgi, önceki sorunun devamı vb.)
                    // ilk soru numarasına kadar atla — bu satırlar hiçbir bloğa dahil edilmez.
                    continue;
                }

                var oMatch = OptionStart.Match(line);
                if (oMatch.Success)
                {
                    CommitOption();
                    currentOptionLabel = oMatch.Groups[1].Value;
                    optionTextBuffer.Append(oMatch.Groups[2].Value);
                    rawBlock.AppendLine(line);
                    continue;
                }

                rawBlock.AppendLine(line);
                if (currentOptionLabel is not null)
                {
                    optionTextBuffer.Append(' ').Append(line);
                }
                else
                {
                    stem.AppendLine(line);
                }
            }

            CommitOption();
            FlushBlock();
        }

        return blocks;
    }

    private static bool IsLowConfidence(string stem, IReadOnlyList<OptionCandidate> options)
    {
        if (stem.Length < 15)
        {
            return true;
        }

        // Tek şık bulunması genelde bir dizgileme hatasının işaretidir (0 şık = açık uçlu soru
        // olabilir ve normaldir, 2+ şık düzenli bir ÇSS'dir).
        return options.Count == 1;
    }
}
