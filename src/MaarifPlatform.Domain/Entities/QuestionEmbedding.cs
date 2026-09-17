using Pgvector;

namespace MaarifPlatform.Domain.Entities;

/// <summary>§10 Benzerlik/Tekrar Kontrolü — Soru Üret pipeline'ının ürettiği her sorunun
/// embedding'i burada saklanır (yalnızca kelime değil SEMANTİK benzerlik kontrolü için).
/// Grade/Subject denormalize edilir — QuestionDna'ya join olmadan hızlı havuz filtrelemesi
/// için (bkz. ReferenceChunk/LearningOutcome'daki aynı desen).</summary>
public class QuestionEmbedding : Entity
{
    public Guid QuestionVersionId { get; set; }
    public QuestionVersion? QuestionVersion { get; set; }

    public int Grade { get; set; }
    public string Subject { get; set; } = string.Empty;

    public Vector Embedding { get; set; } = null!;
}
