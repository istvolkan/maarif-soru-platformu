using System.Text.Json;
using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Rubric;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Infrastructure.Rag;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Analysis;

public sealed record AnalysisSummary(
    int WeightedScore,
    string TransformationLevel,
    bool ManualReviewRequired,
    int GroundingChunksUsed,
    AiUsage Usage);

/// <summary>§4/§8/§A Analysis pipeline orkestrasyonu: soru yükle → RAG grounding çek →
/// ILLMProvider.AnalyzeQuestionAsync çağır → AiRun kaydet → RubricEngine ile deterministik
/// ağırlıklandır → yeni QuestionVersion/QuestionDna/AlignmentScore kayıtları → durum geçişi.
/// RAG hiç sonuç döndürmezse (§elestiri madde 1/9) skor ne olursa olsun ManualReviewRequired'a
/// düşer — grounding'siz kazanım iddiası asla Analyzed sayılmaz.</summary>
public class AnalysisOrchestrationService(
    MaarifDbContext db,
    ILLMProvider llmProvider,
    ReferenceSearchService searchService)
{
    public async Task<AnalysisSummary> AnalyzeAsync(Guid questionId, CancellationToken ct = default)
    {
        var question = await db.Questions.FirstOrDefaultAsync(q => q.Id == questionId, ct)
            ?? throw new InvalidOperationException($"Soru bulunamadı: {questionId}");

        var originalVersion = await db.QuestionVersions
            .Include(v => v.Dna)
            .Where(v => v.QuestionId == questionId && v.Stage == QuestionVersionStage.Original)
            .OrderByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Sorunun orijinal (Extracted) versiyonu bulunamadı.");

        var originalDna = originalVersion.Dna
            ?? throw new InvalidOperationException("Orijinal versiyonun Question DNA kaydı yok.");

        var options = string.IsNullOrWhiteSpace(originalDna.OriginalOptionsJson)
            ? []
            : JsonSerializer.Deserialize<List<OptionCandidate>>(originalDna.OriginalOptionsJson) ?? [];

        var grade = originalDna.Grade ?? 0;
        var subject = originalDna.Subject ?? string.Empty;

        var searchResults = await searchService.SearchAsync(
            originalDna.OriginalQuestion ?? string.Empty, topK: 5, grade: grade == 0 ? null : grade,
            subject: string.IsNullOrEmpty(subject) ? null : subject, ct: ct);

        var grounding = searchResults
            .Select(r => new GroundingReference(r.ReferenceDocumentId, r.Page, r.SectionPath, r.ChunkText))
            .ToList();

        var request = new AnalyzeQuestionRequest(
            originalDna.OriginalQuestion ?? string.Empty,
            options.Select(o => o.Text).ToList(),
            originalDna.OriginalAnswer,
            grade,
            subject,
            grounding);

        var result = await llmProvider.AnalyzeQuestionAsync(request, ct);

        db.AiRuns.Add(new AiRun
        {
            QuestionId = questionId,
            Stage = PipelineStage.Analysis,
            ModelTier = llmProvider.Name == "local-heuristic" ? ModelTier.Cheap : ModelTier.Mid,
            Provider = result.Usage.Provider,
            Model = result.Usage.Model,
            InputTokens = result.Usage.InputTokens,
            OutputTokens = result.Usage.OutputTokens,
            CostUsd = result.Usage.CostUsd,
            LatencyMs = result.Usage.LatencyMs
        });

        var rubric = RubricEngine.Evaluate(result.CriterionEvaluations);

        var nextVersionNo = await db.QuestionVersions
            .Where(v => v.QuestionId == questionId)
            .Select(v => (int?)v.VersionNo)
            .MaxAsync(ct) ?? 0;
        nextVersionNo++;

        var version = new QuestionVersion
        {
            QuestionId = questionId,
            VersionNo = nextVersionNo,
            Stage = QuestionVersionStage.Analyzed,
            PayloadJson = JsonSerializer.Serialize(result),
            CreatedBy = llmProvider.Name
        };

        var groundingInsufficient = grounding.Count == 0;
        var editorRequired = rubric.CriticalGateFailed || result.ManualReviewRequired || groundingInsufficient;

        var dna = new QuestionDna
        {
            QuestionVersion = version,
            SourceBook = originalDna.SourceBook,
            SourcePage = originalDna.SourcePage,
            Grade = originalDna.Grade,
            Subject = originalDna.Subject,
            Theme = originalDna.Theme,
            Topic = originalDna.Topic,
            Subtopic = originalDna.Subtopic,
            OriginalQuestion = originalDna.OriginalQuestion,
            OriginalOptionsJson = originalDna.OriginalOptionsJson,
            OriginalAnswer = originalDna.OriginalAnswer,
            OriginalVisualReference = originalDna.OriginalVisualReference,
            MathematicalCore = result.MathematicalCore,
            LearningOutcome = null,
            LearningOutcomeCode = result.LearningOutcomeCode,
            FieldSkill = result.FieldSkill,
            ConceptualSkill = result.ConceptualSkill,
            ContextQuality = result.ContextIsDecorative ? "decorative" : "functional",
            MaarifAlignmentScore = rubric.WeightedScore,
            AlignmentIssuesJson = JsonSerializer.Serialize(rubric.Issues),
            TransformationLevel = rubric.Level,
            QualityFlagsJson = JsonSerializer.Serialize(rubric.MissingCriteria.Select(m => $"missing_criterion:{m}")),
            EditorRequired = editorRequired,
            DnaSchemaVersion = "1.0"
        };

        db.QuestionVersions.Add(version);
        db.QuestionDnas.Add(dna);

        foreach (var evaluation in result.CriterionEvaluations)
        {
            var definition = MaarifRubric.Criteria.FirstOrDefault(c => c.Key == evaluation.Criterion);
            db.AlignmentScores.Add(new AlignmentScore
            {
                QuestionVersion = version,
                Criterion = evaluation.Criterion,
                Score = evaluation.Score,
                Weight = definition?.Weight ?? 0,
                Explanation = evaluation.Explanation,
                SourceRef = evaluation.SourceRef,
                IsCriticalGate = definition?.IsCriticalGate ?? false
            });
        }

        question.Status = editorRequired ? QuestionStatus.ManualReviewRequired : QuestionStatus.Analyzed;
        question.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return new AnalysisSummary(rubric.WeightedScore, rubric.Level.ToString(), editorRequired, grounding.Count, result.Usage);
    }
}
