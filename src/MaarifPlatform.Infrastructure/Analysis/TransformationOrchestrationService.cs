using System.Text.Json;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Rubric;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Infrastructure.Rag;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Analysis;

public sealed record TransformationSummary(
    string TransformationLevel,
    string Decision,
    bool Skipped,
    int? QualityScore,
    bool? Passed,
    AiUsage? TransformUsage,
    AiUsage? JudgeUsage);

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
    ILLMProvider llmProvider,
    ReferenceSearchService searchService)
{
    public async Task<TransformationSummary> TransformAsync(Guid questionId, CancellationToken ct = default)
    {
        var question = await db.Questions.FirstOrDefaultAsync(q => q.Id == questionId, ct)
            ?? throw new InvalidOperationException($"Soru bulunamadı: {questionId}");

        if (question.Status != QuestionStatus.Analyzed)
        {
            throw new InvalidOperationException(
                $"Soru Transform için uygun durumda değil (Status={question.Status}, Analyzed bekleniyor).");
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

        db.AiRuns.Add(BuildAiRun(questionId, PipelineStage.Transformation, transformResult.Usage));

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

        db.AiRuns.Add(BuildAiRun(questionId, PipelineStage.Judge, evalResult.Usage));

        // Judge, az önce eklenen Transformed DNA satırını yerinde günceller — Vision'ın
        // Analyzed'ı yerinde güncellemesiyle aynı desen, ayrı bir versiyon üretmez.
        dna.QualityScore = evalResult.QualityScore;
        dna.QualityFlagsJson = JsonSerializer.Serialize(
            evalResult.CriticalFailures.Select(f => $"critical:{f}").Concat(evalResult.QualityFlags));
        dna.EditorRequired = !evalResult.Passed;

        question.Status = evalResult.Passed ? QuestionStatus.AiApproved : QuestionStatus.ManualReviewRequired;
        question.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return new TransformationSummary(level.ToString(), decision.ToString(), Skipped: false,
            evalResult.QualityScore, evalResult.Passed, transformResult.Usage, evalResult.Usage);
    }

    private AiRun BuildAiRun(Guid questionId, PipelineStage stage, AiUsage usage) => new()
    {
        QuestionId = questionId,
        Stage = stage,
        ModelTier = llmProvider.Name == "local-heuristic" ? ModelTier.Cheap : ModelTier.Mid,
        Provider = usage.Provider,
        Model = usage.Model,
        InputTokens = usage.InputTokens,
        OutputTokens = usage.OutputTokens,
        CostUsd = usage.CostUsd,
        LatencyMs = usage.LatencyMs
    };
}
