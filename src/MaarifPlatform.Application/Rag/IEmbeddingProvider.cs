namespace MaarifPlatform.Application.Rag;

/// <summary>§11/§G — embedding üretimi de ILLMProvider gibi sağlayıcı-bağımsız olmalıdır.
/// Ayrı bir sözleşme olarak tutulur çünkü embedding modeli seçimi (ve boyutu) genellikle
/// soru analizi/dönüşümü için kullanılan modelden bağımsız değişir.</summary>
public interface IEmbeddingProvider
{
    /// <summary>Üretilen vektörün boyutu — pgvector sütun tanımıyla (bkz. ReferenceChunkConfiguration)
    /// uyumlu olmalıdır. Sağlayıcı değişirse migration gerekebilir (§elestiri madde 6).</summary>
    int Dimensions { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}
