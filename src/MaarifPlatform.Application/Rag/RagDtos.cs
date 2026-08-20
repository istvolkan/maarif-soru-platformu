namespace MaarifPlatform.Application.Rag;

/// <summary>§G RAG mimarisi — kazanım/beceri-bazlı parçalanmış aday chunk (henüz gömülmemiş).</summary>
public sealed record ChunkCandidate(int? Page, string? SectionPath, string Text);

/// <summary>Retrieval sonucu — atıf zorunluluğu (§G) için gereken tüm alanları taşır.</summary>
public sealed record RetrievedChunk(
    Guid ReferenceDocumentId,
    string DocumentTitle,
    int? Page,
    string? SectionPath,
    string ChunkText,
    float Distance);
