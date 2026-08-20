using Pgvector;

namespace MaarifPlatform.Domain.Entities;

/// <summary>§G RAG mimarisi — kazanım/beceri-bazlı parçalanmış, gömülü referans metni.</summary>
public class ReferenceChunk : Entity
{
    public Guid ReferenceDocumentId { get; set; }
    public ReferenceDocument? ReferenceDocument { get; set; }

    public int? Page { get; set; }
    public string? SectionPath { get; set; }
    public string ChunkText { get; set; } = string.Empty;

    /// <summary>Embedding vektörü — pgvector "vector" sütununa eşlenir (Infrastructure'da yapılandırılır).</summary>
    public Vector? Embedding { get; set; }
}
