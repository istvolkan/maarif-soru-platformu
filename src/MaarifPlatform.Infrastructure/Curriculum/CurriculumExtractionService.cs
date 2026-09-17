using MaarifPlatform.Application.Providers;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Ai;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Infrastructure.Curriculum;

public sealed record CurriculumExtractionResult(
    int ThemesCreated, int LearningOutcomesCreated, int ContentFrameworksCreated,
    int ProcessComponentsCreated, int FieldSkillsCreated);

/// <summary>Bir ReferenceDocument'in (zaten ingest edilmiş, chunk'lanmış) gerçek metninden
/// müfredat yapısı (Theme/LearningOutcome/ContentFramework/ProcessComponent/FieldSkill) çıkarır.
/// Sonuçlar HER ZAMAN <see cref="ApprovalStatus.Draft"/> olarak kaydedilir — LLM'in çıkardığı
/// hiçbir kayıt admin onayı olmadan cascading dropdown'larda görünmez veya üretimde kullanılmaz
/// (bkz. Admin/CurriculumReview.razor). Bu, "LLM curriculum kodu uydurmasın" kuralının ikinci
/// katmanı — birincisi ILLMProvider.ExtractCurriculumStructureAsync'in yalnızca sağlanan
/// doküman metnine (grounding) dayanması.</summary>
public class CurriculumExtractionService(
    MaarifDbContext db,
    ILLMProviderFactory providerFactory,
    IOptionsMonitor<AiRoutingOptions> aiRouting)
{
    /// <summary>Gerçek MEB dokümanları yüzlerce sayfa/chunk olabilir (ör. bu depoda zaten ingest
    /// edilmiş 772 chunk'lık bir 9. sınıf matematik kitabı) — hepsini TEK bir LLM çağrısına
    /// vermek context penceresini aşar ve tek bir dev, ucuz olmayan istek yaratır. Chunk'lar
    /// sayfa sırasıyla bu boyutta gruplara bölünüp HER grup ayrı bir extraction çağrısı olarak
    /// işlenir; sonuçlar (aynı isim/kod tekrar ederse tek kayda) birleştirilir.</summary>
    private const int ChunkBatchSize = 20;

    public async Task<CurriculumExtractionResult> ExtractAsync(Guid referenceDocumentId, CancellationToken ct = default)
    {
        var document = await db.ReferenceDocuments.FirstOrDefaultAsync(d => d.Id == referenceDocumentId, ct)
            ?? throw new InvalidOperationException($"Referans doküman bulunamadı: {referenceDocumentId}");

        if (document.Grade is null)
        {
            throw new InvalidOperationException(
                "Müfredat yapısı çıkarımı için dokümanın Sınıf alanı dolu olmalı.");
        }

        var chunks = await db.ReferenceChunks
            .Where(c => c.ReferenceDocumentId == referenceDocumentId)
            .OrderBy(c => c.Page)
            .ToListAsync(ct);

        if (chunks.Count == 0)
        {
            throw new InvalidOperationException(
                "Doküman henüz ingest edilmemiş (hiç chunk yok) — önce \"Ingest Et\" çalıştırılmalı.");
        }

        var llmProvider = providerFactory.Get(aiRouting.CurrentValue.Provider);
        var standardVersion = await FindOrCreateDefaultStandardVersionAsync(ct);

        // Aynı çalıştırma içinde farklı batch'lerden gelen aynı isimli tema/aynı kodlu kazanımı
        // TEK kayda birleştirir — her chunk grubu kendi bakış açısıyla aynı temayı görebilir.
        var themesByName = new Dictionary<string, Domain.Entities.Theme>(StringComparer.OrdinalIgnoreCase);
        var outcomesByCode = new Dictionary<string, LearningOutcome>(StringComparer.OrdinalIgnoreCase);

        // Bu SADECE bu run'ın içindeki tekrarları yakalar — aynı kazanım kodu DAHA ÖNCEKİ bir
        // extraction'dan (başka bir doküman, ör. aynı müfredatın 2. kitabı) veritabanında zaten
        // varsa yukarıdaki in-memory kontrol bunu göremez ve unique index ihlaliyle SaveChangesAsync
        // patlardı (gerçek bir MEB dokümanıyla test ederken tam olarak bu yaşandı) — o yüzden
        // mevcut kodlar/isimler baştan önceden yüklenir, "seen" kümesine dahil edilir ama ASLA
        // yeniden Add edilmez (yalnızca atlanır, zaten Approved/Draft olarak duruyorlar).
        var existingOutcomeCodes = await db.LearningOutcomes
            .Where(lo => lo.MaarifStandardVersionId == standardVersion.Id)
            .Select(lo => lo.Code)
            .ToListAsync(ct);
        foreach (var code in existingOutcomeCodes)
        {
            outcomesByCode.TryAdd(code, null!);
        }

        var existingThemes = await db.Themes
            .Where(t => t.Grade == document.Grade.Value && t.Subject == document.Subject)
            .ToListAsync(ct);
        foreach (var existingTheme in existingThemes)
        {
            themesByName.TryAdd(existingTheme.Name, existingTheme);
        }

        int themesCreated = 0, outcomesCreated = 0, frameworksCreated = 0, componentsCreated = 0, skillsCreated = 0;

        foreach (var batch in chunks.Chunk(ChunkBatchSize))
        {
            ct.ThrowIfCancellationRequested();

            var grounding = batch
                .Select(c => new GroundingReference(c.ReferenceDocumentId, c.Page, c.SectionPath, c.ChunkText))
                .ToList();

            var result = await llmProvider.ExtractCurriculumStructureAsync(
                new ExtractCurriculumRequest(document.Grade.Value, document.Subject, grounding), ct);

            foreach (var themeCandidate in result.Themes)
            {
                if (string.IsNullOrWhiteSpace(themeCandidate.Name))
                {
                    continue;
                }

                if (!themesByName.TryGetValue(themeCandidate.Name, out var theme))
                {
                    theme = new Domain.Entities.Theme
                    {
                        Grade = document.Grade.Value,
                        Subject = document.Subject,
                        Name = themeCandidate.Name,
                        MaarifStandardVersionId = standardVersion.Id,
                        SourceDocumentId = referenceDocumentId,
                        SourcePage = themeCandidate.SourcePage,
                        ApprovalStatus = ApprovalStatus.Draft
                    };
                    db.Themes.Add(theme);
                    themesByName[themeCandidate.Name] = theme;
                    themesCreated++;
                }

                foreach (var outcomeCandidate in themeCandidate.LearningOutcomes)
                {
                    if (string.IsNullOrWhiteSpace(outcomeCandidate.Code) || string.IsNullOrWhiteSpace(outcomeCandidate.Description))
                    {
                        continue;
                    }

                    if (outcomesByCode.ContainsKey(outcomeCandidate.Code))
                    {
                        continue;
                    }

                    var outcome = new LearningOutcome
                    {
                        Code = outcomeCandidate.Code,
                        Grade = document.Grade.Value,
                        Subject = document.Subject,
                        Description = outcomeCandidate.Description,
                        Theme = theme,
                        MaarifStandardVersionId = standardVersion.Id,
                        SourceDocumentId = referenceDocumentId,
                        ApprovalStatus = ApprovalStatus.Draft
                    };
                    db.LearningOutcomes.Add(outcome);
                    outcomesByCode[outcomeCandidate.Code] = outcome;
                    outcomesCreated++;

                    foreach (var frameworkName in outcomeCandidate.ContentFrameworks.Where(f => !string.IsNullOrWhiteSpace(f)))
                    {
                        db.ContentFrameworks.Add(new ContentFramework
                        {
                            LearningOutcome = outcome,
                            Name = frameworkName,
                            SourceDocumentId = referenceDocumentId,
                            SourcePage = outcomeCandidate.SourcePage,
                            ApprovalStatus = ApprovalStatus.Draft
                        });
                        frameworksCreated++;
                    }

                    foreach (var componentDescription in outcomeCandidate.ProcessComponents.Where(p => !string.IsNullOrWhiteSpace(p)))
                    {
                        db.ProcessComponents.Add(new ProcessComponent
                        {
                            LearningOutcome = outcome,
                            Description = componentDescription,
                            SourceDocumentId = referenceDocumentId,
                            SourcePage = outcomeCandidate.SourcePage,
                            ApprovalStatus = ApprovalStatus.Draft
                        });
                        componentsCreated++;
                    }
                }
            }

            foreach (var skillCandidate in result.FieldSkills)
            {
                if (string.IsNullOrWhiteSpace(skillCandidate.Code) || string.IsNullOrWhiteSpace(skillCandidate.Name))
                {
                    continue;
                }

                // Aynı dokümanın tekrar çıkarımında (idempotent yeniden deneme) veya bu çalıştırmanın
                // önceki bir batch'inde zaten eklenmiş Subject+Code çiftini tekrar Draft olarak
                // eklemez (unique index'i de korur).
                var alreadyExists = await db.FieldSkills
                    .AnyAsync(f => f.Subject == document.Subject && f.Code == skillCandidate.Code, ct)
                    || db.ChangeTracker.Entries<FieldSkill>()
                        .Any(e => e.Entity.Subject == document.Subject && e.Entity.Code == skillCandidate.Code);
                if (alreadyExists)
                {
                    continue;
                }

                db.FieldSkills.Add(new FieldSkill
                {
                    Subject = document.Subject,
                    Code = skillCandidate.Code,
                    Name = skillCandidate.Name,
                    SourceDocumentId = referenceDocumentId,
                    SourcePage = skillCandidate.SourcePage,
                    ApprovalStatus = ApprovalStatus.Draft
                });
                skillsCreated++;
            }

            db.AiRuns.Add(new AiRun
            {
                Stage = PipelineStage.CurriculumExtraction,
                ModelTier = llmProvider.Name == "local-heuristic" ? ModelTier.Cheap : ModelTier.Mid,
                Provider = result.Usage.Provider,
                Model = result.Usage.Model,
                InputTokens = result.Usage.InputTokens,
                OutputTokens = result.Usage.OutputTokens,
                CostUsd = result.Usage.CostUsd,
                LatencyMs = result.Usage.LatencyMs
            });
        }

        await db.SaveChangesAsync(ct);

        return new CurriculumExtractionResult(themesCreated, outcomesCreated, frameworksCreated, componentsCreated, skillsCreated);
    }

    private async Task<MaarifStandardVersion> FindOrCreateDefaultStandardVersionAsync(CancellationToken ct)
    {
        var existing = await db.MaarifStandardVersions.OrderBy(v => v.CreatedAt).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return existing;
        }

        var version = new MaarifStandardVersion
        {
            Code = "TYM-Guncel",
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Notes = "CurriculumExtractionService tarafından otomatik oluşturuldu (ilk müfredat çıkarımı)."
        };
        db.MaarifStandardVersions.Add(version);
        return version;
    }
}
