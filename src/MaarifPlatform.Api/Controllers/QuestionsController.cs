using MaarifPlatform.Api.Dtos;
using MaarifPlatform.Infrastructure.Analysis;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Api.Controllers;

/// <summary>§4/§J Analysis pipeline API yüzeyi. Auth/RBAC henüz yok.</summary>
[ApiController]
[Route("api/questions")]
public class QuestionsController(MaarifDbContext db, AnalysisOrchestrationService analysisService) : ControllerBase
{
    [HttpPost("{id:guid}/analyze")]
    public async Task<ActionResult<AnalysisSummaryResponse>> Analyze(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await analysisService.AnalyzeAsync(id, ct);
            return Ok(new AnalysisSummaryResponse(
                result.WeightedScore, result.TransformationLevel, result.ManualReviewRequired,
                result.GroundingChunksUsed, result.RequiresVisual, result.Usage.Provider, result.Usage.Model,
                result.Usage.InputTokens, result.Usage.OutputTokens, result.Usage.CostUsd, result.Usage.LatencyMs));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuestionDetailResponse>> GetById(Guid id, CancellationToken ct)
    {
        var question = await db.Questions.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (question is null)
        {
            return NotFound();
        }

        var latestVersion = await db.QuestionVersions
            .Include(v => v.Dna)
            .Include(v => v.AlignmentScores)
            .Where(v => v.QuestionId == id)
            .OrderByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct);

        var alignmentScores = (latestVersion?.AlignmentScores ?? [])
            .Select(a => new AlignmentScoreResponse(a.Criterion, a.Score, a.Weight, a.Explanation, a.SourceRef, a.IsCriticalGate))
            .ToList();

        return new QuestionDetailResponse(
            question.Id,
            question.QuestionNo,
            question.Status.ToString(),
            latestVersion?.Dna?.MathematicalCore,
            latestVersion?.Dna?.LearningOutcomeCode,
            latestVersion?.Dna?.MaarifAlignmentScore,
            latestVersion?.Dna?.TransformationLevel?.ToString(),
            latestVersion?.Dna?.EditorRequired ?? false,
            latestVersion?.Dna?.RequiresVisual ?? false,
            latestVersion?.Dna?.VisualType,
            latestVersion?.Dna?.VisualConfidence,
            latestVersion?.Dna?.VisualDescription,
            alignmentScores);
    }
}
