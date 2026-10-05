using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Application.Generation;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Storage;
using MaarifPlatform.Application.Visuals;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Ai;
using MaarifPlatform.Infrastructure.Curriculum;
using MaarifPlatform.Infrastructure.Generation;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Infrastructure.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Infrastructure.Analysis;

public sealed record GenerationSummary(Guid QuestionId, AiUsage Usage);

// Faz 1 curriculum-driven toplu üretim (§3-§15) — GenerateAsync'in (yukarıda, serbest metin/
// tek soru, POST /api/questions/generate) YANINA eklenir, onu DEĞİŞTİRMEZ.
public sealed record GenerateBatchRequest(
    int Grade,
    string Subject,
    string Theme,
    string LearningOutcomeCode,
    string LearningOutcomeDescription,
    IReadOnlyList<string> ProcessComponents,
    IReadOnlyList<string> SkillCodes,
    string DifficultySelection,
    IReadOnlyList<string> QuestionTypes,
    IReadOnlyList<string> ContentFrameworkNames,
    string Context,
    string ReasoningRequirement,
    int Count,
    string VisualUsage = "None");

public enum GenerationItemOutcome { Success, Failed }

/// <summary>§14 gerçek backend progress — her olay, o an GERÇEKTEN tamamlanmış bir pipeline
/// aşamasını temsil eder (fake animasyon yok). <see cref="Outcome"/> yalnızca bir slot
/// kesinleşince (başarı/başarısızlık) dolu gelir, ara aşama olaylarında null'dır.</summary>
public sealed record GenerationProgressEvent(
    int SlotNo, int Total, string Stage, GenerationItemOutcome? Outcome, Guid? QuestionId, string? Message,
    bool IsFinal = false);

/// <summary>§16 Yeni Soru Üretim Modülü. PDF kaynağı yoktur — LLM'in ürettiği içerik doğrudan
/// bir `Original` QuestionVersion/QuestionDna olarak kalıcılaştırılır (BookExtractionService'in
/// PDF'ten Original üretmesiyle aynı desen, yalnızca kaynak farklı). AnalysisOrchestrationService
/// Question.Status'a hiç bakmadığı, yalnızca Original versiyon+DNA varlığını aradığı için mevcut
/// Analyze/Transform pipeline'ları BURADA HİÇ DEĞİŞTİRİLMEDEN, aynen kullanılabilir — bu servis
/// yalnızca üretim + kalıcılaştırma adımını yapar, puanlama/dönüşüm/yargı yapmaz.
/// Question.BookId zorunlu (non-nullable) olduğundan üretilen sorular (Grade,Subject) başına
/// paylaşılan bir placeholder Book'a (SourceType=Generated) bağlanır — yeni migration gerekmez.</summary>
public class GenerationOrchestrationService(
    MaarifDbContext db,
    ILLMProviderFactory providerFactory,
    IOptionsMonitor<AiRoutingOptions> aiRouting,
    IOptionsMonitor<JudgeRoutingOptions> judgeRoutingOptions,
    IOptionsMonitor<GenerationRoutingOptions> generationRoutingOptions,
    IOptionsMonitor<Dictionary<string, string?>> difficultyRouting,
    ReferenceSearchService searchService,
    CurriculumQueryService curriculumQuery,
    QuestionSimilarityService similarityService,
    IBookFileStorage storage)
{
    public async Task<GenerationSummary> GenerateAsync(GenerateQuestionRequest request, CancellationToken ct = default)
    {
        // Sprint 11: her çağrıda taze okunur — Admin Ayarlar'dan değiştirilen Ai:Provider
        // yeniden başlatma gerektirmeden etkili olur.
        var llmProvider = providerFactory.Get(aiRouting.CurrentValue.Provider);

        var queryText = $"{request.Theme} {request.LearningOutcomeDescription} {request.Context}".Trim();
        var searchResults = await searchService.SearchAsync(
            queryText, topK: 5, grade: request.Grade == 0 ? null : request.Grade,
            subject: string.IsNullOrEmpty(request.Subject) ? null : request.Subject, ct: ct);

        var grounding = searchResults
            .Select(r => new GroundingReference(r.ReferenceDocumentId, r.Page, r.SectionPath, r.ChunkText))
            .ToList();

        var result = await llmProvider.GenerateQuestionAsync(request with { Grounding = grounding }, ct);

        var book = await FindOrCreatePlaceholderBookAsync(request.Grade, request.Subject, ct);

        var question = new Question { BookId = book.Id, Status = QuestionStatus.Extracted };

        var version = new QuestionVersion
        {
            Question = question,
            QuestionId = question.Id,
            VersionNo = 1,
            Stage = QuestionVersionStage.Original,
            PayloadJson = JsonSerializer.Serialize(result),
            CreatedBy = llmProvider.Name
        };

        var options = result.Options
            .Select((text, i) => new OptionCandidate(((char)('A' + i)).ToString(), text))
            .ToList();

        var dna = new QuestionDna
        {
            QuestionVersion = version,
            QuestionVersionId = version.Id,
            SourceBook = book.Title,
            Grade = request.Grade,
            Subject = request.Subject,
            Theme = request.Theme,
            OriginalQuestion = result.Question,
            OriginalOptionsJson = JsonSerializer.Serialize(options),
            OriginalAnswer = result.CorrectAnswer,
            DnaSchemaVersion = "1.0"
        };

        db.Questions.Add(question);
        db.QuestionVersions.Add(version);
        db.QuestionDnas.Add(dna);

        foreach (var d in result.Distractors)
        {
            db.Distractors.Add(new Distractor
            {
                QuestionVersion = version,
                OptionLabel = d.OptionLabel,
                MisconceptionCode = d.MisconceptionCode,
                Explanation = d.Explanation,
                IsHypothesis = true
            });
        }

        db.AiRuns.Add(new AiRun
        {
            QuestionId = question.Id,
            Stage = PipelineStage.Generation,
            ModelTier = llmProvider.Name == "local-heuristic" ? ModelTier.Cheap : ModelTier.Mid,
            Provider = result.Usage.Provider,
            Model = result.Usage.Model,
            InputTokens = result.Usage.InputTokens,
            OutputTokens = result.Usage.OutputTokens,
            CostUsd = result.Usage.CostUsd,
            LatencyMs = result.Usage.LatencyMs
        });

        await db.SaveChangesAsync(ct);

        return new GenerationSummary(question.Id, result.Usage);
    }

    /// <summary>§3-§15 curriculum-driven toplu üretim. Her blueprint slotu İÇİN: Generator →
    /// Curriculum Validator → Judge (+ mevcut çapraz-sağlayıcı consensus) → Benzerlik kontrolü;
    /// biri FAIL olursa slot en fazla <see cref="GenerationRoutingOptions.MaxRegenerationAttempts"/>
    /// kez yeniden denenir, tükenirse düşük kaliteli bir soruyla ASLA doldurulmaz (§15 fail-safe).
    /// Her aşama GERÇEKTEN tamamlandığında bir <see cref="GenerationProgressEvent"/> yield edilir —
    /// BookBatchTransformService.ProcessManualReviewAsync ile AYNI IAsyncEnumerable deseni.</summary>
    public async IAsyncEnumerable<GenerationProgressEvent> GenerateBatchAsync(
        GenerateBatchRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // §8 hallucination control kapısı: onaylanmış curriculum verisi yoksa üretim HİÇ başlamaz.
        var hasCurriculum = await curriculumQuery.HasApprovedCurriculumAsync(request.Grade, request.Subject, ct);
        if (!hasCurriculum)
        {
            yield return new GenerationProgressEvent(0, request.Count, "Durduruldu", GenerationItemOutcome.Failed, null,
                "Bu kombinasyon için doğrulanmış öğretim programı verisi bulunamadı.", IsFinal: true);
            yield break;
        }

        yield return new GenerationProgressEvent(0, request.Count, "Öğretim programı doğrulandı, sorular oluşturuluyor…", null, null, null);

        // Sprint 11 canlı-yeniden-yükleme deseni: her batch başında bir kez okunur (blueprint
        // tamamlanana kadar tutarlı kalması için — Ayarlar ortasında değişirse bir sonraki
        // "Üret" çağrısında etkili olur).
        var generationRouting = generationRoutingOptions.CurrentValue;
        var judgeRouting = judgeRoutingOptions.CurrentValue;

        // §57/§58 Faz 6 — Pattern Library'den (Faz 3) bu Grade+Subject için en iyi adayları
        // çek: PatternScorer admin'in MaarifWeight ayarına göre MaarifAffinity'yi ağırlıklandırır,
        // SourceSupportCount (§54: kaç bağımsız soruda gözlemlendi) ikincil sıralama kriteridir —
        // tek bir sorudan doğmuş, hiç doğrulanmamış bir archetype yüksek affinity'yle bile öne
        // çıkmasın diye. Faz 3 verisi henüz yoksa (hiç kitap analiz edilmemişse) boş liste döner —
        // GenerationBlueprintBuilder bunu geriye dönük uyumlu şekilde "hint yok" olarak işler.
        var archetypeHints = await db.QuestionArchetypes
            .Where(a => a.Subject == request.Subject && a.GradeRangeMin <= request.Grade && a.GradeRangeMax >= request.Grade)
            .Select(a => new { a.Name, a.MaarifAffinity, a.SourceSupportCount })
            .ToListAsync(ct);
        var rankedArchetypeHints = archetypeHints
            .OrderByDescending(a => PatternScorer.Score(a.MaarifAffinity, generationRouting.MaarifWeight))
            .ThenByDescending(a => a.SourceSupportCount)
            .Select(a => a.Name)
            .Take(Math.Min(5, request.Count))
            .ToList();

        IReadOnlyList<GenerationBlueprintItem>? blueprint = null;
        string? blueprintError = null;
        try
        {
            blueprint = GenerationBlueprintBuilder.Build(
                request.Count, request.DifficultySelection, request.QuestionTypes, request.ContentFrameworkNames,
                rankedArchetypeHints);
        }
        catch (ArgumentException ex)
        {
            blueprintError = ex.Message;
        }

        if (blueprint is null)
        {
            yield return new GenerationProgressEvent(0, request.Count, "Durduruldu", GenerationItemOutcome.Failed, null, blueprintError, IsFinal: true);
            yield break;
        }

        var maxAttempts = Math.Max(1, generationRouting.MaxRegenerationAttempts);
        var defaultCurriculumValidatorProviderName = string.IsNullOrWhiteSpace(generationRouting.CurriculumValidatorProvider)
            ? aiRouting.CurrentValue.Provider
            : generationRouting.CurriculumValidatorProvider;

        var succeeded = 0;
        var failed = 0;

        for (var i = 0; i < blueprint.Count; i++)
        {
            var slotNo = i + 1;
            var item = blueprint[i];
            var attemptMessages = new List<string>();
            GenerateQuestionResult? accepted = null;
            string? acceptedSvg = null;

            // §16 zorluk bazlı yönlendirme: bu slotun DifficultyLevel'ine göre Generator/
            // CurriculumValidator/Judge'ın ÜÇÜ de AYRI bir (sağlayıcı, model) çiftine
            // yönlendirilebilir (ör. Kolay→OpenAI gpt-6-luna, Zor→Anthropic claude-opus-4-8) —
            // hiçbiri Admin Ayarlar'da override edilmemişse mevcut global varsayılanlara düşülür.
            var (llmProvider, generatorModel) = ResolveRoute("Generation", item.Difficulty, aiRouting.CurrentValue.Provider);
            var (curriculumValidatorProvider, curriculumValidatorModel) = ResolveRoute(
                "CurriculumValidation", item.Difficulty, defaultCurriculumValidatorProviderName);
            var (judgeProvider, judgeModel) = ResolveRoute("Judge", item.Difficulty, aiRouting.CurrentValue.Provider);
            var (solverProvider, solverModel) = ResolveRoute("IndependentSolver", item.Difficulty, aiRouting.CurrentValue.Provider);

            for (var attempt = 1; attempt <= maxAttempts && accepted is null; attempt++)
            {
                yield return new GenerationProgressEvent(slotNo, blueprint.Count,
                    $"Soru {slotNo}/{blueprint.Count} oluşturuluyor (deneme {attempt}/{maxAttempts})…", null, null, null);

                // §RAG: sorgu metni kazanımın gerçek AÇIKLAMASINI içermeli — yalnızca Tema adı +
                // opaque kod (ör. "MAT.9.4.1") kullanmak, aynı temadaki farklı alt-kazanımları
                // (ör. "Eşlik ve Benzerlik" teması altındaki hem Öklid bağıntıları hem de
                // geometrik dönüşümler alt kazanımlarını) semantik olarak ayırt edemiyor ve
                // yanlış chunk'lar grounding olarak geliyordu (canlı testte gözlemlendi).
                var queryText = $"{request.Theme} {request.LearningOutcomeDescription} {request.Context}".Trim();
                var searchResults = await searchService.SearchAsync(
                    queryText, topK: 5, grade: request.Grade, subject: request.Subject, ct: ct);
                var grounding = searchResults
                    .Select(r => new GroundingReference(r.ReferenceDocumentId, r.Page, r.SectionPath, r.ChunkText))
                    .ToList();

                // §9 maliyet ilkesi: kör bir tekrar yerine, önceki denemenin GERÇEK ret gerekçesini
                // Generator'a somut düzeltme talimatı olarak ver — attempt 2'nin isabet oranını
                // artırır (bkz. AnthropicLLMProvider.BuildGenerateSystemPrompt), aksi halde aynı
                // hatayla ikinci kez de reddedilip Generation+CurriculumValidation çağrısı boşa gider.
                var generateRequest = new GenerateQuestionRequest(
                    request.Grade, request.Subject, request.Theme, request.LearningOutcomeCode,
                    item.Difficulty.ToString(), item.QuestionType, request.Context, request.ReasoningRequirement,
                    grounding, request.SkillCodes,
                    item.ContentFramework is null ? [] : [item.ContentFramework],
                    request.ProcessComponents, request.VisualUsage, request.LearningOutcomeDescription,
                    PreviousAttemptFeedback: attemptMessages.Count > 0 ? attemptMessages[^1] : null,
                    ModelOverride: generatorModel,
                    ArchetypeHint: item.ArchetypeHint);

                GenerateQuestionResult generated;
                try
                {
                    generated = await llmProvider.GenerateQuestionAsync(generateRequest, ct);
                }
                catch (Exception ex)
                {
                    attemptMessages.Add($"Üretim hatası: {ex.Message}");
                    continue;
                }

                db.AiRuns.Add(BuildAiRun(null, PipelineStage.Generation, generated.Usage, llmProvider.Name));

                // Şık adedi Grade'e göre sabit bir kural (bkz. MultipleChoiceOptionPolicy) — prompt/
                // şema seviyesinde sağlayıcıya zaten bildirilir, ama bu asıl GARANTİ kapısı: LLM
                // (veya mock) kurala uymazsa slot diğer kalite kontrolleri gibi regenerate edilir,
                // yanlış şık adediyle ASLA kalıcılaştırılmaz.
                //
                // Bilinçli olarak item.QuestionType == "Çoktan Seçmeli" EŞLEŞMESİNE değil,
                // generated.Options.Count > 0'a (LLM'in GERÇEKTEN şıklı ürettiğine) bakılır —
                // "Tablo Yorumlama"/"Grafik Yorumlama"/"Görsel Yorumlama"/"Senaryo Temelli"/
                // "Problem Temelli" gibi tipler açıkça "Çoktan Seçmeli" ETİKETLİ olmasa da LLM
                // bunları sıkça şıklı üretiyor (şema bu tiplere genel "3-6 şık" izni veriyor,
                // Grade'e özel sabit sayı değil) — eski kod bu durumda hiç kontrol etmediği için
                // lise sorularında 4 ile 5 arası karışık şık sayısı kalıcılaşıyordu (gerçek
                // kullanıcı raporu, 2026-10). Açık uçlu/eşleştirme gibi GERÇEKTEN şıksız tipler
                // zaten Options.Count==0 döner, bu kontrolden hiç etkilenmez.
                if (generated.Options.Count > 0)
                {
                    var requiredCount = MultipleChoiceOptionPolicy.RequiredOptionCount(request.Grade);
                    if (generated.Options.Count != requiredCount)
                    {
                        attemptMessages.Add(
                            $"Şıklı (çoktan seçmeli biçimli) soru için {requiredCount} şık üretilmeliydi, {generated.Options.Count} şık üretildi.");
                        continue;
                    }
                }

                yield return new GenerationProgressEvent(slotNo, blueprint.Count,
                    $"Soru {slotNo}/{blueprint.Count}: öğretim programı uyumu kontrol ediliyor…", null, null, null);

                CurriculumAlignmentResult alignment;
                try
                {
                    alignment = await curriculumValidatorProvider.ValidateCurriculumAlignmentAsync(
                        new ValidateCurriculumAlignmentRequest(
                            generated.Question, request.LearningOutcomeCode, request.LearningOutcomeDescription,
                            request.ProcessComponents, ModelOverride: curriculumValidatorModel), ct);
                }
                catch (Exception ex)
                {
                    attemptMessages.Add($"Curriculum Validator hatası: {ex.Message}");
                    continue;
                }

                db.AiRuns.Add(BuildAiRun(null, PipelineStage.CurriculumValidation, alignment.Usage, curriculumValidatorProvider.Name));

                if (!alignment.MeasuresProcessComponent || alignment.LearningOutcomeAlignmentScore < 50)
                {
                    attemptMessages.Add(
                        $"Kazanım/süreç bileşeni uyumu yetersiz (skor {alignment.LearningOutcomeAlignmentScore}/100)." +
                        (alignment.Issues.Count > 0 ? " " + string.Join(" ", alignment.Issues) : ""));
                    continue;
                }

                yield return new GenerationProgressEvent(slotNo, blueprint.Count,
                    $"Soru {slotNo}/{blueprint.Count}: matematiksel doğruluk kontrol ediliyor…", null, null, null);

                var evalRequest = new EvaluateQuestionRequest(
                    generated.Question, generated.Options, generated.CorrectAnswer, generated.Solution, grounding,
                    ModelOverride: judgeModel);

                EvaluateQuestionResult evalResult;
                try
                {
                    evalResult = await judgeProvider.EvaluateQuestionAsync(evalRequest, ct);
                }
                catch (Exception ex)
                {
                    attemptMessages.Add($"Judge hatası: {ex.Message}");
                    continue;
                }

                db.AiRuns.Add(BuildAiRun(null, PipelineStage.Judge, evalResult.Usage, judgeProvider.Name));

                // §8/§10 ile AYNI çapraz-sağlayıcı consensus deseni (bkz. TransformationOrchestrationService).
                var disagreementFlags = new List<string>();
                if (!string.IsNullOrWhiteSpace(judgeRouting.SecondaryProvider)
                    && evalResult.QualityScore / 100m < judgeRouting.ConsensusConfidenceThreshold)
                {
                    var secondaryProvider = providerFactory.Get(judgeRouting.SecondaryProvider);
                    var secondaryEval = await secondaryProvider.EvaluateQuestionAsync(evalRequest, ct);
                    db.AiRuns.Add(BuildAiRun(null, PipelineStage.Judge, secondaryEval.Usage, secondaryProvider.Name));
                    disagreementFlags.AddRange(
                        JudgeConsensusChecker.Compare(evalResult, secondaryEval, judgeRouting.ConsensusScoreDeltaThreshold));
                }

                if (!evalResult.Passed || disagreementFlags.Count > 0)
                {
                    attemptMessages.Add(evalResult.CriticalFailures.Count > 0
                        ? $"Kalite kontrolünden geçemedi: {string.Join(" ", evalResult.CriticalFailures)}"
                        : disagreementFlags.Count > 0
                            ? $"Sağlayıcılar arasında uyuşmazlık: {string.Join(" ", disagreementFlags)}"
                            : "Kalite kontrolünden geçemedi.");
                    continue;
                }

                // §61 Independent Solver — Generator'ın KENDİ iddia ettiği cevabı güvenilir kabul
                // etmek yerine, ayrı (routing'e göre farklı sağlayıcı/model olabilen) bir "çözücü"
                // soruyu Generator'ın cevabı/çözümü hiç verilmeden sıfırdan çözer; sonuç uyuşmazsa
                // slot diğer kalite kapıları gibi regenerate edilir. Yalnızca GERÇEKTEN şıklı
                // sorularda anlamlı (bkz. SolveQuestionRequest doc) — açık uçlu sorularda serbest
                // metin eşitliği güvenilir bir sinyal değildir, bu yüzden Options.Count>0 şartı var.
                if (generated.Options.Count > 0)
                {
                    yield return new GenerationProgressEvent(slotNo, blueprint.Count,
                        $"Soru {slotNo}/{blueprint.Count}: bağımsız çözüm doğrulanıyor…", null, null, null);

                    SolveQuestionResult solveResult;
                    try
                    {
                        solveResult = await solverProvider.SolveQuestionAsync(
                            new SolveQuestionRequest(generated.Question, generated.Options, solverModel), ct);
                    }
                    catch (Exception ex)
                    {
                        attemptMessages.Add($"Bağımsız çözüm hatası: {ex.Message}");
                        continue;
                    }

                    db.AiRuns.Add(BuildAiRun(null, PipelineStage.IndependentSolve, solveResult.Usage, solverProvider.Name));

                    if (!string.Equals(solveResult.Answer.Trim(), generated.CorrectAnswer.Trim(), StringComparison.Ordinal))
                    {
                        attemptMessages.Add(
                            $"Bağımsız çözücü farklı bir sonuca ulaştı (Generator: \"{generated.CorrectAnswer}\", " +
                            $"Çözücü: \"{solveResult.Answer}\") — cevap hatalı olabilir.");
                        continue;
                    }
                }

                yield return new GenerationProgressEvent(slotNo, blueprint.Count,
                    $"Soru {slotNo}/{blueprint.Count}: benzerlik kontrol ediliyor…", null, null, null);

                var similarity = await similarityService.CheckAsync(
                    request.Grade, request.Subject, generated.Question, generationRouting.SimilarityRejectThreshold, ct);
                if (similarity.IsDuplicate)
                {
                    // §52 Orijinallik Kontrolü: hangi havuza (kaynak kitap mı, önceki üretim mi)
                    // çok benzediği ayrı belirtilir — kaynak kitaba benzerlik daha ciddi bir
                    // kopyalama riskidir (bkz. QuestionEmbeddingSourceKind).
                    var sourceDescription = similarity.MostSimilarSourceKind == QuestionEmbeddingSourceKind.Extracted
                        ? "kaynak kitaplardan çıkarılmış bir soruya"
                        : "havuzdaki daha önce üretilmiş bir soruya";
                    attemptMessages.Add(
                        $"{sourceDescription} çok benziyor (benzerlik %{similarity.MostSimilarScore * 100:F0}) — " +
                        "yalnızca sayı/isim değiştirilmiş bir kopya olabilir.");
                    continue;
                }

                // §6 Görsel Soru Motoru (Faz 2) — LLM burada YALNIZCA bir tarif (visual_spec)
                // üretmiştir; gerçek görsel VisualSpecRenderer tarafından deterministik olarak
                // render edilir. Render başarısız olursa (geçersiz/tutarsız spec) bu da diğer
                // kontroller gibi regenerate'e düşer — sahte/placeholder bir görselle ASLA devam
                // edilmez.
                string? renderedSvg = null;
                if (generated.VisualRequired)
                {
                    if (generated.VisualSpec is null)
                    {
                        attemptMessages.Add("visual_required=true ama visual_spec eksik.");
                        continue;
                    }

                    yield return new GenerationProgressEvent(slotNo, blueprint.Count,
                        $"Soru {slotNo}/{blueprint.Count}: görsel oluşturuluyor…", null, null, null);

                    try
                    {
                        renderedSvg = VisualSpecRenderer.RenderToSvg(generated.VisualSpec);
                    }
                    catch (Exception ex)
                    {
                        attemptMessages.Add($"Görsel üretilemedi: {ex.Message}");
                        continue;
                    }
                }

                accepted = generated;
                acceptedSvg = renderedSvg;
            }

            if (accepted is null)
            {
                failed++;
                // Başarısız denemelerin AiRun kayıtları (Generation/CurriculumValidation/Judge, hepsi
                // QuestionId=null) döngü içinde eklendi ama henüz flush edilmedi — bir sonraki slot
                // başarılı olursa onun SaveChangesAsync'iyle birlikte yazılırlardı; TÜM batch başarısız
                // olursa (ör. anahtar hatası) maliyet hiç kaydedilmeden kaybolmasın diye burada kaydet.
                await db.SaveChangesAsync(ct);
                yield return new GenerationProgressEvent(slotNo, blueprint.Count,
                    "Başarısız", GenerationItemOutcome.Failed, null, string.Join(" ", attemptMessages));
                continue;
            }

            var (question, version) = await PersistGeneratedQuestionAsync(request, item, accepted, acceptedSvg, llmProvider.Name, ct);
            await similarityService.RecordAsync(
                version.Id, request.Grade, request.Subject, accepted.Question, QuestionEmbeddingSourceKind.Generated, ct);

            succeeded++;
            yield return new GenerationProgressEvent(slotNo, blueprint.Count,
                "Tamamlandı", GenerationItemOutcome.Success, question.Id, null);
        }

        yield return new GenerationProgressEvent(blueprint.Count, blueprint.Count,
            failed == 0
                ? $"Soru havuzu hazırlandı: {succeeded}/{blueprint.Count} soru tamamlandı."
                : $"{blueprint.Count} sorudan {succeeded}'i kalite kontrollerini geçti. {failed} soru başarısız oldu " +
                  "(düşük kaliteyle doldurulmadı) — isterseniz tekrar deneyin.",
            null, null, null, IsFinal: true);
    }

    private async Task<(Question Question, QuestionVersion Version)> PersistGeneratedQuestionAsync(
        GenerateBatchRequest request, GenerationBlueprintItem item, GenerateQuestionResult result,
        string? renderedSvg, string providerName, CancellationToken ct)
    {
        var book = await FindOrCreatePlaceholderBookAsync(request.Grade, request.Subject, ct);

        // GenerateAsync'in (eski, doğrulamasız tek-soru API'si) aksine bu yol yalnızca Curriculum
        // Validator + Judge'ı GEÇMİŞ sorular için çağrılır — durum bu yüzden "Extracted" değil,
        // doğrudan AiApproved (Transform pipeline'ının Judge geçince yaptığıyla aynı anlam:
        // otomatik doğrulamadan geçti, yalnızca insan "Onayla"sı bekliyor).
        var question = new Question { BookId = book.Id, Status = QuestionStatus.AiApproved };

        var version = new QuestionVersion
        {
            Question = question,
            QuestionId = question.Id,
            VersionNo = 1,
            Stage = QuestionVersionStage.Original,
            PayloadJson = JsonSerializer.Serialize(result),
            CreatedBy = providerName
        };

        var options = result.Options
            .Select((text, i) => new OptionCandidate(((char)('A' + i)).ToString(), text))
            .ToList();

        var dna = new QuestionDna
        {
            QuestionVersion = version,
            QuestionVersionId = version.Id,
            SourceBook = book.Title,
            Grade = request.Grade,
            Subject = request.Subject,
            Theme = request.Theme,
            LearningOutcomeCode = request.LearningOutcomeCode,
            LearningOutcome = request.LearningOutcomeDescription,
            Difficulty = item.Difficulty,
            QuestionType = item.QuestionType,
            Topic = item.ContentFramework,
            OriginalQuestion = result.Question,
            OriginalOptionsJson = JsonSerializer.Serialize(options),
            OriginalAnswer = result.CorrectAnswer,
            // "Dönüşüm çıktısı" (New*/CorrectAnswer/Solution) alanları normalde yalnızca Transform'un
            // doldurduğu alanlardır, ama bu akışın hiç Transform adımı yoktur (LLM'in ürettiği içerik
            // zaten nihai) — CorrectAnswer/Solution zaten burada dolduruluyordu, NewQuestion/
            // NewOptionsJson da AYNI NEDENLE burada doldurulmalı; aksi halde "Soru Detayı" sayfası
            // (QuestionsController.GetById, yalnızca New* okur) ve PDF export (2026-10'da Original*'a
            // düşmeyi öğrendi, ama kaynakta da tutarlı olmak daha sağlam) bu soruları boş gösterir.
            NewQuestion = result.Question,
            NewOptionsJson = JsonSerializer.Serialize(result.Options),
            Solution = result.Solution,
            CorrectAnswer = result.CorrectAnswer,
            DnaSchemaVersion = "1.0",
            RequiresVisual = renderedSvg is not null,
            VisualType = renderedSvg is not null ? result.VisualSpec?.Type : null
        };

        db.Questions.Add(question);
        db.QuestionVersions.Add(version);
        db.QuestionDnas.Add(dna);

        if (renderedSvg is not null)
        {
            var svgBytes = System.Text.Encoding.UTF8.GetBytes(renderedSvg);
            var storageUri = await storage.SaveAsync(question.Id, "visual.svg", new MemoryStream(svgBytes), ct);
            db.QuestionVisualAssets.Add(new QuestionVisualAsset
            {
                QuestionId = question.Id,
                StorageUri = storageUri,
                ContentType = "image/svg+xml",
                AssetHash = Convert.ToHexString(SHA256.HashData(svgBytes))
            });
        }

        foreach (var d in result.Distractors)
        {
            db.Distractors.Add(new Distractor
            {
                QuestionVersion = version,
                OptionLabel = d.OptionLabel,
                MisconceptionCode = d.MisconceptionCode,
                Explanation = d.Explanation,
                IsHypothesis = true
            });
        }

        // Bu slotun Generation/CurriculumValidation/Judge AiRun'ları kabul edilen denemede zaten
        // GenerateBatchAsync döngüsünde eklendi (QuestionId=null) — burada tekrar eklemek maliyeti
        // ÇİFT SAYARDI. Bilinçli sınır: kabul edilen üretim çağrısının AiRun'ı bu yüzden nihai
        // soruya değil (QuestionId=null olarak) kalır; toplam maliyet doğru, yalnızca soru bazlı
        // maliyet dökümü regenerasyon olan slotlarda tam hassas değildir.
        await db.SaveChangesAsync(ct);

        return (question, version);
    }

    /// <summary>§16 zorluk bazlı model yönlendirme. <paramref name="stage"/> Admin Ayarlar'daki
    /// üç adlandırılmış routing tablosundan ("Generation"/"CurriculumValidation"/"Judge") birine
    /// karşılık gelir; anahtar <see cref="DifficultyLevel"/>.ToString(), değer "Sağlayıcı:Model"
    /// (ör. "OpenAI:gpt-6-luna") ya da boş. Override yoksa <paramref name="fallbackProviderName"/>
    /// ile mevcut varsayılan davranışa (model override'sız, sağlayıcının kendi Options.Model'i)
    /// düşülür.</summary>
    private (ILLMProvider Provider, string? Model) ResolveRoute(string stage, DifficultyLevel difficulty, string fallbackProviderName)
    {
        var routing = difficultyRouting.Get(stage);
        if (routing.TryGetValue(difficulty.ToString(), out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            var parts = raw.Split(':', 2);
            var providerName = parts[0].Trim();
            var model = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim() : null;
            return (providerFactory.Get(providerName), model);
        }

        return (providerFactory.Get(fallbackProviderName), null);
    }

    private static AiRun BuildAiRun(Guid? questionId, PipelineStage stage, AiUsage usage, string providerName) => new()
    {
        QuestionId = questionId,
        Stage = stage,
        ModelTier = providerName == "local-heuristic" ? ModelTier.Cheap : ModelTier.Mid,
        Provider = usage.Provider,
        Model = usage.Model,
        InputTokens = usage.InputTokens,
        OutputTokens = usage.OutputTokens,
        CostUsd = usage.CostUsd,
        LatencyMs = usage.LatencyMs
    };

    private async Task<Book> FindOrCreatePlaceholderBookAsync(int grade, string subject, CancellationToken ct)
    {
        var existing = await db.Books.FirstOrDefaultAsync(
            b => b.SourceType == SourceType.Generated && b.Grade == grade && b.Subject == subject, ct);
        if (existing is not null)
        {
            return existing;
        }

        var book = new Book
        {
            Title = $"AI Üretilen Sorular — {grade}. Sınıf {subject}",
            Grade = grade,
            Subject = subject,
            SourceType = SourceType.Generated,
            StorageUri = string.Empty
        };
        db.Books.Add(book);
        return book;
    }
}
