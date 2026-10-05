using MaarifPlatform.Domain.Enums;
using Pgvector;

namespace MaarifPlatform.Domain.Entities;

/// <summary>§10/§52 Benzerlik/Tekrar Kontrolü — hem Soru Üret pipeline'ının ürettiği sorular
/// (<see cref="QuestionEmbeddingSourceKind.Generated"/>) HEM DE kitaptan çıkarılan gerçek kaynak
/// sorular (<see cref="QuestionEmbeddingSourceKind.Extracted"/>) burada aynı havuzda saklanır —
/// yalnızca kelime değil SEMANTİK benzerlik kontrolü için. Grade/Subject denormalize edilir —
/// QuestionDna'ya join olmadan hızlı havuz filtrelemesi için (bkz. ReferenceChunk/
/// LearningOutcome'daki aynı desen).</summary>
public class QuestionEmbedding : Entity
{
    public Guid QuestionVersionId { get; set; }
    public QuestionVersion? QuestionVersion { get; set; }

    public int Grade { get; set; }
    public string Subject { get; set; } = string.Empty;

    // Mevcut satırların hepsi Generated'tı (bkz. migration default) — bu alan eklenmeden önce
    // havuzda yalnızca üretilmiş sorular vardı.
    public QuestionEmbeddingSourceKind SourceKind { get; set; } = QuestionEmbeddingSourceKind.Generated;

    public Vector Embedding { get; set; } = null!;
}
