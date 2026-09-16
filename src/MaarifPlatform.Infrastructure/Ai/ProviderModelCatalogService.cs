using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Models.Models;
using MaarifPlatform.Infrastructure.Vision;
using Microsoft.Extensions.Options;
using OpenAI.Models;

namespace MaarifPlatform.Infrastructure.Ai;

public sealed record ProviderModelOption(string Id, string? DisplayName);

/// <summary>Admin Ayarlar ekranındaki Model alanları serbest metindi ve sağlayıcıların güncel
/// model kataloğuyla elle senkron tutulmak zorundaydı (bkz. GeminiOptions.Model üzerindeki "KESİN
/// doğru kabul etmeyin" notu) — bu servis her sağlayıcının kendi "list models" uç noktasını
/// kayıtlı/az önce girilen anahtarla sorgulayarak GERÇEK, o an geçerli model listesini döner.
/// Sabit kodlanmış bir liste YOKTUR çünkü o liste tam olarak bu sınıfın çözmeye çalıştığı
/// bayatlama sorununu yeniden üretir.</summary>
public class ProviderModelCatalogService(
    HttpClient httpClient,
    IOptionsMonitor<AnthropicOptions> anthropicOptions,
    IOptionsMonitor<OpenAiOptions> openAiOptions,
    IOptionsMonitor<GeminiOptions> geminiOptions)
{
    public async Task<IReadOnlyList<ProviderModelOption>> ListAnthropicModelsAsync(string? apiKeyOverride, CancellationToken ct = default)
    {
        var apiKey = string.IsNullOrWhiteSpace(apiKeyOverride) ? anthropicOptions.CurrentValue.ApiKey : apiKeyOverride;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Model listesini çekmek için önce bir Anthropic API anahtarı girin.");
        }

        var client = new AnthropicClient { ApiKey = apiKey };
        var models = new List<ProviderModelOption>();
        var page = await client.Models.List(new ModelListParams(), ct);
        while (true)
        {
            models.AddRange(page.Items.Select(m => new ProviderModelOption(m.ID, m.DisplayName)));
            if (!page.HasNext())
            {
                break;
            }

            page = await page.Next(ct);
        }

        return models.OrderByDescending(m => m.Id).ToList();
    }

    public async Task<IReadOnlyList<ProviderModelOption>> ListOpenAiModelsAsync(string? apiKeyOverride, CancellationToken ct = default)
    {
        var apiKey = string.IsNullOrWhiteSpace(apiKeyOverride) ? openAiOptions.CurrentValue.ApiKey : apiKeyOverride;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Model listesini çekmek için önce bir OpenAI API anahtarı girin.");
        }

        var client = new OpenAIModelClient(apiKey);
        var result = await client.GetModelsAsync(ct);

        // Judge sağlayıcısı sohbet/tool-calling modeli bekliyor — embedding/ses/moderasyon
        // modelleri (aynı hesabın döndürdüğü katalogda hepsi karışık gelir) burada elenir.
        var excluded = new[] { "embedding", "whisper", "tts", "dall-e", "moderation", "davinci", "babbage" };
        return result.Value
            .Where(m => !excluded.Any(x => m.Id.Contains(x, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new ProviderModelOption(m.Id, null))
            .ToList();
    }

    public async Task<IReadOnlyList<ProviderModelOption>> ListGeminiModelsAsync(string? apiKeyOverride, CancellationToken ct = default)
    {
        var options = geminiOptions.CurrentValue;
        var apiKey = string.IsNullOrWhiteSpace(apiKeyOverride) ? options.ApiKey : apiKeyOverride;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Model listesini çekmek için önce bir Gemini API anahtarı girin.");
        }

        using var response = await httpClient.GetAsync($"{options.BaseUrl}/models?key={apiKey}", ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<GeminiModelListResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Gemini model listesi API'si boş yanıt döndü.");

        return (payload.Models ?? [])
            .Where(m => m.SupportedGenerationMethods?.Contains("generateContent") == true && m.Name is not null)
            .Select(m => new ProviderModelOption(m.Name!["models/".Length..], m.DisplayName))
            .OrderByDescending(m => m.Id)
            .ToList();
    }

    private sealed class GeminiModelListResponse
    {
        [JsonPropertyName("models")] public List<GeminiModelEntry>? Models { get; set; }
    }

    private sealed class GeminiModelEntry
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("supportedGenerationMethods")] public List<string>? SupportedGenerationMethods { get; set; }
    }
}
