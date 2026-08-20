using MaarifPlatform.Application.Extraction;

namespace MaarifPlatform.Application.Rag;

/// <summary>§G "kazanım-bazlı chunking": sabit token uzunluğu yerine paragraf/başlık
/// sınırlarına saygı gösteren bir ilk geçiş. Gerçek kazanım-sınır tespiti (ör. "Kazanım M.9.1.3.2"
/// başlıklarını tanıma) ileride bu sözleşmenin arkasında daha gelişmiş bir implementasyonla
/// değiştirilebilir; arayüz bunun için sabit tutulur.</summary>
public interface IReferenceChunker
{
    IReadOnlyList<ChunkCandidate> Chunk(IReadOnlyList<ExtractedPage> pages);
}
