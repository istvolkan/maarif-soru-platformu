using System.Diagnostics;
using System.Text.Json;
using MaarifPlatform.Application.Providers;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace MaarifPlatform.Infrastructure.Ai;

/// <summary>§8/§10 Judge ikincil/consensus sağlayıcısı — OpenAI Chat Completions API üzerinden,
/// function-calling ile zorunlu yapılandırılmış çıktı. YALNIZCA EvaluateQuestionAsync gerçek
/// implemente edilmiştir; Analyze/Transform/Generate bu sprintte kapsam dışı (bu provider şu an
/// yalnızca Judge'ın çapraz-sağlayıcı kontrolü için kullanılıyor — bkz. TransformationOrchestrationService).
/// Sistem promptu AnthropicLLMProvider.BuildEvaluateSystemPrompt/BuildEvaluateUserContent'teki
/// AYNI içeriktir — bağımsız çapraz-kontrol için iki sağlayıcıya farklı prompt vermek amaca
/// aykırı olur.</summary>
public class OpenAiLLMProvider : ILLMProvider
{
    private const string EvaluateToolName = "submit_evaluation";

    private readonly OpenAiOptions _options;
    private readonly ChatClient _client;

    public OpenAiLLMProvider(IOptions<OpenAiOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Judge:OpenAI:ApiKey tanımlı değil. Judge çapraz-sağlayıcı consensus için " +
                "appsettings/user-secrets üzerinden bir API anahtarı sağlanmalı; anahtar yoksa " +
                "Judge:SecondaryProvider boş bırakılmalı.");
        }

        _client = new ChatClient(model: _options.Model, apiKey: _options.ApiKey);
    }

    public string Name => "openai";

    public async Task<EvaluateQuestionResult> EvaluateQuestionAsync(EvaluateQuestionRequest request, CancellationToken ct = default)
    {
        var tool = BuildEvaluationTool();
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = _options.MaxTokens,
            ToolChoice = ChatToolChoice.CreateFunctionChoice(EvaluateToolName)
        };
        options.Tools.Add(tool);

        List<ChatMessage> messages =
        [
            new SystemChatMessage(BuildEvaluateSystemPrompt(request)),
            new UserChatMessage(BuildEvaluateUserContent(request))
        ];

        var stopwatch = Stopwatch.StartNew();
        ChatCompletion completion = await _client.CompleteChatAsync(messages, options, ct);
        stopwatch.Stop();

        var toolCall = completion.ToolCalls.FirstOrDefault(t => t.FunctionName == EvaluateToolName)
            ?? throw new InvalidOperationException("OpenAI yanıtında beklenen submit_evaluation tool_call bulunamadı.");

        using var argsDoc = JsonDocument.Parse(toolCall.FunctionArguments);
        var input = argsDoc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

        var usage = completion.Usage;
        var inputTokens = usage.InputTokenCount;
        var outputTokens = usage.OutputTokenCount;
        var aiUsage = new AiUsage(
            Name, _options.Model, inputTokens, outputTokens,
            OpenAiPricing.EstimateCostUsd(_options.Model, inputTokens, outputTokens),
            (int)stopwatch.ElapsedMilliseconds);

        return ParseEvaluateResult(input, aiUsage);
    }

    public Task<AnalyzeQuestionResult> AnalyzeQuestionAsync(AnalyzeQuestionRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("OpenAiLLMProvider şu an yalnızca Judge ikincil sağlayıcısı olarak kullanılıyor (bkz. Sprint 10); Analyze implemente edilmedi.");

    public Task<TransformQuestionResult> TransformQuestionAsync(TransformQuestionRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("OpenAiLLMProvider şu an yalnızca Judge ikincil sağlayıcısı olarak kullanılıyor (bkz. Sprint 10); Transform implemente edilmedi.");

    public Task<GenerateQuestionResult> GenerateQuestionAsync(GenerateQuestionRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("OpenAiLLMProvider şu an yalnızca Judge ikincil sağlayıcısı olarak kullanılıyor (bkz. Sprint 10); Generate implemente edilmedi.");

    private static ChatTool BuildEvaluationTool()
    {
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                quality_score = new { type = "integer", description = "0-100 arası nihai kalite puanı." },
                passed = new { type = "boolean", description = "critical_failures doluysa MUTLAKA false." },
                critical_failures = new
                {
                    type = "array",
                    description = "Yayına engel ciddi hatalar (matematiksel yanlışlık, desteklenmeyen iddia, vb).",
                    items = new { type = "string" }
                },
                quality_flags = new
                {
                    type = "array",
                    description = "Engel olmayan ama editöre bildirilmesi gereken küçük gözlemler.",
                    items = new { type = "string" }
                }
            },
            required = new[] { "quality_score", "passed", "critical_failures", "quality_flags" }
        });

        return ChatTool.CreateFunctionTool(
            EvaluateToolName, "Dönüştürülmüş sorunun kalite değerlendirmesini bildir.", BinaryData.FromString(schema));
    }

    private static string BuildEvaluateSystemPrompt(EvaluateQuestionRequest request) => $"""
        Sen dönüştürülmüş matematik sorularını denetleyen bağımsız bir kalite hakemisin (§8).
        Soruyu orijinaliyle KIYASLAMADAN, kendi başına değerlendir.

        KURALLAR:
        1. Matematiksel doğruluk: çözüm ve doğru cevap tutarlı mı?
        2. Çeldirici kalitesi: yanlış şıklar makul mü, bariz mi?
        3. Kaynak sadakati: aşağıdaki [KAYNAK n] bloklarıyla desteklenmeyen bir kazanım/olgu
           iddiası varsa bunu critical_failures'a ekle.
        4. Açıklık: soru ve çözüm anlaşılır mı?
        5. critical_failures doluysa passed MUTLAKA false olmalı — skor ne olursa olsun.
        6. Cevabını YALNIZCA submit_evaluation aracını çağırarak ver.

        {BuildGroundingBlock(request.Grounding)}
        """;

    private static string BuildEvaluateUserContent(EvaluateQuestionRequest request)
    {
        var options = string.Join("\n", request.Options.Select((o, i) => $"{(char)('A' + i)}) {o}"));

        return $"""
            SORU:
            {request.TransformedQuestion}

            ŞIKLAR:
            {options}

            DOĞRU CEVAP: {request.CorrectAnswer}

            ÇÖZÜM:
            {request.Solution}
            """;
    }

    private static string BuildGroundingBlock(IReadOnlyList<GroundingReference> grounding) =>
        grounding.Count == 0
            ? "(RAG'de hiçbir referans bulunamadı. Kaynaksız kazanım/olgu iddiası üretme.)"
            : "RAG BAĞLAMI:\n" + string.Join("\n\n", grounding.Select((g, i) =>
                $"[KAYNAK {i + 1}] (doküman {g.ReferenceDocumentId}, sayfa {g.Page?.ToString() ?? "?"})\n{g.ChunkText}"));

    private static EvaluateQuestionResult ParseEvaluateResult(IReadOnlyDictionary<string, JsonElement> input, AiUsage usage)
    {
        static List<string> GetStringArray(IReadOnlyDictionary<string, JsonElement> input, string key) =>
            input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
                : [];

        return new EvaluateQuestionResult(
            QualityScore: input.TryGetValue("quality_score", out var qs) ? qs.GetInt32() : 0,
            Passed: input.TryGetValue("passed", out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False && p.GetBoolean(),
            CriticalFailures: GetStringArray(input, "critical_failures"),
            QualityFlags: GetStringArray(input, "quality_flags"),
            Usage: usage);
    }
}
