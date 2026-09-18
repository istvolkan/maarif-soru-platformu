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
    private const string GenerateToolName = "submit_generation";
    private const string ExtractCurriculumToolName = "submit_curriculum_structure";
    private const string ValidateCurriculumAlignmentToolName = "submit_curriculum_alignment";

    private readonly IOptionsMonitor<OpenAiOptions> _optionsMonitor;
    private ChatClient? _client;
    private string? _clientKey;

    public OpenAiLLMProvider(IOptionsMonitor<OpenAiOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public string Name => "openai";

    /// <summary>Sprint 11: bkz. AnthropicLLMProvider.Current() — aynı desen. ChatClient model'i
    /// constructor'da bağladığı için (Anthropic'in aksine), anahtar VEYA model değiştiğinde
    /// yeniden kurulur.</summary>
    private (OpenAiOptions Options, ChatClient Client) Current()
    {
        var options = _optionsMonitor.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "Judge:OpenAI:ApiKey tanımlı değil. Judge çapraz-sağlayıcı consensus için " +
                "appsettings/user-secrets üzerinden bir API anahtarı sağlanmalı; anahtar yoksa " +
                "Judge:SecondaryProvider boş bırakılmalı.");
        }

        var clientKey = $"{options.ApiKey}|{options.Model}";
        if (_client is null || _clientKey != clientKey)
        {
            _client = new ChatClient(model: options.Model, apiKey: options.ApiKey);
            _clientKey = clientKey;
        }

        return (options, _client);
    }

    public async Task<EvaluateQuestionResult> EvaluateQuestionAsync(EvaluateQuestionRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var tool = BuildEvaluationTool();
        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = options.MaxTokens,
            ToolChoice = ChatToolChoice.CreateFunctionChoice(EvaluateToolName)
        };
        chatOptions.Tools.Add(tool);

        List<ChatMessage> messages =
        [
            new SystemChatMessage(BuildEvaluateSystemPrompt(request)),
            new UserChatMessage(BuildEvaluateUserContent(request))
        ];

        var stopwatch = Stopwatch.StartNew();
        ChatCompletion completion = await client.CompleteChatAsync(messages, chatOptions, ct);
        stopwatch.Stop();

        var toolCall = completion.ToolCalls.FirstOrDefault(t => t.FunctionName == EvaluateToolName)
            ?? throw new InvalidOperationException("OpenAI yanıtında beklenen submit_evaluation tool_call bulunamadı.");

        using var argsDoc = JsonDocument.Parse(toolCall.FunctionArguments);
        var input = argsDoc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

        var usage = completion.Usage;
        var inputTokens = usage.InputTokenCount;
        var outputTokens = usage.OutputTokenCount;
        var aiUsage = new AiUsage(
            Name, options.Model, inputTokens, outputTokens,
            OpenAiPricing.EstimateCostUsd(options.Model, inputTokens, outputTokens),
            (int)stopwatch.ElapsedMilliseconds);

        return ParseEvaluateResult(input, aiUsage);
    }

    public Task<AnalyzeQuestionResult> AnalyzeQuestionAsync(AnalyzeQuestionRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("OpenAiLLMProvider şu an yalnızca Judge ikincil sağlayıcısı olarak kullanılıyor (bkz. Sprint 10); Analyze implemente edilmedi.");

    public Task<TransformQuestionResult> TransformQuestionAsync(TransformQuestionRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("OpenAiLLMProvider şu an yalnızca Judge ikincil sağlayıcısı olarak kullanılıyor (bkz. Sprint 10); Transform implemente edilmedi.");

    public Task<RecommendRevisionResult> RecommendRevisionAsync(RecommendRevisionRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("OpenAiLLMProvider şu an yalnızca Judge ikincil sağlayıcısı olarak kullanılıyor (bkz. Sprint 10); RecommendRevision implemente edilmedi.");

    /// <summary>Sistem promptu/sonuç ayrıştırma AnthropicLLMProvider ile PAYLAŞILIR (bkz. o
    /// sınıftaki internal static üyeler) — Extract/Generate/ValidateAlignment, Judge'ın aksine
    /// çapraz-sağlayıcı BAĞIMSIZLIK gerektirmez (birbirini denetlemiyorlar), bu yüzden aynı
    /// promptu iki kez bakımı ayrı yerlerde yapmak yerine tek kaynaktan paylaşmak tercih edildi.</summary>
    public async Task<GenerateQuestionResult> GenerateQuestionAsync(GenerateQuestionRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var tool = BuildGenerationTool();
        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = options.MaxTokens,
            ToolChoice = ChatToolChoice.CreateFunctionChoice(GenerateToolName)
        };
        chatOptions.Tools.Add(tool);

        List<ChatMessage> messages =
        [
            new SystemChatMessage(AnthropicLLMProvider.BuildGenerateSystemPrompt(request)),
            new UserChatMessage(AnthropicLLMProvider.BuildGenerateUserContent(request))
        ];

        var stopwatch = Stopwatch.StartNew();
        ChatCompletion completion = await client.CompleteChatAsync(messages, chatOptions, ct);
        stopwatch.Stop();

        var toolCall = completion.ToolCalls.FirstOrDefault(t => t.FunctionName == GenerateToolName)
            ?? throw new InvalidOperationException("OpenAI yanıtında beklenen submit_generation tool_call bulunamadı.");

        var input = ParseToolArguments(toolCall.FunctionArguments);
        var usage = BuildUsage(completion, stopwatch, options);
        return AnthropicLLMProvider.ParseGenerateResult(input, usage);
    }

    public async Task<ExtractCurriculumResult> ExtractCurriculumStructureAsync(ExtractCurriculumRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var tool = BuildExtractCurriculumTool();
        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = options.MaxTokens,
            ToolChoice = ChatToolChoice.CreateFunctionChoice(ExtractCurriculumToolName)
        };
        chatOptions.Tools.Add(tool);

        List<ChatMessage> messages =
        [
            new SystemChatMessage(AnthropicLLMProvider.BuildExtractCurriculumSystemPrompt(request)),
            new UserChatMessage($"Sınıf {request.Grade}, {request.Subject} için yukarıdaki dokümandan müfredat yapısını çıkar.")
        ];

        var stopwatch = Stopwatch.StartNew();
        ChatCompletion completion = await client.CompleteChatAsync(messages, chatOptions, ct);
        stopwatch.Stop();

        var toolCall = completion.ToolCalls.FirstOrDefault(t => t.FunctionName == ExtractCurriculumToolName)
            ?? throw new InvalidOperationException("OpenAI yanıtında beklenen submit_curriculum_structure tool_call bulunamadı.");

        var input = ParseToolArguments(toolCall.FunctionArguments);
        var usage = BuildUsage(completion, stopwatch, options);
        return AnthropicLLMProvider.ParseExtractCurriculumResult(input, usage);
    }

    public async Task<CurriculumAlignmentResult> ValidateCurriculumAlignmentAsync(ValidateCurriculumAlignmentRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var tool = BuildValidateCurriculumAlignmentTool();
        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = options.MaxTokens,
            ToolChoice = ChatToolChoice.CreateFunctionChoice(ValidateCurriculumAlignmentToolName)
        };
        chatOptions.Tools.Add(tool);

        List<ChatMessage> messages =
        [
            new SystemChatMessage(AnthropicLLMProvider.BuildValidateCurriculumAlignmentSystemPrompt(request)),
            new UserChatMessage(request.QuestionText)
        ];

        var stopwatch = Stopwatch.StartNew();
        ChatCompletion completion = await client.CompleteChatAsync(messages, chatOptions, ct);
        stopwatch.Stop();

        var toolCall = completion.ToolCalls.FirstOrDefault(t => t.FunctionName == ValidateCurriculumAlignmentToolName)
            ?? throw new InvalidOperationException("OpenAI yanıtında beklenen submit_curriculum_alignment tool_call bulunamadı.");

        var input = ParseToolArguments(toolCall.FunctionArguments);
        var usage = BuildUsage(completion, stopwatch, options);

        static List<string> GetStringArray(IReadOnlyDictionary<string, JsonElement> input, string key) =>
            input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
                : [];

        return new CurriculumAlignmentResult(
            MeasuresProcessComponent: input.TryGetValue("measures_process_component", out var mp)
                && mp.ValueKind is JsonValueKind.True or JsonValueKind.False && mp.GetBoolean(),
            LearningOutcomeAlignmentScore: input.TryGetValue("learning_outcome_alignment_score", out var los) ? los.GetInt32() : 0,
            SkillAlignmentScore: input.TryGetValue("skill_alignment_score", out var sas) ? sas.GetInt32() : 0,
            Issues: GetStringArray(input, "issues"),
            Usage: usage);
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseToolArguments(BinaryData functionArguments)
    {
        using var argsDoc = JsonDocument.Parse(functionArguments);
        return argsDoc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private AiUsage BuildUsage(ChatCompletion completion, Stopwatch stopwatch, OpenAiOptions options)
    {
        var inputTokens = completion.Usage.InputTokenCount;
        var outputTokens = completion.Usage.OutputTokenCount;
        return new AiUsage(
            Name, options.Model, inputTokens, outputTokens,
            OpenAiPricing.EstimateCostUsd(options.Model, inputTokens, outputTokens),
            (int)stopwatch.ElapsedMilliseconds);
    }

    private static ChatTool BuildGenerationTool()
    {
        var schema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["question"] = new { type = "string", description = "Üretilen soru metni." },
                ["options"] = new { type = "array", description = "3-6 şık.", items = new { type = "string" }, minItems = 3, maxItems = 6 },
                ["correct_answer"] = new { type = "string", description = "Doğru şıkkın metni (options içindeki değerlerden biri)." },
                ["solution"] = new { type = "string", description = "Adım adım çözüm." },
                ["distractors"] = AnthropicLLMProvider.BuildDistractorsSchema(),
                ["visual_required"] = new
                {
                    type = "boolean",
                    description = "Bu soru için gerçek bir görsel (grafik/koordinat sistemi/geometrik şekil/tablo) " +
                        "üretilmeli mi? Görsel yalnızca çözümün ANLAMLI bir parçasıysa true — dekoratif amaçla ASLA true verme."
                },
                ["visual_spec"] = AnthropicLLMProvider.BuildVisualSpecSchema()
            },
            required = new[] { "question", "options", "correct_answer", "solution", "distractors", "visual_required" }
        };

        return ChatTool.CreateFunctionTool(
            GenerateToolName, "Üretilen sorunun yapılandırılmış sonucunu bildir.",
            BinaryData.FromString(JsonSerializer.Serialize(schema)));
    }

    private static ChatTool BuildExtractCurriculumTool()
    {
        var learningOutcomeSchema = new
        {
            type = "object",
            properties = new
            {
                code = new { type = "string", description = "Dokümanda yazılı resmi kazanım kodu (örn. MAT.9.2.1). Kodu uydurma; dokümanda kod yoksa bu kazanımı hiç döndürme." },
                description = new { type = "string", description = "Kazanımın dokümandaki tam/özet açıklaması." },
                content_frameworks = new { type = "array", items = new { type = "string" }, description = "Bu kazanıma bağlı konu/içerik çerçevesi başlıkları (dokümanda geçtiği şekliyle)." },
                process_components = new { type = "array", items = new { type = "string" }, description = "Bu kazanımın ölçtüğü süreç bileşenleri (dokümanda geçtiği şekliyle)." },
                source_page = new { type = "integer", description = "Bu kazanımın bulunduğu [KAYNAK n] bloğunun sayfa numarası; emin değilsen döndürme." }
            },
            required = new[] { "code", "description" }
        };

        var themeSchema = new
        {
            type = "array",
            description = "Dokümanda gerçekten geçen temalar. Kaynakta olmayan bir tema ASLA uydurma.",
            items = new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string" },
                    source_page = new { type = "integer", description = "Emin değilsen döndürme." },
                    learning_outcomes = new { type = "array", items = learningOutcomeSchema }
                },
                required = new[] { "name", "learning_outcomes" }
            }
        };

        var fieldSkillSchema = new
        {
            type = "array",
            description = "Ders geneline ait alan becerileri (örn. Matematik için MAB1-MAB5). Dokümanda yoksa boş dizi döndür.",
            items = new
            {
                type = "object",
                properties = new
                {
                    code = new { type = "string" },
                    name = new { type = "string" },
                    source_page = new { type = "integer" }
                },
                required = new[] { "code", "name" }
            }
        };

        var schema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["themes"] = themeSchema,
                ["field_skills"] = fieldSkillSchema
            },
            required = new[] { "themes", "field_skills" }
        };

        return ChatTool.CreateFunctionTool(
            ExtractCurriculumToolName,
            "Sağlanan doküman parçalarından (yalnızca dokümanda YAZILI olan) müfredat yapısını bildir. " +
            "Hiçbir tema/kazanım/beceri uydurma — dokümanda bulamadığını boş bırak.",
            BinaryData.FromString(JsonSerializer.Serialize(schema)));
    }

    private static ChatTool BuildValidateCurriculumAlignmentTool()
    {
        var schema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["measures_process_component"] = new
                {
                    type = "boolean",
                    description = "Soru, verilen süreç bileşenlerinden EN AZ birini gerçekten ölçüyorsa true. " +
                        "Yalnızca yüzeysel olarak konuyla ilgiliyse ama süreç bileşenini ölçmüyorsa false."
                },
                ["learning_outcome_alignment_score"] = new { type = "integer", description = "0-100, sorunun kazanım açıklamasıyla ne kadar örtüştüğü." },
                ["skill_alignment_score"] = new { type = "integer", description = "0-100, sorunun beklenen beceriyi ne kadar ölçtüğü." },
                ["issues"] = new
                {
                    type = "array",
                    description = "Uyumsuzluk varsa somut gerekçeler; sorun yoksa boş dizi.",
                    items = new { type = "string" }
                }
            },
            required = new[] { "measures_process_component", "learning_outcome_alignment_score", "skill_alignment_score", "issues" }
        };

        return ChatTool.CreateFunctionTool(
            ValidateCurriculumAlignmentToolName,
            "Sorunun GERÇEK kazanım açıklamasıyla ve süreç bileşenleriyle ölçülebilir uyumunu bildir — " +
            "genel kalite değil, YALNIZCA curriculum hizası.",
            BinaryData.FromString(JsonSerializer.Serialize(schema)));
    }

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
