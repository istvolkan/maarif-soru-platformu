using System.Text.Json;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Rubric;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Ai;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Infrastructure.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Infrastructure.Analysis;

public sealed record TransformationSummary(
    string TransformationLevel,
    string Decision,
    bool Skipped,
    int? QualityScore,
    bool? Passed,
    AiUsage? TransformUsage,
    AiUsage? JudgeUsage);

public sealed record RevisionRecommendation(int Score, TransformationLevel Level, string Suggestion);

/// <summary>§5/§6/§8 Transformation + Quality Judge orkestrasyonu: Analyzed versiyonu yükle →
/// TransformationLevel'i TransformationMode'a çevir (bkz. TransformationModeMapper) →
/// NoChange/LightEdit ise LLM'e hiç gitmeden AiApproved'a geç → aksi halde
/// ILLMProvider.TransformQuestionAsync çağır → yeni Transformed QuestionVersion/QuestionDna/
/// Distractor kayıtları → hemen ardından ILLMProvider.EvaluateQuestionAsync çağır (Judge, aynı
/// Transformed DNA satırını YERİNDE günceller — Vision'ın Analyzed'ı yerinde güncellemesiyle
/// aynı desen, ayrı bir versiyon üretmez) → durum geçişi (AiApproved/ManualReviewRequired).
/// Tek atomik çağrı: Transform'suz Judge veya Judge'suz Transform'un state machine'de bir
/// karşılığı yok.</summary>
public class TransformationOrchestrationService(
    MaarifDbContext db,
    ReferenceSearchService searchService,
    ILLMProviderFactory providerFactory,
    IOptionsMonitor<AiRoutingOptions> aiRouting,
    IOptionsMonitor<JudgeRoutingOptions> judgeRoutingOptions)
{
    public async Task<TransformationSummary> TransformAsync(Guid questionId, CancellationToken ct = default)
    {
        // Sprint 11: her ikisi de her çağrıda taze okunur — Admin Ayarlar'dan değiştirilen
        // Ai:Provider/Judge:SecondaryProvider yeniden başlatma gerektirmeden etkili olur.
        var llmProvider = providerFactory.Get(aiRouting.CurrentValue.Provider);
        var judgeRouting = judgeRoutingOptions.CurrentValue;

        var question = await db.Questions.FirstOrDefaultAsync(q => q.Id == questionId, ct)
            ?? throw new InvalidOperationException($"Soru bulunamadı: {questionId}");

        // ManualReviewRequired da kabul edilir: RecommendRevisionAsync ile Maarif Uyum Puanı >= 50
        // bulunan incelemedeki sorular, editör onayı beklemeden doğrudan Transform'a gönderilebilir
        // (bkz. BookBatchTransformService.ProcessManualReviewAsync) — geri kalan alt akış (Analyzed
        // versiyonu okuma, TransformationLevel'e göre karar) hiç değişmez.
        if (question.Status != QuestionStatus.Analyzed && question.Status != QuestionStatus.ManualReviewRequired)
        {
            throw new InvalidOperationException(
                $"Soru Transform için uygun durumda değil (Status={question.Status}, Analyzed veya ManualReviewRequired bekleniyor).");
        }

        var analyzedVersion = await db.QuestionVersions
            .Include(v => v.Dna)
            .Where(v => v.QuestionId == questionId && v.Stage == QuestionVersionStage.Analyzed)
            .OrderByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Sorunun Analyzed versiyonu bulunamadı.");

        var analyzedDna = analyzedVersion.Dna
            ?? throw new InvalidOperationException("Analyzed versiyonun Question DNA kaydı yok.");

        var level = analyzedDna.TransformationLevel
            ?? throw new InvalidOperationException("TransformationLevel hesaplanmamış.");

        var decision = TransformationModeMapper.Decide(level);

        if (decision == TransformDecision.SkipAiApprove)
        {
            question.Status = QuestionStatus.AiApproved;
            question.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            return new TransformationSummary(level.ToString(), decision.ToString(), Skipped: true,
                QualityScore: null, Passed: null, TransformUsage: null, JudgeUsage: null);
        }

        var analysisResult = JsonSerializer.Deserialize<AnalyzeQuestionResult>(analyzedVersion.PayloadJson)
            ?? throw new InvalidOperationException("Analyzed versiyonun PayloadJson'ı çözümlenemedi.");

        var searchResults = await searchService.SearchAsync(
            analyzedDna.OriginalQuestion ?? string.Empty, topK: 5,
            grade: analyzedDna.Grade is null or 0 ? null : analyzedDna.Grade,
            subject: string.IsNullOrEmpty(analyzedDna.Subject) ? null : analyzedDna.Subject, ct: ct);

        var grounding = searchResults
            .Select(r => new GroundingReference(r.ReferenceDocumentId, r.Page, r.SectionPath, r.ChunkText))
            .ToList();

        var transformRequest = new TransformQuestionRequest(
            analyzedDna.OriginalQuestion ?? string.Empty, decision.ToString(), analysisResult, grounding);
        var transformResult = await llmProvider.TransformQuestionAsync(transformRequest, ct);

        db.AiRuns.Add(BuildAiRun(questionId, PipelineStage.Transformation, transformResult.Usage, llmProvider.Name));

        var nextVersionNo = await db.QuestionVersions
            .Where(v => v.QuestionId == questionId)
            .Select(v => (int?)v.VersionNo)
            .MaxAsync(ct) ?? 0;
        nextVersionNo++;

        var version = new QuestionVersion
        {
            QuestionId = questionId,
            VersionNo = nextVersionNo,
            Stage = QuestionVersionStage.Transformed,
            PayloadJson = JsonSerializer.Serialize(transformResult),
            CreatedBy = llmProvider.Name
        };

        var dna = new QuestionDna
        {
            QuestionVersion = version,
            SourceBook = analyzedDna.SourceBook,
            SourcePage = analyzedDna.SourcePage,
            Grade = analyzedDna.Grade,
            Subject = analyzedDna.Subject,
            Theme = analyzedDna.Theme,
            Topic = analyzedDna.Topic,
            Subtopic = analyzedDna.Subtopic,
            OriginalQuestion = analyzedDna.OriginalQuestion,
            OriginalOptionsJson = analyzedDna.OriginalOptionsJson,
            OriginalAnswer = analyzedDna.OriginalAnswer,
            OriginalVisualReference = analyzedDna.OriginalVisualReference,
            MathematicalCore = analyzedDna.MathematicalCore,
            LearningOutcomeCode = analyzedDna.LearningOutcomeCode,
            FieldSkill = analyzedDna.FieldSkill,
            ConceptualSkill = analyzedDna.ConceptualSkill,
            ContextQuality = analyzedDna.ContextQuality,
            MaarifAlignmentScore = analyzedDna.MaarifAlignmentScore,
            AlignmentIssuesJson = analyzedDna.AlignmentIssuesJson,
            TransformationLevel = level,
            RequiresVisual = analyzedDna.RequiresVisual,
            VisualType = analyzedDna.VisualType,
            VisualDescription = analyzedDna.VisualDescription,
            VisualConfidence = analyzedDna.VisualConfidence,
            VisualElementsJson = analyzedDna.VisualElementsJson,
            VisualRelationsJson = analyzedDna.VisualRelationsJson,
            VisualTextJson = analyzedDna.VisualTextJson,
            VisualSymbolsJson = analyzedDna.VisualSymbolsJson,
            VisualMeasurementsJson = analyzedDna.VisualMeasurementsJson,
            VisualWarningsJson = analyzedDna.VisualWarningsJson,
            NewQuestion = transformResult.NewQuestion,
            NewOptionsJson = JsonSerializer.Serialize(transformResult.NewOptions),
            CorrectAnswer = transformResult.CorrectAnswer,
            Solution = transformResult.Solution,
            DnaSchemaVersion = "1.0"
        };

        db.QuestionVersions.Add(version);
        db.QuestionDnas.Add(dna);

        foreach (var d in transformResult.Distractors)
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

        var evalRequest = new EvaluateQuestionRequest(
            transformResult.NewQuestion, transformResult.NewOptions,
            transformResult.CorrectAnswer, transformResult.Solution, grounding);
        var evalResult = await llmProvider.EvaluateQuestionAsync(evalRequest, ct);

        db.AiRuns.Add(BuildAiRun(questionId, PipelineStage.Judge, evalResult.Usage, llmProvider.Name));

        // §8/§10 Judge Provider Disagreement: SecondaryProvider boşsa (varsayılan) bu blok hiç
        // çalışmaz, Sprint 8 davranışı bire bir korunur. Yalnızca birincilin puanı eşiğin
        // ALTINDAYKEN ikinci bir görüş alınır (Vision'ın düşük-güvende-ikinci-görüş mantığıyla
        // aynı) — birincilin YÜKSEK puanla YANLIŞ onay verdiği durumları bu mekanizma yakalamaz,
        // bu bilinçli bir sınır.
        var disagreementFlags = new List<string>();
        if (!string.IsNullOrWhiteSpace(judgeRouting.SecondaryProvider)
            && evalResult.QualityScore / 100m < judgeRouting.ConsensusConfidenceThreshold)
        {
            var secondaryProvider = providerFactory.Get(judgeRouting.SecondaryProvider);
            var secondaryEval = await secondaryProvider.EvaluateQuestionAsync(evalRequest, ct);
            db.AiRuns.Add(BuildAiRun(questionId, PipelineStage.Judge, secondaryEval.Usage, secondaryProvider.Name));
            disagreementFlags.AddRange(
                JudgeConsensusChecker.Compare(evalResult, secondaryEval, judgeRouting.ConsensusScoreDeltaThreshold));
        }

        // Judge, az önce eklenen Transformed DNA satırını yerinde günceller — Vision'ın
        // Analyzed'ı yerinde güncellemesiyle aynı desen, ayrı bir versiyon üretmez. Birincil
        // sonuç kanonik kalır (Vision'ın primary observation'ı kanonik tutmasıyla aynı ilke);
        // disagreement mesajları ayrı bir DTO alanı olmadan mevcut QualityFlags'a eklenir.
        dna.QualityScore = evalResult.QualityScore;
        dna.QualityFlagsJson = JsonSerializer.Serialize(
            evalResult.CriticalFailures.Select(f => $"critical:{f}").Concat(evalResult.QualityFlags).Concat(disagreementFlags));
        dna.EditorRequired = !evalResult.Passed || disagreementFlags.Count > 0;

        // Sağlayıcılar arası uyuşmazlık, birincil Passed=true olsa bile ManualReviewRequired'a
        // zorlar — uyuşmazlığın kendisi insan incelemesi gerektiren bir sinyaldir.
        question.Status = (evalResult.Passed && disagreementFlags.Count == 0)
            ? QuestionStatus.AiApproved : QuestionStatus.ManualReviewRequired;
        question.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return new TransformationSummary(level.ToString(), decision.ToString(), Skipped: false,
            evalResult.QualityScore, evalResult.Passed, transformResult.Usage, evalResult.Usage);
    }

    /// <summary>ManualReviewRequired'a düşmüş (henüz Transform'a girmemiş) bir soru için AI'dan
    /// aksiyona dönük bir düzeltme önerisi alır ve QuestionDna.ExtensionsJson içine yerinde yazar
    /// (§elestiri madde 12 — henüz olgunlaşmamış alan, migration gerektirmez). Skoru da döner ki
    /// çağıran (BookBatchTransformService) eşik kararını (PDF mi, Transform mi) burada tekrar
    /// sorgu atmadan verebilsin.</summary>
    public async Task<RevisionRecommendation> RecommendRevisionAsync(Guid questionId, CancellationToken ct = default)
    {
        var llmProvider = providerFactory.Get(aiRouting.CurrentValue.Provider);

        var question = await db.Questions.FirstOrDefaultAsync(q => q.Id == questionId, ct)
            ?? throw new InvalidOperationException($"Soru bulunamadı: {questionId}");

        if (question.Status != QuestionStatus.ManualReviewRequired)
        {
            throw new InvalidOperationException(
                $"Soru revizyon önerisi için uygun durumda değil (Status={question.Status}, ManualReviewRequired bekleniyor).");
        }

        var analyzedVersion = await db.QuestionVersions
            .Include(v => v.Dna)
            .Where(v => v.QuestionId == questionId && v.Stage == QuestionVersionStage.Analyzed)
            .OrderByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Sorunun Analyzed versiyonu bulunamadı.");

        var dna = analyzedVersion.Dna
            ?? throw new InvalidOperationException("Analyzed versiyonun Question DNA kaydı yok.");

        var score = dna.MaarifAlignmentScore ?? 0;
        var level = dna.TransformationLevel
            ?? throw new InvalidOperationException("TransformationLevel hesaplanmamış.");
        var issues = string.IsNullOrWhiteSpace(dna.AlignmentIssuesJson)
            ? []
            : JsonSerializer.Deserialize<List<string>>(dna.AlignmentIssuesJson) ?? [];

        var searchResults = await searchService.SearchAsync(
            dna.OriginalQuestion ?? string.Empty, topK: 5,
            grade: dna.Grade is null or 0 ? null : dna.Grade,
            subject: string.IsNullOrEmpty(dna.Subject) ? null : dna.Subject, ct: ct);
        var grounding = searchResults
            .Select(r => new GroundingReference(r.ReferenceDocumentId, r.Page, r.SectionPath, r.ChunkText))
            .ToList();

        var request = new RecommendRevisionRequest(dna.OriginalQuestion ?? string.Empty, score, issues, grounding);
        var result = await llmProvider.RecommendRevisionAsync(request, ct);

        db.AiRuns.Add(BuildAiRun(questionId, PipelineStage.Analysis, result.Usage, llmProvider.Name));

        var extensions = string.IsNullOrWhiteSpace(dna.ExtensionsJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(dna.ExtensionsJson) ?? new();
        extensions["revisionSuggestion"] = result.RevisionSuggestion;
        dna.ExtensionsJson = JsonSerializer.Serialize(extensions);

        await db.SaveChangesAsync(ct);

        return new RevisionRecommendation(score, level, result.RevisionSuggestion);
    }

    /// <summary>ManualReviewRequired'a düşen bir soru için editörün elle verdiği karar —
    /// Judge'ın otomatik AiApproved/ManualReviewRequired ayrımının insan tarafından tamamlanması.</summary>
    public async Task ReviewAsync(Guid questionId, bool approve, CancellationToken ct = default)
    {
        var question = await db.Questions.FirstOrDefaultAsync(q => q.Id == questionId, ct)
            ?? throw new InvalidOperationException($"Soru bulunamadı: {questionId}");

        if (question.Status != QuestionStatus.ManualReviewRequired)
        {
            throw new InvalidOperationException(
                $"Soru manuel inceleme için uygun durumda değil (Status={question.Status}, ManualReviewRequired bekleniyor).");
        }

        question.Status = approve ? QuestionStatus.EditorApproved : QuestionStatus.Rejected;
        question.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    private static AiRun BuildAiRun(Guid questionId, PipelineStage stage, AiUsage usage, string providerName) => new()
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
}
