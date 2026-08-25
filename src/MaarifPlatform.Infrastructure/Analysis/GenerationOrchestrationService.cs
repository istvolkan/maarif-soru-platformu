using System.Text.Json;
using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Infrastructure.Rag;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Analysis;

public sealed record GenerationSummary(Guid QuestionId, AiUsage Usage);

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
    ILLMProvider llmProvider,
    ReferenceSearchService searchService)
{
    public async Task<GenerationSummary> GenerateAsync(GenerateQuestionRequest request, CancellationToken ct = default)
    {
        var queryText = $"{request.Theme} {request.LearningOutcomeCode} {request.Context}".Trim();
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
