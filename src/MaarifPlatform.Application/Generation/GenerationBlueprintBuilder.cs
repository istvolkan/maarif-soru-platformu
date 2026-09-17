using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Application.Generation;

public sealed record GenerationBlueprintItem(
    DifficultyLevel Difficulty,
    string QuestionType,
    string? ContentFramework);

/// <summary>§14/§34 Soru Planı — N sorunun zorluk/soru tipi/içerik çerçevesi dağılımını, hiçbir
/// LLM çağrısı yapmadan, saf deterministik olarak hesaplar (RubricEngine/TransformationModeMapper
/// ile aynı felsefe: "LLM'e sorma, kodda hesapla"). GenerationOrchestrationService bu planı
/// tüketerek her slot için AYRI bir Generator çağrısı yapar — "Dengeli Dağılım" tek bir LLM
/// isteğiyle değil, N ayrı çağrının PARAMETRELERİYLE elde edilir.</summary>
public static class GenerationBlueprintBuilder
{
    public const string BalancedDifficulty = "Balanced";
    public const string MixedQuestionType = "Mixed";

    private static readonly (DifficultyLevel Level, double Share)[] BalancedShares =
    [
        (DifficultyLevel.Easy, 0.20),
        (DifficultyLevel.Medium, 0.50),
        (DifficultyLevel.Hard, 0.30)
    ];

    public static IReadOnlyList<GenerationBlueprintItem> Build(
        int count,
        string difficultySelection,
        IReadOnlyList<string> questionTypes,
        IReadOnlyList<string> contentFrameworks)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Soru adedi en az 1 olmalı.");
        }

        if (questionTypes.Count == 0)
        {
            throw new ArgumentException("En az bir soru tipi seçilmeli.", nameof(questionTypes));
        }

        var difficulties = ResolveDifficulties(count, difficultySelection);
        var types = ResolveByRoundRobin(count, ExpandMixed(questionTypes));
        var frameworks = contentFrameworks.Count == 0
            ? Enumerable.Repeat<string?>(null, count).ToList()
            : ResolveByRoundRobin(count, contentFrameworks).Cast<string?>().ToList();

        var items = new List<GenerationBlueprintItem>(count);
        for (var i = 0; i < count; i++)
        {
            items.Add(new GenerationBlueprintItem(difficulties[i], types[i], frameworks[i]));
        }

        return items;
    }

    private static IReadOnlyList<string> ExpandMixed(IReadOnlyList<string> questionTypes) =>
        questionTypes.Count == 1 && string.Equals(questionTypes[0], MixedQuestionType, StringComparison.OrdinalIgnoreCase)
            ? throw new ArgumentException(
                "\"Karma\" seçildiğinde dağıtılacak somut soru tipleri de seçilmelidir " +
                "(Karma tek başına hangi tiplerin karışacağını belirtmez).", nameof(questionTypes))
            : questionTypes;

    private static List<DifficultyLevel> ResolveDifficulties(int count, string selection)
    {
        if (!string.Equals(selection, BalancedDifficulty, StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<DifficultyLevel>(selection, ignoreCase: true, out var level))
            {
                throw new ArgumentException($"Geçersiz zorluk: {selection}", nameof(selection));
            }

            return Enumerable.Repeat(level, count).ToList();
        }

        var counts = AllocateByShare(count, BalancedShares.Select(s => s.Share).ToArray());
        var result = new List<DifficultyLevel>(count);
        for (var i = 0; i < BalancedShares.Length; i++)
        {
            result.AddRange(Enumerable.Repeat(BalancedShares[i].Level, counts[i]));
        }

        return result;
    }

    /// <summary>Karma soru tipi / çoklu içerik çerçevesi seçimlerini pedagojik olarak dengeli
    /// (round-robin) dağıtır — ilk seçilenler ağırlıklı bir yığılma olmadan.</summary>
    private static List<string> ResolveByRoundRobin(int count, IReadOnlyList<string> options)
    {
        var result = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(options[i % options.Count]);
        }

        return result;
    }

    /// <summary>Largest remainder method — paylar toplamı N'e TAM bölünür, yuvarlama artıkları
    /// en büyük kesirli kalana sahip gruba verilir (örn. N=7, %20/%50/%30 → 1/4/2, toplam 7).</summary>
    private static int[] AllocateByShare(int total, IReadOnlyList<double> shares)
    {
        var raw = shares.Select(s => s * total).ToArray();
        var counts = raw.Select(r => (int)Math.Floor(r)).ToArray();
        var remainder = total - counts.Sum();

        var byFraction = raw
            .Select((r, i) => (Index: i, Fraction: r - Math.Floor(r)))
            .OrderByDescending(x => x.Fraction)
            .ToList();

        for (var i = 0; i < remainder; i++)
        {
            counts[byFraction[i].Index]++;
        }

        return counts;
    }
}
