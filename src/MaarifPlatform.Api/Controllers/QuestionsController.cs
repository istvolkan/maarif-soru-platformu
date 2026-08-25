using System.Text.Json;
using MaarifPlatform.Api.Dtos;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Analysis;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Api.Controllers;

/// <summary>§4/§J Analysis pipeline API yüzeyi.</summary>
[ApiController]
[Route("api/questions")]
[Authorize]
public class QuestionsController(
    MaarifDbContext db, AnalysisOrchestrationService analysisService,
    TransformationOrchestrationService transformationService, GenerationOrchestrationService generationService)
    : ControllerBase
{
    [HttpPost("generate")]
    [Authorize(Roles = "Admin,Editor")]
    public async Task<ActionResult<GenerateQuestionResponse>> Generate(GenerateQuestionApiRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<DifficultyLevel>(request.Difficulty, ignoreCase: true, out _))
        {
            return BadRequest($"Geçersiz zorluk: {request.Difficulty}. Geçerli değerler: {string.Join(", ", Enum.GetNames<DifficultyLevel>())}");
        }

        var result = await generationService.GenerateAsync(new GenerateQuestionRequest(
            request.Grade, request.Subject, request.Theme, request.LearningOutcomeCode,
            request.Difficulty, request.QuestionType, request.Context, request.ReasoningType, []), ct);

        return CreatedAtAction(nameof(GetById), new { id = result.QuestionId },
            new GenerateQuestionResponse(result.QuestionId, result.Usage.Provider, result.Usage.Model, result.Usage.CostUsd));
    }

    [HttpPost("{id:guid}/analyze")]
    [Authorize(Roles = "Admin,Editor")]
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

    [HttpPost("{id:guid}/transform")]
    [Authorize(Roles = "Admin,Editor")]
    public async Task<ActionResult<TransformationSummaryResponse>> Transform(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await transformationService.TransformAsync(id, ct);
            return Ok(new TransformationSummaryResponse(
                result.TransformationLevel, result.Decision, result.Skipped,
                result.QualityScore, result.Passed,
                result.TransformUsage?.Provider, result.TransformUsage?.Model, result.TransformUsage?.CostUsd,
                result.JudgeUsage?.Provider, result.JudgeUsage?.Model, result.JudgeUsage?.CostUsd));
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
            .Include(v => v.Distractors)
            .Where(v => v.QuestionId == id)
            .OrderByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct);

        // AlignmentScore satırları yalnızca Analyzed versiyona bağlıdır (Transform/Judge yeni
        // bir versiyon üretmez, sadece Transformed DNA'yı günceller) — bu yüzden "en son
        // versiyon" Transformed olsa bile kriter kırılımı hep Analyzed'dan ayrıca okunur.
        var analyzedVersion = await db.QuestionVersions
            .Include(v => v.AlignmentScores)
            .Where(v => v.QuestionId == id && v.Stage == QuestionVersionStage.Analyzed)
            .OrderByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct);

        var alignmentScores = (analyzedVersion?.AlignmentScores ?? [])
            .Select(a => new AlignmentScoreResponse(a.Criterion, a.Score, a.Weight, a.Explanation, a.SourceRef, a.IsCriticalGate))
            .ToList();

        var newOptions = string.IsNullOrWhiteSpace(latestVersion?.Dna?.NewOptionsJson)
            ? []
            : JsonSerializer.Deserialize<List<string>>(latestVersion!.Dna!.NewOptionsJson!) ?? [];

        var distractors = (latestVersion?.Distractors ?? [])
            .Select(d => new DistractorResponse(d.OptionLabel, d.MisconceptionCode, d.Explanation))
            .ToList();

        var (qualityFlags, criticalFailures) = SplitQualityFlags(latestVersion?.Dna?.QualityFlagsJson);

        // Passed yalnızca Judge gerçekten çalıştıysa (QualityScore set edildiyse) anlamlıdır;
        // NoChange/LightEdit kısayolunda (Judge hiç çağrılmaz) yanlış "false" izlenimi vermemek
        // için null bırakılır.
        bool? passed = latestVersion?.Dna?.QualityScore is null
            ? null
            : question.Status is QuestionStatus.AiApproved or QuestionStatus.EditorApproved or QuestionStatus.Published;

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
            alignmentScores,
            latestVersion?.Dna?.NewQuestion,
            newOptions,
            latestVersion?.Dna?.CorrectAnswer,
            latestVersion?.Dna?.Solution,
            distractors,
            latestVersion?.Dna?.QualityScore,
            passed,
            qualityFlags,
            criticalFailures);
    }

    private static (IReadOnlyList<string> QualityFlags, IReadOnlyList<string> CriticalFailures) SplitQualityFlags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ([], []);
        }

        var all = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        var critical = all.Where(f => f.StartsWith("critical:", StringComparison.Ordinal)).Select(f => f["critical:".Length..]).ToList();
        var flags = all.Where(f => !f.StartsWith("critical:", StringComparison.Ordinal)).ToList();
        return (flags, critical);
    }
}
