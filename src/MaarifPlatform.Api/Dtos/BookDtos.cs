using System.ComponentModel.DataAnnotations;

namespace MaarifPlatform.Api.Dtos;

public class CreateBookRequest
{
    [Required] public string Title { get; set; } = string.Empty;
    public int? Grade { get; set; }
    [Required] public string Subject { get; set; } = string.Empty;
    public string? Publisher { get; set; }
    [Required] public IFormFile File { get; set; } = null!;
}

public record BookResponse(
    Guid Id,
    string Title,
    int? Grade,
    string Subject,
    string? Publisher,
    string SourceType,
    int? TotalPages,
    DateTimeOffset CreatedAt);

public record QuestionSummaryResponse(
    Guid Id,
    int? QuestionNo,
    int? SourcePage,
    string Status,
    string? OriginalQuestion,
    int OptionCount,
    bool EditorRequired,
    IReadOnlyList<string> QualityFlags);

public record BatchTransformResponse(
    int Total,
    int Succeeded,
    int NeedsReview,
    int Rejected,
    int Failed,
    int AlreadyDone,
    IReadOnlyList<BatchQuestionErrorResponse> Errors);

public record BatchQuestionErrorResponse(Guid QuestionId, int? QuestionNo, string Message);
