using System.Security.Cryptography;
using System.Text;
using MaarifPlatform.Application.Rag;

namespace MaarifPlatform.Infrastructure.Rag;

/// <summary>Dış API anahtarı GEREKTİRMEZ — geliştirme/test ortamında RAG borusunun mekaniğini
/// (chunk → embed → pgvector'a yaz → CosineDistance ile getir) doğrulamak içindir.
/// Semantik olarak ANLAMLI DEĞİLDİR: metnin SHA-256 özeti bir PRNG tohumu olarak kullanılır,
/// bu yüzden benzer anlamdaki iki metin birbirine yakın vektörler üretmez — yalnızca AYNI metin
/// her zaman AYNI vektörü üretir (deterministik). Üretimde <see cref="OpenAIEmbeddingProvider"/>
/// gibi gerçek bir sağlayıcıyla değiştirilmelidir (§9/§H — bu görev "ucuz/orta" katman AI çağrısıdır).</summary>
public class LocalDeterministicEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 1536;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        var seed = BitConverter.ToInt32(hash, 0);
        var rng = new Random(seed);

        var vector = new float[Dimensions];
        for (var i = 0; i < Dimensions; i++)
        {
            vector[i] = (float)(rng.NextDouble() * 2 - 1);
        }

        Normalize(vector);
        return Task.FromResult(vector);
    }

    private static void Normalize(float[] vector)
    {
        double sumSquares = 0;
        foreach (var v in vector)
        {
            sumSquares += v * v;
        }

        var norm = Math.Sqrt(sumSquares);
        if (norm <= 0)
        {
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
    }
}
