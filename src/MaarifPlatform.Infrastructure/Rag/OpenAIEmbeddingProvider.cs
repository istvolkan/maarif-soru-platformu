using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MaarifPlatform.Application.Rag;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Infrastructure.Rag;

public class OpenAIEmbeddingOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "text-embedding-3-small";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
}

/// <summary>§11/§H gerçek embedding sağlayıcısı. text-embedding-3-small 1536 boyutludur —
/// ReferenceChunk.Embedding sütunuyla (vector(1536)) uyumlu; farklı bir model seçilirse
/// migration ile sütun boyutu güncellenmelidir (§elestiri madde 6).</summary>
public class OpenAIEmbeddingProvider(HttpClient httpClient, IOptions<OpenAIEmbeddingOptions> options) : IEmbeddingProvider
{
    private readonly OpenAIEmbeddingOptions _options = options.Value;

    public int Dimensions => 1536;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Embeddings:OpenAI:ApiKey tanımlı değil. Gerçek embedding için appsettings/user-secrets " +
                "üzerinden bir API anahtarı sağlanmalı; anahtar yoksa Embeddings:Provider=Local kullanın.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/embeddings");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(new OpenAIEmbeddingRequest(_options.Model, text));

        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("OpenAI embeddings API boş yanıt döndü.");

        return payload.Data.First().Embedding;
    }

    private sealed record OpenAIEmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input);

    private sealed record OpenAIEmbeddingResponse(
        [property: JsonPropertyName("data")] List<OpenAIEmbeddingDatum> Data);

    private sealed record OpenAIEmbeddingDatum(
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
