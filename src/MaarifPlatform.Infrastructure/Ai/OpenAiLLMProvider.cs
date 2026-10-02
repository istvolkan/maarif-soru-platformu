using System.Diagnostics;
using System.Text.Json;
using MaarifPlatform.Application.Generation;
using MaarifPlatform.Application.Providers;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

#pragma warning disable OPENAI001 // Responses types are marked experimental in OpenAI SDK 2.13.

namespace MaarifPlatform.Infrastructure.Ai;

/// <summary>OpenAI Responses API provider for generation, evaluation and curriculum tools.</summary>
public class OpenAiLLMProvider : ILLMProvider
{
    private const string EvaluateToolName = "submit_evaluation";
    private const string GenerateToolName = "submit_generation";
    private const string ExtractCurriculumToolName = "submit_curriculum_structure";
    private const string ValidateCurriculumAlignmentToolName = "submit_curriculum_alignment";

    private readonly IOptionsMonitor<OpenAiOptions> _optionsMonitor;
    private ResponsesClient? _client;
    private string? _clientKey;

    public OpenAiLLMProvider(IOptionsMonitor<OpenAiOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public string Name => "openai";

    // Select the model per request, including difficulty-based overrides.
    private (OpenAiOptions Options, ResponsesClient Client, string Model) Current(string? modelOverride = null)
    {
        var options = _optionsMonitor.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "Judge:OpenAI:ApiKey tanımlı değil. Judge çapraz-sağlayıcı consensus için " +
                "appsettings/user-secrets üzerinden bir API anahtarı sağlanmalı; anahtar yoksa " +
                "Judge:SecondaryProvider boş bırakılmalı.");
        }

        var model = modelOverride ?? options.Model;
        var clientKey = $"{options.ApiKey}|{model}";
        if (_client is null || _clientKey != clientKey)
        {
            _client = new ResponsesClient(options.ApiKey);
            _clientKey = clientKey;
        }

        return (options, _client, model);
    }

    public async Task<EvaluateQuestionResult> EvaluateQuestionAsync(EvaluateQuestionRequest request, CancellationToken ct = default)
    {
        var (options, client, model) = Current(request.ModelOverride);
        var tool = BuildEvaluationTool();
        var responseOptions = new CreateResponseOptions
        {
            Model = model,
            StoredOutputEnabled = false,
            MaxOutputTokenCount = options.MaxTokens,
            ToolChoice = ResponseToolChoice.CreateFunctionChoice(EvaluateToolName)
        };
        responseOptions.Tools.Add(tool);

        List<ResponseItem> messages =
        [
            ResponseItem.CreateSystemMessageItem(BuildEvaluateSystemPrompt(request)),
            ResponseItem.CreateUserMessageItem(BuildEvaluateUserContent(request))
        ];

        var stopwatch = Stopwatch.StartNew();
        foreach (var message in messages) responseOptions.InputItems.Add(message);
        ResponseResult completion = await client.CreateResponseAsync(responseOptions, ct);
        stopwatch.Stop();

        var toolCall = completion.OutputItems.OfType<FunctionCallResponseItem>().FirstOrDefault(t => t.FunctionName == EvaluateToolName)
            ?? throw new InvalidOperationException("OpenAI yanıtında beklenen submit_evaluation tool_call bulunamadı.");

        using var argsDoc = JsonDocument.Parse(toolCall.FunctionArguments);
        var input = argsDoc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

        var aiUsage = BuildUsage(completion, stopwatch, model);
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
        var (options, client, model) = Current(request.ModelOverride);
        var tool = BuildGenerationTool(MultipleChoiceOptionPolicy.RequiredOptionCountFor(request.Grade, request.QuestionType));
        var responseOptions = new CreateResponseOptions
        {
            Model = model,
            StoredOutputEnabled = false,
            MaxOutputTokenCount = options.MaxTokens,
            ToolChoice = ResponseToolChoice.CreateFunctionChoice(GenerateToolName)
        };
        responseOptions.Tools.Add(tool);

        List<ResponseItem> messages =
        [
            ResponseItem.CreateSystemMessageItem(AnthropicLLMProvider.BuildGenerateSystemPrompt(request)),
            ResponseItem.CreateUserMessageItem(AnthropicLLMProvider.BuildGenerateUserContent(request))
        ];

        var stopwatch = Stopwatch.StartNew();
        foreach (var message in messages) responseOptions.InputItems.Add(message);
        ResponseResult completion = await client.CreateResponseAsync(responseOptions, ct);
        stopwatch.Stop();

        var toolCall = completion.OutputItems.OfType<FunctionCallResponseItem>().FirstOrDefault(t => t.FunctionName == GenerateToolName)
            ?? throw new InvalidOperationException("OpenAI yanıtında beklenen submit_generation tool_call bulunamadı.");

        var input = ParseToolArguments(toolCall.FunctionArguments);
        var usage = BuildUsage(completion, stopwatch, model);
        return AnthropicLLMProvider.ParseGenerateResult(input, usage);
    }

    public async Task<ExtractCurriculumResult> ExtractCurriculumStructureAsync(ExtractCurriculumRequest request, CancellationToken ct = default)
    {
        var (options, client, model) = Current();
        var tool = BuildExtractCurriculumTool();
        var responseOptions = new CreateResponseOptions
        {
            Model = model,
            StoredOutputEnabled = false,
            MaxOutputTokenCount = options.MaxTokens,
            ToolChoice = ResponseToolChoice.CreateFunctionChoice(ExtractCurriculumToolName)
        };
        responseOptions.Tools.Add(tool);

        List<ResponseItem> messages =
        [
            ResponseItem.CreateSystemMessageItem(AnthropicLLMProvider.BuildExtractCurriculumSystemPrompt(request)),
            ResponseItem.CreateUserMessageItem($"Sınıf {request.Grade}, {request.Subject} için yukarıdaki dokümandan müfredat yapısını çıkar.")
        ];

        var stopwatch = Stopwatch.StartNew();
        foreach (var message in messages) responseOptions.InputItems.Add(message);
        ResponseResult completion = await client.CreateResponseAsync(responseOptions, ct);
        stopwatch.Stop();

        var toolCall = completion.OutputItems.OfType<FunctionCallResponseItem>().FirstOrDefault(t => t.FunctionName == ExtractCurriculumToolName)
            ?? throw new InvalidOperationException("OpenAI yanıtında beklenen submit_curriculum_structure tool_call bulunamadı.");

        var input = ParseToolArguments(toolCall.FunctionArguments);
        var usage = BuildUsage(completion, stopwatch, model);
        return AnthropicLLMProvider.ParseExtractCurriculumResult(input, usage);
    }

    public async Task<CurriculumAlignmentResult> ValidateCurriculumAlignmentAsync(ValidateCurriculumAlignmentRequest request, CancellationToken ct = default)
    {
        var (options, client, model) = Current(request.ModelOverride);
        var tool = BuildValidateCurriculumAlignmentTool();
        var responseOptions = new CreateResponseOptions
        {
            Model = model,
            StoredOutputEnabled = false,
            MaxOutputTokenCount = options.MaxTokens,
            ToolChoice = ResponseToolChoice.CreateFunctionChoice(ValidateCurriculumAlignmentToolName)
        };
        responseOptions.Tools.Add(tool);

        List<ResponseItem> messages =
        [
            ResponseItem.CreateSystemMessageItem(AnthropicLLMProvider.BuildValidateCurriculumAlignmentSystemPrompt(request)),
            ResponseItem.CreateUserMessageItem(request.QuestionText)
        ];

        var stopwatch = Stopwatch.StartNew();
        foreach (var message in messages) responseOptions.InputItems.Add(message);
        ResponseResult completion = await client.CreateResponseAsync(responseOptions, ct);
        stopwatch.Stop();

        var toolCall = completion.OutputItems.OfType<FunctionCallResponseItem>().FirstOrDefault(t => t.FunctionName == ValidateCurriculumAlignmentToolName)
            ?? throw new InvalidOperationException("OpenAI yanıtında beklenen submit_curriculum_alignment tool_call bulunamadı.");

        var input = ParseToolArguments(toolCall.FunctionArguments);
        var usage = BuildUsage(completion, stopwatch, model);

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

    /// <summary>§16 zorluk bazlı yönlendirme: model artık options.Model ile aynı olmayabilir
    /// (Current(modelOverride) çözülmüş modeli döner) — maliyet/kayıt için o çözülmüş model
    /// kullanılır, tek bir global "OpenAI modeli" varsayımı geçerli değil.</summary>
    private AiUsage BuildUsage(ResponseResult completion, Stopwatch stopwatch, string model)
    {
        var inputTokens = completion.Usage.InputTokenCount;
        var outputTokens = completion.Usage.OutputTokenCount;
        var cachedTokens = completion.Usage.InputTokenDetails?.CachedTokenCount ?? 0;
        return new AiUsage(
            Name, model, inputTokens, outputTokens,
            OpenAiPricing.EstimateCostUsd(model, inputTokens, outputTokens, cachedTokens),
            (int)stopwatch.ElapsedMilliseconds,
            CacheCreationInputTokens: 0,
            CacheReadInputTokens: cachedTokens);
    }

    // Preserve optional properties in the existing tool schemas.
    private static ResponseTool CreateFunctionTool(string name, string description, BinaryData parameters, bool strictModeEnabled)
        => ResponseTool.CreateFunctionTool(name, parameters, strictModeEnabled, description);

    private static ResponseTool BuildGenerationTool(int? requiredOptionCount = null)
    {
        var optionsSchema = requiredOptionCount is int n
            ? new { type = "array", description = $"Tam olarak {n} şık (bundan az ya da çok ASLA).", items = new { type = "string" }, minItems = n, maxItems = n }
            : new { type = "array", description = "3-6 şık.", items = new { type = "string" }, minItems = 3, maxItems = 6 };

        var schema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["question"] = new { type = "string", description = "Üretilen soru metni." },
                ["options"] = optionsSchema,
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

        return CreateFunctionTool(
            GenerateToolName, "Üretilen sorunun yapılandırılmış sonucunu bildir.",
            BinaryData.FromString(JsonSerializer.Serialize(schema)), strictModeEnabled: false);
    }

    private static ResponseTool BuildExtractCurriculumTool()
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

        return CreateFunctionTool(
            ExtractCurriculumToolName,
            "Sağlanan doküman parçalarından (yalnızca dokümanda YAZILI olan) müfredat yapısını bildir. " +
            "Hiçbir tema/kazanım/beceri uydurma — dokümanda bulamadığını boş bırak.",
            BinaryData.FromString(JsonSerializer.Serialize(schema)), strictModeEnabled: false);
    }

    private static ResponseTool BuildValidateCurriculumAlignmentTool()
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

        return CreateFunctionTool(
            ValidateCurriculumAlignmentToolName,
            "Sorunun GERÇEK kazanım açıklamasıyla ve süreç bileşenleriyle ölçülebilir uyumunu bildir — " +
            "genel kalite değil, YALNIZCA curriculum hizası.",
            BinaryData.FromString(JsonSerializer.Serialize(schema)), strictModeEnabled: false);
    }

    private static ResponseTool BuildEvaluationTool()
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

        return CreateFunctionTool(
            EvaluateToolName, "Dönüştürülmüş sorunun kalite değerlendirmesini bildir.", BinaryData.FromString(schema), strictModeEnabled: false);
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
