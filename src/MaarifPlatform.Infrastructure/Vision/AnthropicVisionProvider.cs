using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Vision;
using MaarifPlatform.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Infrastructure.Vision;

/// <summary>§7/§10 Provider Disagreement için ikinci gerçek Vision sağlayıcısı — GeminiVisionProvider'a
/// paralel, aynı yapılandırılmış çıktı şemasına (tool-use ile) sahip. Sprint 4'te kurulu resmi
/// Anthropic NuGet paketini kullanır — yeni bir bağımlılık eklemez.</summary>
public class AnthropicVisionProvider : IVisionProvider
{
    private const string ToolName = "submit_visual_observation";

    private readonly AnthropicVisionOptions _options;
    private readonly AnthropicClient _client;

    public AnthropicVisionProvider(IOptions<AnthropicVisionOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Vision:Anthropic:ApiKey tanımlı değil. Gerçek görsel analiz için appsettings/user-secrets " +
                "üzerinden bir API anahtarı sağlanmalı; anahtar yoksa Vision:Provider=Local kullanın.");
        }

        _client = new AnthropicClient { ApiKey = _options.ApiKey };
    }

    public string Name => "anthropic";

    public Task<VisualObservation> AnalyzePageAsync(byte[] pageImagePng, CancellationToken ct = default) =>
        CallAnthropicAsync(pageImagePng,
            "Bu bir ders kitabı sayfasının tam görüntüsüdür. Sayfadaki TÜM görsel öğeleri " +
            "(şekil, grafik, tablo, diyagram) tespit et.", ct);

    public Task<VisualObservation> AnalyzeQuestionImageAsync(byte[] questionImagePng, string questionText, CancellationToken ct = default) =>
        CallAnthropicAsync(questionImagePng,
            $"Bu, aşağıdaki soruya ait bir görseldir. Soru metni: \"{questionText}\"\n" +
            "Görseldeki öğeleri ve aralarındaki ilişkileri, soru metninin atıfta bulunduğu etiketlere " +
            "(nokta/kenar/açı isimleri vb.) sadık kalarak çıkar.", ct);

    public Task<VisualObservation> ExtractVisualStructureAsync(byte[] imagePng, string visualType, CancellationToken ct = default) =>
        CallAnthropicAsync(imagePng,
            $"Bu görsel bir '{visualType}' türündedir. Ders-bazlı kritik ilişki türlerini " +
            "(geometri: point_on_segment/parallel/perpendicular/equal_length vb.; fizik: " +
            "series_connection/force_direction vb.; kimya: bond_type/charge vb.) kullanarak derinlemesine analiz et.",
            ct);

    /// <summary>§6 — ortak deterministik doğrulayıcıya delege eder, ikinci bir AI çağrısı yapmaz.</summary>
    public Task<IReadOnlyList<VisualWarning>> ValidateVisualStructureAsync(VisualObservation observation, CancellationToken ct = default) =>
        Task.FromResult(VisualObservationValidator.Validate(observation));

    private async Task<VisualObservation> CallAnthropicAsync(byte[] imagePng, string taskPrompt, CancellationToken ct)
    {
        const string systemPrompt =
            "Sen bir matematik/fizik/kimya ders kitabı görselini analiz eden bir gözlemcisin. " +
            "KURAL: Emin olmadığın bir etiket-değer eşleşmesi varsa (örn. '5' değeri AB'ye mi AC'ye mi " +
            "ait belli değilse) bunu warnings alanında AMBIGUOUS_LABEL_ASSOCIATION olarak işaretle, tahmin " +
            "ile doldurma. Cevabını YALNIZCA submit_visual_observation aracını çağırarak ver.";

        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = systemPrompt,
            Tools = [BuildObservationTool()],
            ToolChoice = new ToolChoiceTool { Name = ToolName },
            Messages =
            [
                new()
                {
                    Role = Role.User,
                    Content = new List<ContentBlockParam>
                    {
                        new ImageBlockParam
                        {
                            Source = new Base64ImageSource
                            {
                                MediaType = "image/png",
                                Data = Convert.ToBase64String(imagePng)
                            }
                        },
                        new TextBlockParam { Text = taskPrompt }
                    }
                }
            ]
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == ToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_visual_observation tool_use bloğu bulunamadı.");

        var inputTokens = (int)response.Usage.InputTokens;
        var outputTokens = (int)response.Usage.OutputTokens;
        var usage = new AiUsage(
            Name, _options.Model, inputTokens, outputTokens,
            AnthropicPricing.EstimateCostUsd(_options.Model, inputTokens, outputTokens),
            (int)stopwatch.ElapsedMilliseconds);

        return ParseObservation(toolUse.Input, usage);
    }

    private static Tool BuildObservationTool()
    {
        var properties = new Dictionary<string, JsonElement>
        {
            ["visual_type"] = Schema("string", "Görselin türü (örn. geometry_diagram, electric_circuit, lewis_structure)."),
            ["description"] = Schema("string", "Görselin kısa açıklaması."),
            ["confidence"] = Schema("number", "0.0-1.0 arası genel güven."),
            ["elements"] = JsonSerializer.SerializeToElement(new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string" },
                        type = new { type = "string" },
                        label = new { type = "string" },
                        value = new { type = "string" },
                        unit = new { type = "string" },
                        confidence = new { type = "number" }
                    },
                    required = new[] { "id", "type", "confidence" }
                }
            }),
            ["relations"] = JsonSerializer.SerializeToElement(new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        subject = new { type = "string" },
                        relation = new { type = "string" },
                        @object = new { type = "string" },
                        confidence = new { type = "number" }
                    },
                    required = new[] { "subject", "relation", "object", "confidence" }
                }
            }),
            ["visual_text"] = Schema("array", "Görseldeki metinler."),
            ["symbols"] = Schema("array", "Görseldeki semboller."),
            ["measurements"] = Schema("array", "Görseldeki ölçüm ifadeleri."),
            ["warnings"] = JsonSerializer.SerializeToElement(new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        type = new { type = "string" },
                        message = new { type = "string" },
                        confidence = new { type = "number" }
                    },
                    required = new[] { "type", "message", "confidence" }
                }
            })
        };

        return new Tool
        {
            Name = ToolName,
            Description = "Görselden çıkarılan yapılandırılmış gözlemi bildir.",
            InputSchema = new()
            {
                Properties = properties,
                Required = ["visual_type", "description", "confidence", "elements", "relations", "warnings"]
            }
        };
    }

    private static JsonElement Schema(string type, string description)
    {
        if (type == "array")
        {
            return JsonSerializer.SerializeToElement(new { type, items = new { type = "string" }, description });
        }

        return JsonSerializer.SerializeToElement(new { type, description });
    }

    private static VisualObservation ParseObservation(IReadOnlyDictionary<string, JsonElement> input, AiUsage usage)
    {
        string GetString(string key) =>
            input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        decimal GetDecimal(string key) =>
            input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;

        static string GetElString(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        static string? GetElOptionalString(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        static decimal GetElDecimal(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;

        var elements = new List<VisualElement>();
        if (input.TryGetValue("elements", out var elementsEl) && elementsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in elementsEl.EnumerateArray())
            {
                elements.Add(new VisualElement(
                    GetElString(e, "id"), GetElString(e, "type"), GetElOptionalString(e, "label"),
                    GetElOptionalString(e, "value"), GetElOptionalString(e, "unit"), GetElDecimal(e, "confidence")));
            }
        }

        var relations = new List<VisualRelation>();
        if (input.TryGetValue("relations", out var relationsEl) && relationsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in relationsEl.EnumerateArray())
            {
                relations.Add(new VisualRelation(
                    GetElString(r, "subject"), GetElString(r, "relation"), GetElString(r, "object"), GetElDecimal(r, "confidence")));
            }
        }

        var warnings = new List<VisualWarning>();
        if (input.TryGetValue("warnings", out var warningsEl) && warningsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var w in warningsEl.EnumerateArray())
            {
                warnings.Add(new VisualWarning(GetElString(w, "type"), GetElString(w, "message"), GetElDecimal(w, "confidence")));
            }
        }

        List<string> GetStringArray(string key) =>
            input.TryGetValue(key, out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                : [];

        return new VisualObservation(
            VisualType: GetString("visual_type"),
            Description: GetString("description"),
            Confidence: GetDecimal("confidence"),
            Elements: elements,
            Relations: relations,
            VisualText: GetStringArray("visual_text"),
            Symbols: GetStringArray("symbols"),
            Measurements: GetStringArray("measurements"),
            Warnings: warnings,
            Usage: usage);
    }
}
