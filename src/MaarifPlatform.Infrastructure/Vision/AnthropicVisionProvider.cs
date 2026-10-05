using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using MaarifPlatform.Application.Extraction;
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
    private const string TranscribeToolName = "submit_page_transcription";

    private readonly IOptionsMonitor<AnthropicVisionOptions> _optionsMonitor;
    private AnthropicClient? _client;
    private string? _clientKey;

    public AnthropicVisionProvider(IOptionsMonitor<AnthropicVisionOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public string Name => "anthropic";

    /// <summary>Sprint 11: bkz. AnthropicLLMProvider.Current() — aynı desen.</summary>
    private (AnthropicVisionOptions Options, AnthropicClient Client) Current()
    {
        var options = _optionsMonitor.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "Vision:Anthropic:ApiKey tanımlı değil. Gerçek görsel analiz için appsettings/user-secrets " +
                "üzerinden bir API anahtarı sağlanmalı; anahtar yoksa Vision:Provider=Local kullanın.");
        }

        if (_client is null || _clientKey != options.ApiKey)
        {
            _client = new AnthropicClient { ApiKey = options.ApiKey };
            _clientKey = options.ApiKey;
        }

        return (options, _client);
    }

    public Task<VisualObservation> AnalyzePageAsync(byte[] pageImagePng, CancellationToken ct = default) =>
        CallAnthropicAsync(pageImagePng,
            "Bu bir ders kitabı sayfasının tam görüntüsüdür. Sayfadaki TÜM görsel öğeleri " +
            "(şekil, grafik, tablo, diyagram) tespit et.", ct);

    public Task<VisualObservation> AnalyzeQuestionImageAsync(byte[] questionImagePng, string questionText, CancellationToken ct = default) =>
        CallAnthropicAsync(questionImagePng,
            $"Bu, aşağıdaki soruyu içeren ders kitabı SAYFASININ TAM görüntüsüdür (yalnızca soruya " +
            $"kırpılmış değildir — sayfada başka sorular/metinler de olabilir). Soru metni: \"{questionText}\"\n" +
            "Görseldeki öğeleri ve aralarındaki ilişkileri, soru metninin atıfta bulunduğu etiketlere " +
            "(nokta/kenar/açı isimleri vb.) sadık kalarak çıkar. Ayrıca bounding_box alanında, bu soruya " +
            "ait asıl şekli/diyagramı/grafiği (metin değil, yalnızca görsel öğeyi) sıkıca çevreleyen " +
            "dikdörtgeni, sayfanın tam genişlik/yüksekliğine göre 0.0-1.0 arası normalize edilmiş " +
            "{x, y, width, height} olarak bildir (x/y sol-üst köşe). Sayfada bu soruya ait ayırt " +
            "edilebilir bir şekil/diyagram YOKSA (örn. görsel yalnızca metin veya soru tablo/formül " +
            "içermiyor) bounding_box alanını tamamen atla.", ct);

    public Task<VisualObservation> ExtractVisualStructureAsync(byte[] imagePng, string visualType, CancellationToken ct = default) =>
        CallAnthropicAsync(imagePng,
            $"Bu görsel bir '{visualType}' türündedir. Ders-bazlı kritik ilişki türlerini " +
            "(geometri: point_on_segment/parallel/perpendicular/equal_length vb.; fizik: " +
            "series_connection/force_direction vb.; kimya: bond_type/charge vb.) kullanarak derinlemesine analiz et.",
            ct);

    /// <summary>§6 — ortak deterministik doğrulayıcıya delege eder, ikinci bir AI çağrısı yapmaz.</summary>
    public Task<IReadOnlyList<VisualWarning>> ValidateVisualStructureAsync(VisualObservation observation, CancellationToken ct = default) =>
        Task.FromResult(VisualObservationValidator.Validate(observation));

    public async Task<PageTranscriptionResult> TranscribePageAsync(byte[] pageImagePng, int pageNo, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = options.MaxTokens,
            System = TranscribeSystemPrompt,
            Tools = [BuildTranscriptionTool()],
            ToolChoice = new ToolChoiceTool { Name = TranscribeToolName },
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
                                Data = Convert.ToBase64String(pageImagePng)
                            }
                        },
                        new TextBlockParam { Text = "Bu sayfadaki tüm soruları transkribe et." }
                    }
                }
            ]
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == TranscribeToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_page_transcription tool_use bloğu bulunamadı.");

        var inputTokens = (int)response.Usage.InputTokens;
        var outputTokens = (int)response.Usage.OutputTokens;
        var usage = new AiUsage(
            Name, options.Model, inputTokens, outputTokens,
            AnthropicPricing.EstimateCostUsd(options.Model, inputTokens, outputTokens),
            (int)stopwatch.ElapsedMilliseconds);

        return ParseTranscription(toolUse.Input, usage);
    }

    /// <summary>Ham PDF metni + regex heuristic'in (IQuestionSegmenter) YERİNE kullanılır — bkz.
    /// GeminiVisionProvider'daki aynı metnin kopyası (iki bağımsız sağlayıcı aynı talimatı almalı,
    /// §10 Provider Disagreement karşılaştırmasının anlamlı olması için).</summary>
    private const string TranscribeSystemPrompt =
        "Sen bir matematik/fen ders kitabı sayfasının görüntüsünü okuyup üzerindeki soruları " +
        "yazıya döken bir transkripsiyon uzmanısın.\n\n" +
        "KURALLAR:\n" +
        "1. Sayfadaki HER soruyu ayrı bir öğe olarak döndür. Soru numarası görseldeyse question_no'ya yaz.\n" +
        "2. stem alanına soru kökünü, matematiksel gösterimi (kesir, üs, kök, formül) olabildiğince " +
        "sadık bir şekilde DÜZ METNE çevirerek yaz (örn. kesir için 'a/b', üs için 'x^2'). Diyagramdaki " +
        "nokta/etiket isimlerini stem'e KARIŞTIRMA — onlar visual_description alanına ait.\n" +
        "3. Şıklar varsa options dizisine (label: 'A'/'B'/..., text) yaz; yoksa boş dizi döndür.\n" +
        "4. Görselde doğrudan yazılı bir doğru cevap/işaretli şık görüyorsan correct_answer'a yaz; " +
        "emin değilsen boş bırak — TAHMİN ETME.\n" +
        "5. Sorunun bir şekil/grafik/tablo/diyagrama GERÇEKTEN ihtiyacı varsa has_visual=true yap ve " +
        "visual_description'a o şeklin/diyagramın ne gösterdiğini (köşe/nokta isimleri, kenar " +
        "uzunlukları, eksen etiketleri vb. dahil) ayrıntılı yaz. Salt dekoratif görsellerde false.\n" +
        "6. Bu sayfa bir cevap anahtarı, içindekiler, önsöz gibi SORU OLMAYAN bir sayfaysa questions " +
        "alanını boş dizi döndür — bir cevap anahtarını soru sanıp UYDURMA.\n" +
        "7. Okunaksız/belirsiz bir kısım varsa olduğu gibi (belirsiz işaretleyerek) yaz, tahminle doldurma.\n" +
        "8. Cevabını YALNIZCA submit_page_transcription aracını çağırarak ver.";

    private static Tool BuildTranscriptionTool()
    {
        var optionSchema = new
        {
            type = "object",
            properties = new { label = new { type = "string" }, text = new { type = "string" } },
            required = new[] { "label", "text" }
        };

        var questionSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["question_no"] = new { type = "integer" },
                ["stem"] = new { type = "string" },
                ["options"] = new { type = "array", items = optionSchema },
                ["correct_answer"] = new { type = "string" },
                ["has_visual"] = new { type = "boolean" },
                ["visual_description"] = new { type = "string" }
            },
            required = new[] { "stem", "options", "has_visual" }
        };

        return new Tool
        {
            Name = TranscribeToolName,
            Description = "Sayfadaki tüm soruların transkripsiyonunu bildir.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["questions"] = JsonSerializer.SerializeToElement(new { type = "array", items = questionSchema })
                },
                Required = ["questions"]
            }
        };
    }

    private static PageTranscriptionResult ParseTranscription(IReadOnlyDictionary<string, JsonElement> input, AiUsage usage)
    {
        var blocks = new List<TranscribedQuestionBlock>();
        if (input.TryGetValue("questions", out var questionsEl) && questionsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var q in questionsEl.EnumerateArray())
            {
                var options = new List<OptionCandidate>();
                if (q.TryGetProperty("options", out var optionsEl) && optionsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var o in optionsEl.EnumerateArray())
                    {
                        var label = o.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                        var text = o.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                        options.Add(new OptionCandidate(label, text));
                    }
                }

                blocks.Add(new TranscribedQuestionBlock(
                    QuestionNo: q.TryGetProperty("question_no", out var qn) && qn.ValueKind == JsonValueKind.Number ? qn.GetInt32() : null,
                    Stem: q.TryGetProperty("stem", out var s) ? s.GetString() ?? "" : "",
                    Options: options,
                    CorrectAnswer: q.TryGetProperty("correct_answer", out var ca) && ca.ValueKind == JsonValueKind.String ? ca.GetString() : null,
                    HasVisual: q.TryGetProperty("has_visual", out var hv) && hv.ValueKind is JsonValueKind.True or JsonValueKind.False && hv.GetBoolean(),
                    VisualDescription: q.TryGetProperty("visual_description", out var vd) && vd.ValueKind == JsonValueKind.String ? vd.GetString() : null));
            }
        }

        return new PageTranscriptionResult(blocks, usage);
    }

    private async Task<VisualObservation> CallAnthropicAsync(byte[] imagePng, string taskPrompt, CancellationToken ct)
    {
        var (options, client) = Current();
        const string systemPrompt =
            "Sen bir matematik/fizik/kimya ders kitabı görselini analiz eden bir gözlemcisin. " +
            "KURAL: Emin olmadığın bir etiket-değer eşleşmesi varsa (örn. '5' değeri AB'ye mi AC'ye mi " +
            "ait belli değilse) bunu warnings alanında AMBIGUOUS_LABEL_ASSOCIATION olarak işaretle, tahmin " +
            "ile doldurma. Cevabını YALNIZCA submit_visual_observation aracını çağırarak ver.";

        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = options.MaxTokens,
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
        var response = await client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == ToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_visual_observation tool_use bloğu bulunamadı.");

        var inputTokens = (int)response.Usage.InputTokens;
        var outputTokens = (int)response.Usage.OutputTokens;
        var usage = new AiUsage(
            Name, options.Model, inputTokens, outputTokens,
            AnthropicPricing.EstimateCostUsd(options.Model, inputTokens, outputTokens),
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
            ["bounding_box"] = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                description = "Sayfadaki asıl şekli/diyagramı çevreleyen, 0.0-1.0 normalize dikdörtgen. " +
                    "Ayırt edilebilir bir şekil yoksa bu alanı tamamen atla.",
                properties = new
                {
                    x = new { type = "number" },
                    y = new { type = "number" },
                    width = new { type = "number" },
                    height = new { type = "number" }
                },
                required = new[] { "x", "y", "width", "height" }
            }),
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

        VisualBoundingBox? boundingBox = null;
        if (input.TryGetValue("bounding_box", out var bboxEl) && bboxEl.ValueKind == JsonValueKind.Object)
        {
            boundingBox = new VisualBoundingBox(
                GetElDecimal(bboxEl, "x"), GetElDecimal(bboxEl, "y"), GetElDecimal(bboxEl, "width"), GetElDecimal(bboxEl, "height"));
        }

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
            Usage: usage,
            BoundingBox: boundingBox);
    }
}
