using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MaarifPlatform.Infrastructure.Analysis;

public enum BatchQuestionOutcome
{
    AlreadyDone,
    Succeeded,
    NeedsReview,
    Rejected,
    Failed
}

public sealed record BatchQuestionResult(Guid QuestionId, int? QuestionNo, BatchQuestionOutcome Outcome, string? Message);

/// <summary>Bir kitaptaki tüm soruları toplu olarak Analyze veya Analyze→Transform akışından
/// geçirir. Zaten sonuçlanmış sorulara dokunmaz — idempotent, tekrar çalıştırılabilir. Bir
/// sorudaki hata (LLM hatası, grounding yokluğu vb.) tüm batch'i durdurmaz; sonraki soruyla
/// devam edilir. Her soru kendi IServiceScopeFactory scope'unda işlenir —
/// AnalysisOrchestrationService/TransformationOrchestrationService Scoped kayıtlı olduğu için
/// uzun bir döngü boyunca tek bir DbContext paylaşılmaz.</summary>
public class BookBatchTransformService(MaarifDbContext db, IServiceScopeFactory scopeFactory)
{
    /// <summary>Yalnızca Analyze çalıştırır, Transform'a hiç geçmez — Extracted durumundaki
    /// sorular Analyzed/ManualReviewRequired'a taşınır. Analyzed ve sonrası durumdaki sorular
    /// AlreadyDone sayılır.</summary>
    public IAsyncEnumerable<BatchQuestionResult> AnalyzeBookAsync(Guid bookId, CancellationToken ct = default) =>
        RunBatchAsync(bookId, runTransform: false, ct);

    /// <summary>Extracted→Analyze, Analyzed→Transform akışının tamamını çalıştırır.</summary>
    public IAsyncEnumerable<BatchQuestionResult> TransformBookAsync(Guid bookId, CancellationToken ct = default) =>
        RunBatchAsync(bookId, runTransform: true, ct);

    private async IAsyncEnumerable<BatchQuestionResult> RunBatchAsync(
        Guid bookId, bool runTransform, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var questionIds = await db.Questions
            .Where(q => q.BookId == bookId)
            .OrderBy(q => q.QuestionNo)
            .Select(q => q.Id)
            .ToListAsync(ct);

        foreach (var questionId in questionIds)
        {
            ct.ThrowIfCancellationRequested();
            yield return await ProcessQuestionAsync(questionId, runTransform, ct);
        }
    }

    private async Task<BatchQuestionResult> ProcessQuestionAsync(Guid questionId, bool runTransform, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<MaarifDbContext>();

        var question = await scopedDb.Questions.FirstOrDefaultAsync(q => q.Id == questionId, ct);
        if (question is null)
            return new BatchQuestionResult(questionId, null, BatchQuestionOutcome.Failed, "Soru bulunamadı.");

        var alreadyDone = runTransform
            ? question.Status is QuestionStatus.AiApproved or QuestionStatus.EditorApproved
                or QuestionStatus.Published or QuestionStatus.Rejected
            : question.Status != QuestionStatus.Extracted;

        if (alreadyDone)
            return new BatchQuestionResult(questionId, question.QuestionNo, BatchQuestionOutcome.AlreadyDone, null);

        try
        {
            if (question.Status == QuestionStatus.Extracted)
            {
                var analysisService = scope.ServiceProvider.GetRequiredService<AnalysisOrchestrationService>();
                await analysisService.AnalyzeAsync(questionId, ct);
                await scopedDb.Entry(question).ReloadAsync(ct);
            }

            if (runTransform && question.Status == QuestionStatus.Analyzed)
            {
                var transformationService = scope.ServiceProvider.GetRequiredService<TransformationOrchestrationService>();
                await transformationService.TransformAsync(questionId, ct);
                await scopedDb.Entry(question).ReloadAsync(ct);
            }

            var outcome = question.Status switch
            {
                QuestionStatus.AiApproved or QuestionStatus.EditorApproved or QuestionStatus.Published
                    => BatchQuestionOutcome.Succeeded,
                QuestionStatus.Analyzed when !runTransform => BatchQuestionOutcome.Succeeded,
                QuestionStatus.Rejected => BatchQuestionOutcome.Rejected,
                _ => BatchQuestionOutcome.NeedsReview
            };
            return new BatchQuestionResult(questionId, question.QuestionNo, outcome, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new BatchQuestionResult(questionId, question.QuestionNo, BatchQuestionOutcome.Failed, ex.Message);
        }
    }
}
