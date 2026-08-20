using System.ComponentModel.DataAnnotations;

namespace MaarifPlatform.Api.Dtos;

public class CreateReferenceDocumentRequest
{
    [Required] public string Title { get; set; } = string.Empty;
    [Required] public string DocumentType { get; set; } = string.Empty;
    public int? Grade { get; set; }
    [Required] public string Subject { get; set; } = string.Empty;
    public string? Version { get; set; }
    public DateOnly? PublicationDate { get; set; }
    [Required] public string Authority { get; set; } = string.Empty;
    [Required] public IFormFile File { get; set; } = null!;
}

public record ReferenceDocumentResponse(
    Guid Id,
    string Title,
    string DocumentType,
    int? Grade,
    string Subject,
    string Version,
    string Authority,
    bool Active,
    DateTimeOffset CreatedAt);

public record RetrievedChunkResponse(
    Guid ReferenceDocumentId,
    string DocumentTitle,
    int? Page,
    string? SectionPath,
    string ChunkText,
    float Distance);
