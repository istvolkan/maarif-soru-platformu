using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Rubric;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Infrastructure.Ai;

/// <summary>§11/§H gerçek Analysis sağlayıcısı — Anthropic Messages API üzerinden, tool-calling
/// ile zorunlu yapılandırılmış çıktı (§I). LLM yalnızca kriter başına HAM puan döner;
/// ağırlıklandırma ve nihai karar RubricEngine'de (Application/Rubric) deterministik hesaplanır.
/// Yalnızca AnalyzeQuestionAsync implemente edilmiştir — Transform/Judge/Generate ileriki
/// sprintlerde eklenecek (bkz. LocalHeuristicLLMProvider'daki simetrik kapsam).</summary>
public class AnthropicLLMProvider : ILLMProvider
{
    private const string ToolName = "submit_analysis";

    private readonly AnthropicOptions _options;
    private readonly AnthropicClient _client;

    public AnthropicLLMProvider(IOptions<AnthropicOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Ai:Anthropic:ApiKey tanımlı değil. Gerçek analiz için appsettings/user-secrets " +
                "üzerinden bir API anahtarı sağlanmalı; anahtar yoksa Ai:Provider=Local kullanın.");
        }

        _client = new AnthropicClient { ApiKey = _options.ApiKey };
    }

    public string Name => "anthropic";

    public async Task<AnalyzeQuestionResult> AnalyzeQuestionAsync(AnalyzeQuestionRequest request, CancellationToken ct = default)
    {
        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = BuildSystemPrompt(request),
            Tools = [BuildAnalysisTool()],
            ToolChoice = new ToolChoiceTool { Name = ToolName },
            Messages = [new() { Role = Role.User, Content = BuildUserContent(request) }],
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == ToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_analysis tool_use bloğu bulunamadı.");

        var inputTokens = (int)response.Usage.InputTokens;
        var outputTokens = (int)response.Usage.OutputTokens;
        var usage = new AiUsage(
            Name,
            _options.Model,
            inputTokens,
            outputTokens,
            AnthropicPricing.EstimateCostUsd(_options.Model, inputTokens, outputTokens),
            (int)stopwatch.ElapsedMilliseconds);

        return ParseResult(toolUse.Input, usage);
    }

    public Task<TransformQuestionResult> TransformQuestionAsync(TransformQuestionRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("Transformation Sprint 5'te eklenecek.");

    public Task<EvaluateQuestionResult> EvaluateQuestionAsync(EvaluateQuestionRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("Quality Judge Sprint 5'te eklenecek.");

    public Task<GenerateQuestionResult> GenerateQuestionAsync(GenerateQuestionRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("Soru üretimi ileriki bir sprintte eklenecek.");

    private static Tool BuildAnalysisTool()
    {
        var criterionEnum = MaarifRubric.Criteria.Select(c => c.Key).ToArray();

        var properties = new Dictionary<string, JsonElement>
        {
            ["mathematical_core"] = Schema("string", "Sorunun ölçtüğü matematiksel öz, kısa ifade."),
            ["learning_outcome_code"] = Schema("string", "RAG bağlamında doğrulanabilen MEB kazanım kodu; bulunamıyorsa alanı hiç döndürme."),
            ["field_skill"] = Schema("string", "İlgili alan becerisi."),
            ["conceptual_skill"] = Schema("string", "İlgili kavramsal beceri."),
            ["context_is_decorative"] = Schema("boolean", "Bağlam çözüm için gerekli değilse (salt süsleme) true."),
            ["manual_review_required"] = Schema("boolean", "RAG bağlamı yetersizse veya emin değilsen true."),
            ["manual_review_reason"] = Schema("string", "manual_review_required=true ise kısa gerekçe."),
            ["criterion_evaluations"] = JsonSerializer.SerializeToElement(new
            {
                type = "array",
                description = "Her rubrik kriteri için ham değerlendirme (ağırlıklandırma burada YAPILMAZ).",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        criterion = new { type = "string", @enum = criterionEnum },
                        score = new { type = "integer", description = "0-100 arası, yalnızca bu kriter için." },
                        explanation = new { type = "string" },
                        source_ref = new { type = "string", description = "İlgiliyse [KAYNAK n] referansı." },
                        critical_gate_violated = new { type = "boolean", description = "Bu kriter critical gate ise ve ihlal edildiyse true." }
                    },
                    required = new[] { "criterion", "score", "explanation", "critical_gate_violated" }
                }
            })
        };

        return new Tool
        {
            Name = ToolName,
            Description = "Soru analizinin yapılandırılmış sonucunu bildir. Bilmediğin veya " +
                "kaynakta bulamadığın bilgiyi uydurma — ilgili alanı boş bırak veya " +
                "manual_review_required=true işaretle.",
            InputSchema = new()
            {
                Properties = properties,
                Required =
                [
                    "mathematical_core", "field_skill", "conceptual_skill",
                    "context_is_decorative", "manual_review_required", "criterion_evaluations"
                ]
            }
        };
    }

    private static JsonElement Schema(string type, string description) =>
        JsonSerializer.SerializeToElement(new { type, description });

    private static string BuildSystemPrompt(AnalyzeQuestionRequest request)
    {
        var criteriaList = string.Join("\n", MaarifRubric.Criteria.Select(c =>
            $"- {c.Key} (ağırlık {c.Weight}{(c.IsCriticalGate ? ", CRITICAL GATE" : "")})"));

        var grounding = request.Grounding.Count == 0
            ? "(Bu soru için RAG'de hiçbir referans bulunamadı. Kazanım/beceri alanlarını uydurma; " +
              "learning_outcome_code alanını döndürme ve manual_review_required=true işaretle.)"
            : string.Join("\n\n", request.Grounding.Select((g, i) =>
                $"[KAYNAK {i + 1}] (doküman {g.ReferenceDocumentId}, sayfa {g.Page?.ToString() ?? "?"})\n{g.ChunkText}"));

        return $"""
            Sen Türkiye Yüzyılı Maarif Modeli'ne göre matematik sorularını analiz eden bir uzmansın.

            KURALLAR:
            1. Yalnızca aşağıdaki [KAYNAK n] bloklarına dayanarak kazanım/beceri iddiası üret.
               Kaynakta olmayan bir MEB kazanımını veya beceri tanımını ASLA uydurma.
            2. Her rubrik kriteri için HAM bir puan (0-100) ver. Ağırlıklandırma ve nihai karar
               senin işin değil — ayrı bir motorda deterministik hesaplanacak.
            3. mathematical_accuracy kriterinde çözüm/sonuç matematiksel olarak hatalıysa
               critical_gate_violated=true işaretle. learning_outcome_alignment kriterinde
               kazanım hiçbir kaynakla doğrulanamıyorsa critical_gate_violated=true işaretle.
            4. Emin değilsen manual_review_required=true döndür ve nedenini yaz — tahmin ile doldurma.
            5. Cevabını YALNIZCA submit_analysis aracını çağırarak ver.

            RUBRİK KRİTERLERİ (bkz. §E):
            {criteriaList}

            RAG BAĞLAMI:
            {grounding}
            """;
    }

    private static string BuildUserContent(AnalyzeQuestionRequest request)
    {
        var options = request.OriginalOptions.Count == 0
            ? "(şık yok — açık uçlu soru)"
            : string.Join("\n", request.OriginalOptions.Select((o, i) => $"{(char)('A' + i)}) {o}"));

        return $"""
            Sınıf: {request.Grade}
            Ders: {request.Subject}

            SORU:
            {request.OriginalQuestion}

            ŞIKLAR:
            {options}

            VERİLEN CEVAP: {request.OriginalAnswer ?? "(belirtilmemiş)"}
            """;
    }

    private static AnalyzeQuestionResult ParseResult(IReadOnlyDictionary<string, JsonElement> input, AiUsage usage)
    {
        string? GetString(string key) =>
            input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        bool GetBool(string key) =>
            input.TryGetValue(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False && v.GetBoolean();

        var evaluations = new List<CriterionEvaluation>();
        if (input.TryGetValue("criterion_evaluations", out var evalArray) && evalArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in evalArray.EnumerateArray())
            {
                var criterion = item.TryGetProperty("criterion", out var c) ? c.GetString() ?? "" : "";
                var score = item.TryGetProperty("score", out var s) ? s.GetInt32() : 0;
                var explanation = item.TryGetProperty("explanation", out var e) ? e.GetString() ?? "" : "";
                var sourceRef = item.TryGetProperty("source_ref", out var sr) && sr.ValueKind == JsonValueKind.String
                    ? sr.GetString() : null;
                var criticalViolated = item.TryGetProperty("critical_gate_violated", out var cg)
                    && cg.ValueKind is JsonValueKind.True or JsonValueKind.False && cg.GetBoolean();

                evaluations.Add(new CriterionEvaluation(criterion, score, explanation, sourceRef, criticalViolated));
            }
        }

        return new AnalyzeQuestionResult(
            MathematicalCore: GetString("mathematical_core") ?? string.Empty,
            LearningOutcomeCode: GetString("learning_outcome_code"),
            FieldSkill: GetString("field_skill") ?? string.Empty,
            ConceptualSkill: GetString("conceptual_skill") ?? string.Empty,
            ContextIsDecorative: GetBool("context_is_decorative"),
            CriterionEvaluations: evaluations,
            ManualReviewRequired: GetBool("manual_review_required"),
            ManualReviewReason: GetString("manual_review_reason"),
            Usage: usage);
    }
}
