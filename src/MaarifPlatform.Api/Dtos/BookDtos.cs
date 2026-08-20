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
