namespace MaarifPlatform.Domain.Entities;

/// <summary>§2/§13 Reference Library — MEB öğretim programı, ders kitabı, kılavuz vb. RAG kaynağı.</summary>
public class ReferenceDocument : Entity
{
    public string Title { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public int? Grade { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateOnly? PublicationDate { get; set; }
    public string Authority { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public string StorageUri { get; set; } = string.Empty;

    /// <summary>Aynı dokümanın tekrar tekrar embed edilmesini önlemek için idempotent ingestion anahtarı (§9).</summary>
    public string DocumentHash { get; set; } = string.Empty;

    public ICollection<ReferenceChunk> Chunks { get; set; } = new List<ReferenceChunk>();
}
