using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Vision;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Infrastructure.Vision;

/// <summary>§8.1 varsayılan Vision sağlayıcısı. Google'ın resmi .NET istemcileri (Google.Cloud.AIPlatform.V1)
/// Vertex AI'ye (GCP proje/servis hesabı) bağlıdır; burada bilinçli olarak daha basit, API-key
/// tabanlı genel Generative Language API'ye (generativelanguage.googleapis.com) karşı ham HTTP
/// çağrısı yapılır — <see cref="MaarifPlatform.Infrastructure.Rag.OpenAIEmbeddingProvider"/> ile
/// aynı desen. Bu yaklaşım yeni bir NuGet bağımlılığı eklemekten kaçınır (PdfPig dersi sonrası
/// tedarik zinciri temkinliliği).</summary>
public class GeminiVisionProvider(HttpClient httpClient, IOptions<GeminiOptions> options) : IVisionProvider
{
    private readonly GeminiOptions _options = options.Value;

    public string Name => "gemini";

    public Task<VisualObservation> AnalyzePageAsync(byte[] pageImagePng, CancellationToken ct = default) =>
        CallGeminiAsync(pageImagePng,
            "Bu bir ders kitabı sayfasının tam görüntüsüdür. Sayfadaki TÜM görsel öğeleri " +
            "(şekil, grafik, tablo, diyagram) tespit et.", ct);

    public Task<VisualObservation> AnalyzeQuestionImageAsync(byte[] questionImagePng, string questionText, CancellationToken ct = default) =>
        CallGeminiAsync(questionImagePng,
            $"Bu, aşağıdaki soruya ait bir görseldir. Soru metni: \"{questionText}\"\n" +
            "Görseldeki öğeleri ve aralarındaki ilişkileri, soru metninin atıfta bulunduğu etiketlere " +
            "(nokta/kenar/açı isimleri vb.) sadık kalarak çıkar.", ct);

    public Task<VisualObservation> ExtractVisualStructureAsync(byte[] imagePng, string visualType, CancellationToken ct = default) =>
        CallGeminiAsync(imagePng,
            $"Bu görsel bir '{visualType}' türündedir. Ders-bazlı kritik ilişki türlerini " +
            "(geometri: point_on_segment/parallel/perpendicular/equal_length vb.; fizik: " +
            "series_connection/force_direction vb.; kimya: bond_type/charge vb.) kullanarak derinlemesine analiz et.",
            ct);

    /// <summary>§6 Mathematical/Scientific Fidelity — ikinci bir AI çağrısı yapmadan deterministik
    /// tutarlılık kontrolü (RubricEngine'in "kod tabanlı denetim" felsefesiyle aynı çizgide, §29).</summary>
    public Task<IReadOnlyList<VisualWarning>> ValidateVisualStructureAsync(VisualObservation observation, CancellationToken ct = default)
    {
        var warnings = new List<VisualWarning>();

        if (observation.Confidence < 0.5m)
        {
            warnings.Add(new VisualWarning("LOW_CONFIDENCE",
                $"Gözlem güveni düşük ({observation.Confidence:0.00}).", observation.Confidence));
        }

        var elementIds = observation.Elements.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var relation in observation.Relations)
        {
            if (!elementIds.Contains(relation.Subject) || !elementIds.Contains(relation.Object))
            {
                warnings.Add(new VisualWarning("DANGLING_RELATION_REFERENCE",
                    $"'{relation.Subject} {relation.Relation} {relation.Object}' ilişkisi bilinmeyen bir öğeye atıfta bulunuyor.",
                    relation.Confidence));
            }
        }

        if (observation.Elements.Count == 0 && observation.Relations.Count == 0)
        {
            warnings.Add(new VisualWarning("EMPTY_OBSERVATION", "Görselden hiçbir öğe/ilişki çıkarılamadı.", observation.Confidence));
        }

        return Task.FromResult<IReadOnlyList<VisualWarning>>(warnings);
    }

    private async Task<VisualObservation> CallGeminiAsync(byte[] imagePng, string taskPrompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Vision:Gemini:ApiKey tanımlı değil. Gerçek görsel analiz için appsettings/user-secrets " +
                "üzerinden bir API anahtarı sağlanmalı; anahtar yoksa Vision:Provider=Local kullanın.");
        }

        const string systemPreamble =
            "Sen bir matematik/fizik/kimya ders kitabı görselini analiz eden bir gözlemcisin. " +
            "KURAL: Emin olmadığın bir etiket-değer eşleşmesi varsa (örn. '5' değeri AB'ye mi AC'ye mi ait " +
            "belli değilse) bunu warnings alanında AMBIGUOUS_LABEL_ASSOCIATION olarak işaretle, tahmin ile " +
            "doldurma. Çıktın yalnızca verilen JSON şemasına uygun olmalı.";

        var requestBody = new
        {
            contents = new object[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = systemPreamble + "\n\n" + taskPrompt },
                        new { inline_data = new { mime_type = "image/png", data = Convert.ToBase64String(imagePng) } }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = BuildResponseSchema()
            }
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{_options.BaseUrl}/models/{_options.Model}:generateContent?key={_options.ApiKey}")
        {
            Content = JsonContent.Create(requestBody)
        };

        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<GeminiGenerateContentResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Gemini API boş yanıt döndü.");

        var jsonText = payload.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Gemini yanıtında beklenen JSON içerik bulunamadı.");

        var inputTokens = payload.UsageMetadata?.PromptTokenCount ?? 0;
        var outputTokens = payload.UsageMetadata?.CandidatesTokenCount ?? 0;

        // NOT: Gemini fiyatlandırması burada modellenmedi (canlı/doğrulanmış bir kaynağım yok) —
        // yanlış rakamı doğruymuş gibi göstermektense CostUsd=0 bırakıldı. Gerçek anahtar
        // sağlandığında Google'ın güncel fiyat sayfasından doğrulanıp AnthropicPricing'deki
        // desenle (model→$/1M token) doldurulmalı.
        var usage = new AiUsage(Name, _options.Model, inputTokens, outputTokens, 0m, 0);

        return ParseObservation(jsonText, usage);
    }

    private static object BuildResponseSchema() => new
    {
        type = "OBJECT",
        properties = new
        {
            visual_type = new { type = "STRING" },
            description = new { type = "STRING" },
            confidence = new { type = "NUMBER" },
            elements = new
            {
                type = "ARRAY",
                items = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        id = new { type = "STRING" },
                        type = new { type = "STRING" },
                        label = new { type = "STRING" },
                        value = new { type = "STRING" },
                        unit = new { type = "STRING" },
                        confidence = new { type = "NUMBER" }
                    },
                    required = new[] { "id", "type", "confidence" }
                }
            },
            relations = new
            {
                type = "ARRAY",
                items = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        subject = new { type = "STRING" },
                        relation = new { type = "STRING" },
                        @object = new { type = "STRING" },
                        confidence = new { type = "NUMBER" }
                    },
                    required = new[] { "subject", "relation", "object", "confidence" }
                }
            },
            visual_text = new { type = "ARRAY", items = new { type = "STRING" } },
            symbols = new { type = "ARRAY", items = new { type = "STRING" } },
            measurements = new { type = "ARRAY", items = new { type = "STRING" } },
            warnings = new
            {
                type = "ARRAY",
                items = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        type = new { type = "STRING" },
                        message = new { type = "STRING" },
                        confidence = new { type = "NUMBER" }
                    },
                    required = new[] { "type", "message", "confidence" }
                }
            }
        },
        required = new[] { "visual_type", "description", "confidence", "elements", "relations", "warnings" }
    };

    private static VisualObservation ParseObservation(string json, AiUsage usage)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        static string GetString(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        static string? GetOptionalString(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        static decimal GetDecimal(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;

        var elements = new List<VisualElement>();
        if (root.TryGetProperty("elements", out var elementsEl) && elementsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in elementsEl.EnumerateArray())
            {
                elements.Add(new VisualElement(
                    GetString(e, "id"), GetString(e, "type"), GetOptionalString(e, "label"),
                    GetOptionalString(e, "value"), GetOptionalString(e, "unit"), GetDecimal(e, "confidence")));
            }
        }

        var relations = new List<VisualRelation>();
        if (root.TryGetProperty("relations", out var relationsEl) && relationsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in relationsEl.EnumerateArray())
            {
                relations.Add(new VisualRelation(
                    GetString(r, "subject"), GetString(r, "relation"), GetString(r, "object"), GetDecimal(r, "confidence")));
            }
        }

        var warnings = new List<VisualWarning>();
        if (root.TryGetProperty("warnings", out var warningsEl) && warningsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var w in warningsEl.EnumerateArray())
            {
                warnings.Add(new VisualWarning(GetString(w, "type"), GetString(w, "message"), GetDecimal(w, "confidence")));
            }
        }

        List<string> GetStringArray(string prop) =>
            root.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                : [];

        return new VisualObservation(
            VisualType: GetString(root, "visual_type"),
            Description: GetString(root, "description"),
            Confidence: GetDecimal(root, "confidence"),
            Elements: elements,
            Relations: relations,
            VisualText: GetStringArray("visual_text"),
            Symbols: GetStringArray("symbols"),
            Measurements: GetStringArray("measurements"),
            Warnings: warnings,
            Usage: usage);
    }

    private sealed class GeminiGenerateContentResponse
    {
        [JsonPropertyName("candidates")] public List<GeminiCandidate>? Candidates { get; set; }
        [JsonPropertyName("usageMetadata")] public GeminiUsageMetadata? UsageMetadata { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")] public GeminiContent? Content { get; set; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("parts")] public List<GeminiPart>? Parts { get; set; }
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private sealed class GeminiUsageMetadata
    {
        [JsonPropertyName("promptTokenCount")] public int PromptTokenCount { get; set; }
        [JsonPropertyName("candidatesTokenCount")] public int CandidatesTokenCount { get; set; }
    }
}
