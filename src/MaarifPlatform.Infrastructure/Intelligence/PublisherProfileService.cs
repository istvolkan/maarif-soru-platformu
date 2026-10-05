using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Intelligence;

public sealed record ArchetypeFrequency(string Name, int Count);

/// <summary>§39/§53 Yayınevi Analizi (Publisher Intelligence) — Question Intelligence Engine
/// Faz 5. Amaç bir yayınevinin METNİNİ taklit etmek DEĞİL, Faz 1/2/4'ün zaten çıkardığı
/// ölçülebilir pedagojik/yapısal karakteristikleri özetlemektir (bkz. sınıf doc'undaki ilke,
/// aynı şekilde §40'taki yazar profili için de geçerlidir).</summary>
public sealed record PublisherProfile(
    string Publisher,
    int BookCount,
    int QuestionCount,
    int AnalyzedQuestionCount,
    double? AvgStemLength,
    double? VisualQuestionRate,
    double? RealWorldContextRate,
    double? MultipleChoiceRate,
    double? AvgMaarifAlignmentScore,
    IReadOnlyDictionary<QuestionPoolClassification, int> PoolDistribution,
    IReadOnlyDictionary<DifficultyLevel, int> DifficultyDistribution,
    IReadOnlyList<ArchetypeFrequency> TopArchetypes);

/// <summary>§39 Yayınevi Profili + §40 Yazar metadata (aynı aggregation motoru, grupla alanı
/// farklı — bkz. GetAuthorProfilesAsync). Book.Publisher/AuthorName BİLİNÇLİ OLARAK serbest
/// metin olarak bırakıldı (normalize edilmiş bir Publisher/Author tablosuna GEÇİLMEDİ) — şu an
/// hiçbir yayınevi/yazar bazlı AYARLAR yok, yalnızca raporlama; Admin/Books.razor'ın mevcut
/// Distinct() tabanlı serbest-metin akışını (kullanıcı deneyimi değişmeden) bozmamak, bu fazda
/// bir FK migration'ının riskini/kapsamını haklı çıkarmaz. İleride yayınevi bazlı ayarlar
/// (ör. özel ağırlıklar) gerekirse bu normalize edilebilir.</summary>
public class PublisherProfileService(MaarifDbContext db)
{
    public Task<IReadOnlyList<PublisherProfile>> GetPublisherProfilesAsync(CancellationToken ct = default) =>
        BuildProfilesAsync((publisher, _) => publisher, ct);

    public Task<IReadOnlyList<PublisherProfile>> GetAuthorProfilesAsync(CancellationToken ct = default) =>
        BuildProfilesAsync((_, authorName) => authorName, ct);

    private async Task<IReadOnlyList<PublisherProfile>> BuildProfilesAsync(
        Func<string?, string?, string?> groupSelector, CancellationToken ct)
    {
        // §48/Faz 4'ün "hiçbir soru atılmaz" ilkesiyle TUTARLI: Traditional/düşük-uyumlu sorular
        // da bu raporlamaya dahildir — burada da hiçbir filtreleme/dışlama yapılmaz.
        var books = await db.Books
            .Select(b => new { b.Id, b.Publisher, b.AuthorName })
            .ToListAsync(ct);

        var groupKeyByBookId = books
            .Select(b => new { b.Id, Key = groupSelector(b.Publisher, b.AuthorName) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Id, x => x.Key!);

        if (groupKeyByBookId.Count == 0)
        {
            return [];
        }

        var bookCounts = groupKeyByBookId.Values
            .GroupBy(k => k)
            .ToDictionary(g => g.Key, g => g.Count());

        // §B "LLM'e analiz sonucunu her defasında yeniden sorma" ilkesiyle AYNI gerekçe: tüm
        // ilgili alanlar tek sorguda, soru başına EN SON QuestionDna satırına (BooksController.
        // GetQuestions'daki aynı desen) karşılık gelecek şekilde çekilir, kalan aggregation
        // bellekte yapılır (admin raporlama yolu — sıcak yol değil, veri hacmi bu platformun
        // ölçeğinde LINQ-to-Objects için uygundur).
        var questionRows = await (
            from q in db.Questions
            where groupKeyByBookId.Keys.Contains(q.BookId)
            select new
            {
                q.BookId,
                Dna = db.QuestionDnas
                    .Where(d => d.QuestionVersion!.QuestionId == q.Id)
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => new
                    {
                        d.OriginalQuestion,
                        d.RequiresVisual,
                        d.ContextQuality,
                        d.QuestionType,
                        d.MaarifAlignmentScore,
                        d.PoolClassification,
                        d.Difficulty
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var archetypeRows = await (
            from m in db.QuestionArchetypeMembers
            where groupKeyByBookId.Keys.Contains(m.BookId)
            join a in db.QuestionArchetypes on m.ArchetypeId equals a.Id
            select new { m.BookId, ArchetypeName = a.Name })
            .ToListAsync(ct);

        var profiles = new List<PublisherProfile>();
        foreach (var (key, bookCount) in bookCounts)
        {
            var bookIdsForKey = groupKeyByBookId.Where(kv => kv.Value == key).Select(kv => kv.Key).ToHashSet();
            var rowsForKey = questionRows.Where(r => bookIdsForKey.Contains(r.BookId)).ToList();
            // DİKKAT: her çıkarılan soru EN BAŞTA bile bir Original-stage QuestionDna satırına
            // sahiptir (BookExtractionService) — MaarifAlignmentScore ise YALNIZCA RubricEngine
            // gerçekten çalıştıktan (AnalysisOrchestrationService.AnalyzeAsync) sonra dolar. Bu
            // yüzden "analiz edilmiş" sayısı r.Dna != null değil, r.Dna?.MaarifAlignmentScore !=
            // null olmalı — aksi halde her soru (hiç Analyze çalıştırılmamış olsa bile) "analiz
            // edilmiş" sayılır ve istatistikler anlamsız (hep-null) bir tabana düşer.
            var analyzedRows = rowsForKey
                .Where(r => r.Dna?.MaarifAlignmentScore is not null)
                .Select(r => r.Dna!)
                .ToList();

            var stemLengths = analyzedRows
                .Where(d => !string.IsNullOrWhiteSpace(d.OriginalQuestion))
                .Select(d => (double)d.OriginalQuestion!.Length)
                .ToList();

            var poolDistribution = analyzedRows
                .Where(d => d.PoolClassification is not null)
                .GroupBy(d => d.PoolClassification!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var difficultyDistribution = analyzedRows
                .Where(d => d.Difficulty is not null)
                .GroupBy(d => d.Difficulty!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var maarifScores = analyzedRows.Where(d => d.MaarifAlignmentScore is not null)
                .Select(d => (double)d.MaarifAlignmentScore!.Value).ToList();

            var topArchetypes = archetypeRows
                .Where(r => bookIdsForKey.Contains(r.BookId))
                .GroupBy(r => r.ArchetypeName)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new ArchetypeFrequency(g.Key, g.Count()))
                .ToList();

            profiles.Add(new PublisherProfile(
                Publisher: key,
                BookCount: bookCount,
                QuestionCount: rowsForKey.Count,
                AnalyzedQuestionCount: analyzedRows.Count,
                AvgStemLength: stemLengths.Count > 0 ? stemLengths.Average() : null,
                VisualQuestionRate: analyzedRows.Count > 0 ? analyzedRows.Count(d => d.RequiresVisual) / (double)analyzedRows.Count : null,
                RealWorldContextRate: analyzedRows.Count > 0
                    ? analyzedRows.Count(d => d.ContextQuality == "functional") / (double)analyzedRows.Count
                    : null,
                MultipleChoiceRate: analyzedRows.Count > 0
                    ? analyzedRows.Count(d => d.QuestionType == "Çoktan Seçmeli") / (double)analyzedRows.Count
                    : null,
                AvgMaarifAlignmentScore: maarifScores.Count > 0 ? maarifScores.Average() : null,
                PoolDistribution: poolDistribution,
                DifficultyDistribution: difficultyDistribution,
                TopArchetypes: topArchetypes));
        }

        return profiles.OrderByDescending(p => p.QuestionCount).ToList();
    }
}
