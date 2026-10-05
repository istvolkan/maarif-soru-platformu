using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using MaarifPlatform.Application.Generation;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Rubric;
using MaarifPlatform.Application.Visuals;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Infrastructure.Ai;

/// <summary>§11/§H gerçek Analysis/Transformation/Judge/Generation sağlayıcısı — Anthropic
/// Messages API üzerinden, tool-calling ile zorunlu yapılandırılmış çıktı (§I). Analyze'de
/// LLM yalnızca kriter başına HAM puan döner; ağırlıklandırma ve nihai karar RubricEngine'de
/// (Application/Rubric) deterministik hesaplanır. Transform/Judge/Generate için LLM nihai
/// çıktıyı doğrudan üretir (bkz. EvaluateQuestionResult — kriter bazlı ayrıştırma yok,
/// RubricEngine'i kullanmaz). Dört ILLMProvider metodu da (Analyze/Transform/Evaluate/Generate)
/// implemente edilmiştir — bkz. LocalHeuristicLLMProvider'daki simetrik mock kapsam.</summary>
public class AnthropicLLMProvider : ILLMProvider
{
    private const string ToolName = "submit_analysis";
    private const string DnaAnalysisToolName = "submit_dna_analysis";
    private const string TransformToolName = "submit_transformation";
    private const string EvaluateToolName = "submit_evaluation";
    private const string GenerateToolName = "submit_generation";
    private const string RecommendRevisionToolName = "submit_revision_recommendation";
    private const string ExtractCurriculumToolName = "submit_curriculum_structure";
    private const string ValidateCurriculumAlignmentToolName = "submit_curriculum_alignment";
    private const string SolveQuestionToolName = "submit_solution";
    private const string VaryQuestionToolName = "submit_question_variations";

    private readonly IOptionsMonitor<AnthropicOptions> _optionsMonitor;
    private AnthropicClient? _client;
    private string? _clientKey;

    public AnthropicLLMProvider(IOptionsMonitor<AnthropicOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public string Name => "anthropic";

    /// <summary>Sprint 11: IOptionsMonitor.CurrentValue her çağrıda taze okunur — Admin Ayarlar
    /// ekranından değiştirilen Ai:Anthropic:ApiKey/Model yeniden başlatma gerektirmeden etkili
    /// olur. Client yalnızca anahtar gerçekten değiştiğinde yeniden kurulur (gereksiz nesne
    /// oluşturmayı önler). Fırlatma constructor-zamanından ilk-çağrı-zamanına taşındı — hiç
    /// çağrılmayan bir sağlayıcının (örn. Ai:Provider=Local iken) artık anahtara ihtiyacı yok.</summary>
    private (AnthropicOptions Options, AnthropicClient Client) Current()
    {
        var options = _optionsMonitor.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "Ai:Anthropic:ApiKey tanımlı değil. Gerçek analiz için appsettings/user-secrets " +
                "üzerinden bir API anahtarı sağlanmalı; anahtar yoksa Ai:Provider=Local kullanın.");
        }

        if (_client is null || _clientKey != options.ApiKey)
        {
            _client = new AnthropicClient { ApiKey = options.ApiKey };
            _clientKey = options.ApiKey;
        }

        return (options, _client);
    }

    public async Task<AnalyzeQuestionResult> AnalyzeQuestionAsync(AnalyzeQuestionRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = options.MaxTokens,
            System = BuildSystemPrompt(request),
            Tools = [BuildAnalysisTool()],
            ToolChoice = new ToolChoiceTool { Name = ToolName },
            Messages = [new() { Role = Role.User, Content = BuildUserContent(request) }],
            CacheControl = new CacheControlEphemeral(),
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == ToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_analysis tool_use bloğu bulunamadı.");

        var usage = BuildUsage(response, stopwatch, options);
        return ParseResult(toolUse.Input, usage);
    }

    /// <summary>§43/§44/§60 LLM-B: Question DNA Analysis — AnalyzeQuestionAsync'ten (LLM-A) ayrı
    /// bir çağrı, soruyu YENİDEN gönderir (aynı isteğin içine gömmek yerine) çünkü iki rol
    /// kavramsal olarak bağımsızdır (bkz. ILLMProvider doc) ve ileride farklı sağlayıcılara
    /// yönlendirilebilir olmalıdır.</summary>
    public async Task<AnalyzeQuestionDnaResult> AnalyzeQuestionDnaAsync(AnalyzeQuestionDnaRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var model = request.ModelOverride ?? options.Model;
        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = options.MaxTokens,
            System = BuildDnaAnalysisSystemPrompt(),
            Tools = [BuildDnaAnalysisTool()],
            ToolChoice = new ToolChoiceTool { Name = DnaAnalysisToolName },
            Messages = [new() { Role = Role.User, Content = BuildDnaAnalysisUserContent(request) }],
            CacheControl = new CacheControlEphemeral(),
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == DnaAnalysisToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_dna_analysis tool_use bloğu bulunamadı.");

        var usage = BuildUsage(response, stopwatch, options, model);
        var input = toolUse.Input;

        static List<string> GetStringArray(IReadOnlyDictionary<string, JsonElement> input, string key) =>
            input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : [];

        return new AnalyzeQuestionDnaResult(
            QuestionArchetype: input.TryGetValue("question_archetype", out var qa) && qa.ValueKind == JsonValueKind.String ? qa.GetString() : null,
            ReasoningPattern: GetStringArray(input, "reasoning_pattern"),
            MisconceptionTargets: GetStringArray(input, "misconception_targets"),
            DistractorLogic: GetStringArray(input, "distractor_logic"),
            AbstractionLevel: input.TryGetValue("abstraction_level", out var al) && al.ValueKind == JsonValueKind.String ? al.GetString() : null,
            Usage: usage);
    }

    private static Tool BuildDnaAnalysisTool() => new()
    {
        Name = DnaAnalysisToolName,
        Description = "Sorunun arkasındaki soyut yapıyı (metnini değil) bildir.",
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["question_archetype"] = Schema("string",
                    "Bu sorunun ait olduğu soyut soru kalıbı, kısa bir etiket (ör. \"Grafik → İlişki → " +
                    "Cebirsel Model → Tahmin\", \"Gerçek Yaşam → Değişken Belirleme → Modelleme → Karar\"). " +
                    "Önceden tanımlı bir listeden SEÇME — sorunun kendi mantığından TÜRET."),
                ["reasoning_pattern"] = JsonSerializer.SerializeToElement(new
                {
                    type = "array",
                    description = "Öğrencinin izlemesi gereken muhakeme ZİNCİRİ, SIRALI adımlar halinde " +
                        "(ör. [\"OBSERVE\",\"RELATE\",\"REPRESENT\",\"MODEL\",\"INFER\"] veya " +
                        "[\"READ_DATA\",\"DETECT_PATTERN\",\"HYPOTHESIZE\",\"VERIFY\",\"GENERALIZE\"]). " +
                        "Sabit bir listeden seçme — bu sorunun GERÇEK adımlarını yaz, İngilizce/kısa " +
                        "SNAKE_CASE etiketler kullan.",
                    items = new { type = "string" }
                }),
                ["misconception_targets"] = JsonSerializer.SerializeToElement(new
                {
                    type = "array",
                    description = "Bu sorunun test ettiği/tetikleyebileceği tipik öğrenci kavram " +
                        "yanılgıları (ör. \"işlem önceliğini unutma\", \"negatif sayılarla çarpımda işaret hatası\").",
                    items = new { type = "string" }
                }),
                ["distractor_logic"] = JsonSerializer.SerializeToElement(new
                {
                    type = "array",
                    description = "Yanlış şıkların HANGİ mantıkla yanlış cevaba götürdüğü (ör. \"paydaları " +
                        "toplayan öğrencinin bulacağı yanlış sonuç\"). Şık yoksa boş dizi döndür.",
                    items = new { type = "string" }
                }),
                ["abstraction_level"] = Schema("string",
                    "Sorunun somuttan soyuta nerede durduğu (ör. \"Somut/Sayısal\", \"Yarı-Soyut/Temsili\", " +
                    "\"Soyut/Sembolik\").")
            },
            Required = []
        }
    };

    private static string BuildDnaAnalysisSystemPrompt() => """
        Sen matematik sorularının YAPISINI analiz eden bir uzmansın. Görevin sorunun METNİNİ
        özetlemek DEĞİL, sorunun öğrenciyi NASIL DÜŞÜNDÜRDÜĞÜNÜ ortaya çıkarmaktır.

        "Bu soru nasıl yazılmış?" sorusuna değil, "bu soru öğrenciyi nasıl düşündürüyor?"
        sorusuna cevap ver: hangi bilgiyi doğrudan verir, hangisini vermez, hangi ilişkiyi
        keşfettirir, hangi temsil dönüşümünü yaptırır, kaç muhakeme adımı gerektirir, hangi
        kavram yanılgısını test eder.

        KURALLAR:
        1. Sabit/önceden tanımlı bir kategori listesinden SEÇME — her alanı bu SORUNUN kendi
           mantığından türet. Aynı iki soru nadiren birebir aynı archetype/reasoning_pattern'a
           sahip olmalı; kopyala-yapıştır genel etiketlerden kaçın.
        2. reasoning_pattern SIRALIDIR — adımları öğrencinin gerçekte izleyeceği sırayla yaz.
        3. Emin olmadığın bir alanı boş bırak (uydurma) — hiçbiri zorunlu değil.
        4. Cevabını YALNIZCA submit_dna_analysis aracını çağırarak ver.
        """;

    private static string BuildDnaAnalysisUserContent(AnalyzeQuestionDnaRequest request)
    {
        var optionsBlock = request.Options.Count == 0
            ? "(şık yok — açık uçlu soru)"
            : string.Join("\n", request.Options.Select((o, i) => $"{(char)('A' + i)}) {o}"));

        return $"""
            SORU:
            {request.Question}

            ŞIKLAR:
            {optionsBlock}

            DOĞRU CEVAP: {request.CorrectAnswer ?? "(belirtilmemiş)"}
            """;
    }

    public async Task<TransformQuestionResult> TransformQuestionAsync(TransformQuestionRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = options.MaxTokens,
            System = BuildTransformSystemPrompt(request),
            Tools = [BuildTransformationTool()],
            ToolChoice = new ToolChoiceTool { Name = TransformToolName },
            Messages = [new() { Role = Role.User, Content = request.OriginalQuestion }],
            CacheControl = new CacheControlEphemeral(),
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == TransformToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_transformation tool_use bloğu bulunamadı.");

        var usage = BuildUsage(response, stopwatch, options);
        return ParseTransformResult(toolUse.Input, usage);
    }

    public async Task<EvaluateQuestionResult> EvaluateQuestionAsync(EvaluateQuestionRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var model = request.ModelOverride ?? options.Model;
        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = options.MaxTokens,
            System = BuildEvaluateSystemPrompt(request),
            Tools = [BuildEvaluationTool()],
            ToolChoice = new ToolChoiceTool { Name = EvaluateToolName },
            Messages = [new() { Role = Role.User, Content = BuildEvaluateUserContent(request) }],
            CacheControl = new CacheControlEphemeral(),
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == EvaluateToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_evaluation tool_use bloğu bulunamadı.");

        var usage = BuildUsage(response, stopwatch, options, model);
        return ParseEvaluateResult(toolUse.Input, usage);
    }

    public async Task<GenerateQuestionResult> GenerateQuestionAsync(GenerateQuestionRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var model = request.ModelOverride ?? options.Model;
        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = options.MaxTokens,
            System = BuildGenerateSystemPrompt(request),
            Tools = [BuildGenerationTool(MultipleChoiceOptionPolicy.RequiredOptionCountFor(request.Grade, request.QuestionType))],
            ToolChoice = new ToolChoiceTool { Name = GenerateToolName },
            Messages = [new() { Role = Role.User, Content = BuildGenerateUserContent(request) }],
            CacheControl = new CacheControlEphemeral(),
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == GenerateToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_generation tool_use bloğu bulunamadı.");

        var usage = BuildUsage(response, stopwatch, options, model);
        return ParseGenerateResult(toolUse.Input, usage);
    }

    public async Task<RecommendRevisionResult> RecommendRevisionAsync(RecommendRevisionRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = options.MaxTokens,
            System = BuildRecommendRevisionSystemPrompt(request),
            Tools = [BuildRecommendRevisionTool()],
            ToolChoice = new ToolChoiceTool { Name = RecommendRevisionToolName },
            Messages = [new() { Role = Role.User, Content = request.OriginalQuestion }],
            CacheControl = new CacheControlEphemeral(),
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == RecommendRevisionToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_revision_recommendation tool_use bloğu bulunamadı.");

        var usage = BuildUsage(response, stopwatch, options);
        var suggestion = toolUse.Input.TryGetValue("revision_suggestion", out var s) ? s.GetString() ?? "" : "";
        return new RecommendRevisionResult(suggestion, usage);
    }

    public async Task<ExtractCurriculumResult> ExtractCurriculumStructureAsync(ExtractCurriculumRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = options.MaxTokens,
            System = BuildExtractCurriculumSystemPrompt(request),
            Tools = [BuildExtractCurriculumTool()],
            ToolChoice = new ToolChoiceTool { Name = ExtractCurriculumToolName },
            Messages = [new() { Role = Role.User, Content = $"Sınıf {request.Grade}, {request.Subject} için yukarıdaki dokümandan müfredat yapısını çıkar." }],
            CacheControl = new CacheControlEphemeral(),
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == ExtractCurriculumToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_curriculum_structure tool_use bloğu bulunamadı.");

        var usage = BuildUsage(response, stopwatch, options);
        return ParseExtractCurriculumResult(toolUse.Input, usage);
    }

    private static Tool BuildExtractCurriculumTool()
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

        return new Tool
        {
            Name = ExtractCurriculumToolName,
            Description = "Sağlanan doküman parçalarından (yalnızca dokümanda YAZILI olan) müfredat " +
                "yapısını bildir. Hiçbir tema/kazanım/beceri uydurma — dokümanda bulamadığını boş bırak.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["themes"] = JsonSerializer.SerializeToElement(themeSchema),
                    ["field_skills"] = JsonSerializer.SerializeToElement(fieldSkillSchema)
                },
                Required = ["themes", "field_skills"]
            }
        };
    }

    internal static string BuildExtractCurriculumSystemPrompt(ExtractCurriculumRequest request)
    {
        var grounding = request.DocumentChunks.Count == 0
            ? "(Doküman parçası verilmedi — themes ve field_skills için boş dizi döndür.)"
            : BuildGroundingBlock(request.DocumentChunks);

        return $"""
            Sen resmi Türkiye Yüzyılı Maarif Modeli öğretim programı dokümanlarından yapılandırılmış
            müfredat verisi çıkaran bir uzmansın. Sınıf {request.Grade}, Ders: {request.Subject}.

            KRİTİK KURAL:
            Bu bir İÇERİK ÜRETİMİ görevi DEĞİL, bir ÇIKARIM (extraction) görevidir. Yalnızca aşağıdaki
            [KAYNAK n] bloklarında GERÇEKTEN YAZILI olan tema/kazanım kodu/içerik çerçevesi/süreç
            bileşeni/alan becerisini yapılandır. Kaynakta olmayan hiçbir şeyi ASLA uydurma — emin
            değilsen o alanı boş bırak veya o kazanımı/temayı hiç döndürme. Bu doküman verilen
            Sınıf/Ders'e ait değilse veya ilgili içerik bulunamıyorsa themes/field_skills için boş
            dizi döndür (uydurmaktan iyidir).

            {grounding}

            Cevabını YALNIZCA submit_curriculum_structure aracını çağırarak ver.
            """;
    }

    internal static ExtractCurriculumResult ParseExtractCurriculumResult(IReadOnlyDictionary<string, JsonElement> input, AiUsage usage)
    {
        static int? GetOptionalInt(JsonElement item, string key) =>
            item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

        static List<string> GetStringArray(JsonElement item, string key) =>
            item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
                : [];

        var themes = new List<CurriculumThemeCandidate>();
        if (input.TryGetValue("themes", out var themesEl) && themesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var themeItem in themesEl.EnumerateArray())
            {
                var outcomes = new List<CurriculumLearningOutcomeCandidate>();
                if (themeItem.TryGetProperty("learning_outcomes", out var outcomesEl) && outcomesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var outcomeItem in outcomesEl.EnumerateArray())
                    {
                        outcomes.Add(new CurriculumLearningOutcomeCandidate(
                            Code: outcomeItem.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "",
                            Description: outcomeItem.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                            ContentFrameworks: GetStringArray(outcomeItem, "content_frameworks"),
                            ProcessComponents: GetStringArray(outcomeItem, "process_components"),
                            SourcePage: GetOptionalInt(outcomeItem, "source_page")));
                    }
                }

                themes.Add(new CurriculumThemeCandidate(
                    Name: themeItem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    LearningOutcomes: outcomes,
                    SourcePage: GetOptionalInt(themeItem, "source_page")));
            }
        }

        var fieldSkills = new List<CurriculumFieldSkillCandidate>();
        if (input.TryGetValue("field_skills", out var skillsEl) && skillsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var skillItem in skillsEl.EnumerateArray())
            {
                fieldSkills.Add(new CurriculumFieldSkillCandidate(
                    Code: skillItem.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "",
                    Name: skillItem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    SourcePage: GetOptionalInt(skillItem, "source_page")));
            }
        }

        return new ExtractCurriculumResult(themes, fieldSkills, usage);
    }

    public async Task<CurriculumAlignmentResult> ValidateCurriculumAlignmentAsync(ValidateCurriculumAlignmentRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var model = request.ModelOverride ?? options.Model;
        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = options.MaxTokens,
            System = BuildValidateCurriculumAlignmentSystemPrompt(request),
            Tools = [BuildValidateCurriculumAlignmentTool()],
            ToolChoice = new ToolChoiceTool { Name = ValidateCurriculumAlignmentToolName },
            Messages = [new() { Role = Role.User, Content = request.QuestionText }],
            CacheControl = new CacheControlEphemeral(),
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == ValidateCurriculumAlignmentToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_curriculum_alignment tool_use bloğu bulunamadı.");

        var usage = BuildUsage(response, stopwatch, options, model);
        var input = toolUse.Input;

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

    /// <summary>§61 Independent Solver. Generator'ın CorrectAnswer/Solution'ı BİLEREK bu isteğe
    /// dahil edilmez (bkz. SolveQuestionRequest doc) — soru yalnızca metin+şıklarla sıfırdan
    /// çözülür, sonuç GenerationOrchestrationService'te Generator'ın cevabıyla karşılaştırılır.</summary>
    public async Task<SolveQuestionResult> SolveQuestionAsync(SolveQuestionRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var model = request.ModelOverride ?? options.Model;
        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = options.MaxTokens,
            System = BuildSolveQuestionSystemPrompt(),
            Tools = [BuildSolveQuestionTool()],
            ToolChoice = new ToolChoiceTool { Name = SolveQuestionToolName },
            Messages = [new() { Role = Role.User, Content = BuildSolveQuestionUserContent(request) }],
            CacheControl = new CacheControlEphemeral(),
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == SolveQuestionToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_solution tool_use bloğu bulunamadı.");

        var usage = BuildUsage(response, stopwatch, options, model);
        var input = toolUse.Input;
        return new SolveQuestionResult(
            Answer: input.TryGetValue("answer", out var a) ? a.GetString() ?? "" : "",
            Reasoning: input.TryGetValue("reasoning", out var r) ? r.GetString() ?? "" : "",
            Usage: usage);
    }

    private static Tool BuildSolveQuestionTool() => new()
    {
        Name = SolveQuestionToolName,
        Description = "Soruyu sıfırdan çöz ve vardığın cevabı bildir.",
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["reasoning"] = Schema("string", "Çözüm adımların, kısa ve net."),
                ["answer"] = Schema("string",
                    "Vardığın SONUÇ. Şık listesi verildiyse bu, şıklardan BİRİNİN TAM METNİYLE " +
                    "harfiyen eşleşmelidir (örn. \"12\" değil, şıkta yazdığı gibi \"12 cm\").")
            },
            Required = ["reasoning", "answer"]
        }
    };

    internal static string BuildSolveQuestionSystemPrompt() => """
        Sen bir matematik/fen sorusunu SIFIRDAN çözen bağımsız bir çözücüsün. Sana sorunun
        metni ve (varsa) şıkları verilecek — DOĞRU CEVAP VERİLMEYECEK, çünkü amaç başka bir
        modelin iddia ettiği cevabı ONAYLAMAK değil, kendi başına bağımsız bir sonuca ulaşmaktır.

        KURALLAR:
        1. Soruyu adım adım, kendi başına çöz. Hiçbir dış cevaba güvenme/varsayma.
        2. Şık listesi verildiyse, vardığın sonucu şıklardan biriyle eşleştir ve o şıkkın TAM
           METNİNİ "answer" alanına yaz (yeniden ifade etme, harfiyen kopyala).
        3. Hiçbir şık senin bulduğun sonuca uymuyorsa, yine de en yakın/mantıklı olanı seç ve
           reasoning'de bu tutarsızlığı belirt.
        4. Cevabını YALNIZCA submit_solution aracını çağırarak ver.
        """;

    internal static string BuildSolveQuestionUserContent(SolveQuestionRequest request)
    {
        var optionsBlock = request.Options.Count == 0
            ? "(Şık yok — açık uçlu soru.)"
            : string.Join("\n", request.Options.Select((o, i) => $"{(char)('A' + i)}) {o}"));

        return $"""
            SORU:
            {request.Question}

            ŞIKLAR:
            {optionsBlock}
            """;
    }

    /// <summary>Soru Çeşitlendir — Maarif Modeli müfredat doğrulaması YAPMAZ (bkz. ILLMProvider'daki
    /// doc), yalnızca kullanıcının sağladığı örnek soruyu küçük/mantıklı değişikliklerle çoğaltır.</summary>
    public async Task<VaryQuestionResult> VaryQuestionAsync(VaryQuestionRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var model = request.ModelOverride ?? options.Model;
        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = options.MaxTokens,
            System = BuildVaryQuestionSystemPrompt(request),
            Tools = [BuildVaryQuestionTool()],
            ToolChoice = new ToolChoiceTool { Name = VaryQuestionToolName },
            Messages = [new() { Role = Role.User, Content = "Yukarıdaki kaynak sorudan istenen sayıda varyasyon üret." }],
            CacheControl = new CacheControlEphemeral(),
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == VaryQuestionToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_question_variations tool_use bloğu bulunamadı.");

        var usage = BuildUsage(response, stopwatch, options, model);
        return ParseVaryQuestionResult(toolUse.Input, usage);
    }

    private static Tool BuildValidateCurriculumAlignmentTool() => new()
    {
        Name = ValidateCurriculumAlignmentToolName,
        Description = "Sorunun GERÇEK kazanım açıklamasıyla ve süreç bileşenleriyle ölçülebilir " +
            "uyumunu bildir — genel kalite değil, YALNIZCA curriculum hizası.",
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["measures_process_component"] = Schema("boolean",
                    "Soru, verilen süreç bileşenlerinden EN AZ birini gerçekten ölçüyorsa true. " +
                    "Yalnızca yüzeysel olarak konuyla ilgiliyse ama süreç bileşenini ölçmüyorsa false."),
                ["learning_outcome_alignment_score"] = Schema("integer", "0-100, sorunun kazanım açıklamasıyla ne kadar örtüştüğü."),
                ["skill_alignment_score"] = Schema("integer", "0-100, sorunun beklenen beceriyi ne kadar ölçtüğü."),
                ["issues"] = JsonSerializer.SerializeToElement(new
                {
                    type = "array",
                    description = "Uyumsuzluk varsa somut gerekçeler; sorun yoksa boş dizi.",
                    items = new { type = "string" }
                })
            },
            Required = ["measures_process_component", "learning_outcome_alignment_score", "skill_alignment_score", "issues"]
        }
    };

    internal static string BuildValidateCurriculumAlignmentSystemPrompt(ValidateCurriculumAlignmentRequest request)
    {
        var componentsList = request.ProcessComponents.Count == 0
            ? "(bu kazanım için kayıtlı süreç bileşeni yok — yalnızca kazanım açıklamasına göre değerlendir)"
            : string.Join("\n", request.ProcessComponents.Select(c => $"- {c}"));

        return $"""
            Sen bir Curriculum Validator'sın — SADECE sorunun resmi kazanıma ve süreç bileşenlerine
            uyumunu denetlersin (matematiksel doğruluk veya dil kalitesi SENİN İŞİN DEĞİL, ayrı bir
            hakem onları kontrol ediyor).

            KAZANIM KODU: {request.LearningOutcomeCode}
            KAZANIM AÇIKLAMASI: {request.LearningOutcomeDescription}

            SÜREÇ BİLEŞENLERİ (sorunun en az birini ÖLÇMESİ gerekir, sadece BAHSETMESİ yetmez):
            {componentsList}

            KURALLAR:
            1. "Maarif'e uygun görünüyor" gibi yüzeysel bir izlenim YETERSİZ — soru gerçekten bu
               süreç bileşenini ölçen bir muhakeme/işlem gerektirmiyorsa measures_process_component=false.
            2. Skorları yalnızca kazanım açıklamasına ve süreç bileşenlerine göre ver, genel soru
               kalitesine göre DEĞİL.
            3. Cevabını YALNIZCA submit_curriculum_alignment aracını çağırarak ver.
            """;
    }

    private static Tool BuildRecommendRevisionTool() => new()
    {
        Name = RecommendRevisionToolName,
        Description = "Sorunun Maarif uyumunu artırmak için somut, aksiyona dönük düzeltme önerisini bildir.",
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["revision_suggestion"] = Schema("string",
                    "Editörün doğrudan uygulayabileceği somut düzeltme önerisi (nedenini değil, nasıl " +
                    "düzeltileceğini anlat). 2-5 cümle.")
            },
            Required = ["revision_suggestion"]
        }
    };

    private static string BuildRecommendRevisionSystemPrompt(RecommendRevisionRequest request)
    {
        var issuesList = request.Issues.Count == 0
            ? "(kayıtlı bir kriter sorunu yok — genel olarak düşük puan)"
            : string.Join("\n", request.Issues.Select(i => $"- {i}"));

        return $"""
            Sen Türkiye Yüzyılı Maarif Modeli'ne göre matematik sorularına düzeltme önerisi veren
            bir editör danışmanısın. Bu soru Maarif Uyum Puanı {request.MaarifAlignmentScore}/100 ile
            incelemeye düşmüş; ne olduğunu tekrar etme, DOĞRUDAN nasıl düzeltileceğini öner.

            TESPİT EDİLEN SORUNLAR:
            {issuesList}

            KURALLAR:
            1. Somut ve uygulanabilir ol — "kazanıma uygun değil" değil, "şu şekilde değiştirilirse
               kazanıma uyar" de.
            2. Yalnızca aşağıdaki [KAYNAK n] bloklarına dayanarak kazanım/olgu iddiası üret.
            3. Cevabını YALNIZCA submit_revision_recommendation aracını çağırarak ver.

            {BuildGroundingBlock(request.Grounding)}
            """;
    }

    /// <summary>§9 maliyet ilkesi / prompt caching: CacheControl her çağrıda açık (bkz. yukarıdaki
    /// MessageCreateParams kurulumları), bu yüzden gerçek maliyet artık InputTokens tek başına
    /// yeterli değil — cache_creation (ilk çağrı, taban fiyatın ~1.25 katı) ve cache_read (tekrar
    /// eden aynı sistem promptu/araç şeması, ~0.1 katı — asıl tasarruf) AYRI sayaçlar.</summary>
    /// <summary>§16 zorluk bazlı model yönlendirme: çağıran taraf GenerateQuestionRequest/
    /// EvaluateQuestionRequest/ValidateCurriculumAlignmentRequest.ModelOverride ile options.Model'i
    /// geçersiz kılabildiği için gerçekte kullanılan model artık options.Model'den FARKLI olabilir —
    /// maliyet/kayıt için çözülmüş modeli açıkça geçmek gerekir.</summary>
    private AiUsage BuildUsage(Message response, Stopwatch stopwatch, AnthropicOptions options, string? modelOverride = null)
    {
        var model = modelOverride ?? options.Model;
        var inputTokens = (int)response.Usage.InputTokens;
        var outputTokens = (int)response.Usage.OutputTokens;
        var cacheCreationTokens = (int)(response.Usage.CacheCreationInputTokens ?? 0);
        var cacheReadTokens = (int)(response.Usage.CacheReadInputTokens ?? 0);
        return new AiUsage(
            Name, model, inputTokens, outputTokens,
            AnthropicPricing.EstimateCostUsd(model, inputTokens, outputTokens, cacheCreationTokens, cacheReadTokens),
            (int)stopwatch.ElapsedMilliseconds,
            cacheCreationTokens,
            cacheReadTokens);
    }

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
            // §43/Faz 1 Question DNA alanları — bu sorunun YAPISINI (nasıl düşündürdüğünü) sınıflandırır,
            // metnini değil. QuestionDna'da zaten var olan ama hiç doldurulmayan kolonlara karşılık gelir.
            ["question_type"] = Schema("string",
                "Sorunun biçimi (örn. \"Çoktan Seçmeli\", \"Açık Uçlu\", \"Kısa Cevaplı\", \"Tablo Yorumlama\", " +
                "\"Grafik Yorumlama\", \"Görsel Yorumlama\", \"Senaryo Temelli\", \"Problem Temelli\", \"Eşleştirme\", " +
                "\"Doğru/Yanlış + Gerekçe\"). Şıklar verildiyse genelde \"Çoktan Seçmeli\"dir, ama tablo/grafik/görsel " +
                "içeren şıklı sorularda o türü tercih et."),
            ["context_type"] = Schema("string",
                "Sorunun bağlam/senaryo türü (örn. \"Günlük Yaşam\", \"Bilim ve Teknoloji\", \"Ekonomi/Finans\", " +
                "\"Spor\", \"Veri/İstatistik\", \"Bağlamsız/Saf Matematik\"). Bağlam yoksa \"Bağlamsız/Saf Matematik\"."),
            ["representation_types"] = JsonSerializer.SerializeToElement(new
            {
                type = "array",
                description = "Sorunun kullandığı temsil biçimleri (örn. \"Grafik\", \"Tablo\", \"Denklem\", " +
                    "\"Şekil/Diyagram\", \"Sözel\", \"Sayı Doğrusu\"). Birden fazla olabilir.",
                items = new { type = "string" }
            }),
            ["cognitive_level"] = JsonSerializer.SerializeToElement(new
            {
                type = "string",
                description = "Bloom taksonomisine göre gerekli bilişsel düzey.",
                @enum = new[] { "Hatırlama", "Anlama", "Uygulama", "Analiz", "Değerlendirme", "Yaratma" }
            }),
            ["reasoning_types"] = JsonSerializer.SerializeToElement(new
            {
                type = "array",
                description = "Sorunun gerektirdiği muhakeme türleri (örn. \"Örüntü Tanıma\", \"Modelleme\", " +
                    "\"Karşılaştırma\", \"Genelleme\", \"Hipotez Kurma\", \"Çıkarım\", \"Sınıflandırma\"). Birden fazla olabilir.",
                items = new { type = "string" }
            }),
            ["expected_solution_steps"] = Schema("integer",
                "Çözüm için gereken ardışık mantıksal/işlemsel adım sayısı (ör. tek işlemse 1)."),
            ["ai_estimated_student_time_minutes"] = Schema("integer",
                "Bu sınıf seviyesindeki tipik bir öğrencinin bu soruyu çözmesi için tahmini süre (dakika)."),
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

    internal static JsonElement Schema(string type, string description) =>
        JsonSerializer.SerializeToElement(new { type, description });

    private static string BuildSystemPrompt(AnalyzeQuestionRequest request)
    {
        var criteriaList = string.Join("\n", MaarifRubric.Criteria.Select(c =>
            $"- {c.Key} (ağırlık {c.Weight}{(c.IsCriticalGate ? ", CRITICAL GATE" : "")})"));

        var grounding = request.Grounding.Count == 0
            ? "(Bu soru için RAG'de hiçbir referans bulunamadı. Kazanım/beceri alanlarını uydurma; " +
              "learning_outcome_code alanını döndürme ve manual_review_required=true işaretle.)"
            : BuildGroundingBlock(request.Grounding);

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
            5. question_type/context_type/representation_types/cognitive_level/reasoning_types/
               expected_solution_steps/ai_estimated_student_time_minutes alanları sorunun METNİNİ
               değil YAPISINI sınıflandırır — "bu soru öğrenciyi nasıl düşündürüyor?" sorusuna cevap
               ver, metni yeniden ifade etme. Emin değilsen bu alanları boş bırak (tahmin etme).
            6. Cevabını YALNIZCA submit_analysis aracını çağırarak ver.

            RUBRİK KRİTERLERİ (bkz. §E):
            {criteriaList}

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

        static List<string>? GetStringArrayOrNull(IReadOnlyDictionary<string, JsonElement> input, string key) =>
            input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : null;

        int? GetOptionalInt(string key) =>
            input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

        return new AnalyzeQuestionResult(
            MathematicalCore: GetString("mathematical_core") ?? string.Empty,
            LearningOutcomeCode: GetString("learning_outcome_code"),
            FieldSkill: GetString("field_skill") ?? string.Empty,
            ConceptualSkill: GetString("conceptual_skill") ?? string.Empty,
            ContextIsDecorative: GetBool("context_is_decorative"),
            CriterionEvaluations: evaluations,
            ManualReviewRequired: GetBool("manual_review_required"),
            ManualReviewReason: GetString("manual_review_reason"),
            Usage: usage,
            QuestionType: GetString("question_type"),
            ContextType: GetString("context_type"),
            RepresentationTypes: GetStringArrayOrNull(input, "representation_types"),
            CognitiveLevel: GetString("cognitive_level"),
            ReasoningTypes: GetStringArrayOrNull(input, "reasoning_types"),
            ExpectedSolutionSteps: GetOptionalInt("expected_solution_steps"),
            AiEstimatedStudentTimeMinutes: GetOptionalInt("ai_estimated_student_time_minutes"));
    }

    private static Tool BuildTransformationTool()
    {
        var properties = new Dictionary<string, JsonElement>
        {
            ["new_question"] = Schema("string", "Dönüştürülmüş soru metni."),
            ["new_options"] = JsonSerializer.SerializeToElement(new
            {
                type = "array",
                description = "3-6 şık.",
                items = new { type = "string" },
                minItems = 3,
                maxItems = 6
            }),
            ["correct_answer"] = Schema("string", "Doğru şıkkın metni (new_options içindeki değerlerden biri)."),
            ["solution"] = Schema("string", "Adım adım çözüm."),
            ["distractors"] = BuildDistractorsSchema()
        };

        return new Tool
        {
            Name = TransformToolName,
            Description = "Dönüştürülmüş sorunun yapılandırılmış sonucunu bildir.",
            InputSchema = new()
            {
                Properties = properties,
                Required = ["new_question", "new_options", "correct_answer", "solution", "distractors"]
            }
        };
    }

    private static string BuildTransformSystemPrompt(TransformQuestionRequest request)
    {
        var modeInstruction = request.TransformationMode switch
        {
            nameof(TransformDecision.Conservative) =>
                "CONSERVATIVE mod: yalnızca dil/ifade düzeltmeleri yap. Sayıları, bağlamı ve yapıyı DEĞİŞTİRME.",
            nameof(TransformDecision.Transform) =>
                "TRANSFORM mod: bağlamı ve sayıları yeniden kurgula, ama aynı kazanım/matematiksel özü koru.",
            nameof(TransformDecision.Redesign) =>
                "REDESIGN mod: tamamen yeni bir senaryo yaz; yalnızca aynı matematiksel öz ve kazanım korunmalı.",
            _ => "Sorunun kazanım uyumunu artıracak şekilde dönüştür."
        };

        return $"""
            Sen Türkiye Yüzyılı Maarif Modeli'ne göre matematik sorularını dönüştüren bir uzmansın.

            MOD: {modeInstruction}

            KURALLAR:
            1. Matematiksel öz ({request.Analysis.MathematicalCore}) ve kazanım
               ({request.Analysis.LearningOutcomeCode ?? "belirtilmemiş"}) korunmalı.
            2. Yalnızca aşağıdaki [KAYNAK n] bloklarına dayanarak bağlam/kazanım iddiası üret.
            3. Her yanlış şık için bir çeldirici kaydı ver; mümkünse ilişkili bir öğrenci hata
               tipini (misconception_code) belirt, emin değilsen boş bırak — uydurma.
            4. Cevabını YALNIZCA submit_transformation aracını çağırarak ver.

            {BuildGroundingBlock(request.Grounding)}
            """;
    }

    private static Tool BuildEvaluationTool()
    {
        var properties = new Dictionary<string, JsonElement>
        {
            ["quality_score"] = Schema("integer", "0-100 arası nihai kalite puanı."),
            ["passed"] = Schema("boolean", "critical_failures doluysa MUTLAKA false."),
            ["critical_failures"] = JsonSerializer.SerializeToElement(new
            {
                type = "array",
                description = "Yayına engel ciddi hatalar (matematiksel yanlışlık, desteklenmeyen iddia, vb).",
                items = new { type = "string" }
            }),
            ["quality_flags"] = JsonSerializer.SerializeToElement(new
            {
                type = "array",
                description = "Engel olmayan ama editöre bildirilmesi gereken küçük gözlemler.",
                items = new { type = "string" }
            })
        };

        return new Tool
        {
            Name = EvaluateToolName,
            Description = "Dönüştürülmüş sorunun kalite değerlendirmesini bildir.",
            InputSchema = new()
            {
                Properties = properties,
                Required = ["quality_score", "passed", "critical_failures", "quality_flags"]
            }
        };
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

    internal static JsonElement BuildDistractorsSchema() => JsonSerializer.SerializeToElement(new
    {
        type = "array",
        description = "Doğru şık HARİÇ her şık için bir çeldirici kaydı (bkz. §15).",
        items = new
        {
            type = "object",
            properties = new
            {
                option_label = new { type = "string", description = "Örn. B, C, D." },
                misconception_code = new { type = "string", description = "Bu çeldiricinin işaret ettiği öğrenci hata tipi; emin değilsen döndürme." },
                explanation = new { type = "string" }
            },
            required = new[] { "option_label" }
        }
    });

    private static Tool BuildGenerationTool(int? requiredOptionCount = null)
    {
        var properties = new Dictionary<string, JsonElement>
        {
            ["question"] = Schema("string", "Üretilen soru metni."),
            ["options"] = JsonSerializer.SerializeToElement(requiredOptionCount is int n
                ? new
                {
                    type = "array",
                    description = $"Tam olarak {n} şık (bundan az ya da çok ASLA).",
                    items = new { type = "string" },
                    minItems = n,
                    maxItems = n
                }
                : new
                {
                    type = "array",
                    description = "3-6 şık.",
                    items = new { type = "string" },
                    minItems = 3,
                    maxItems = 6
                }),
            ["correct_answer"] = Schema("string", "Doğru şıkkın metni (options içindeki değerlerden biri)."),
            ["solution"] = Schema("string", "Adım adım çözüm."),
            ["distractors"] = BuildDistractorsSchema(),
            ["visual_required"] = Schema("boolean",
                "Bu soru için gerçek bir görsel (grafik/koordinat sistemi/geometrik şekil/tablo) " +
                "üretilmeli mi? Görsel yalnızca çözümün ANLAMLI bir parçasıysa true — dekoratif " +
                "amaçla ASLA true verme."),
            ["visual_spec"] = BuildVisualSpecSchema()
        };

        return new Tool
        {
            Name = GenerateToolName,
            Description = "Üretilen sorunun yapılandırılmış sonucunu bildir.",
            InputSchema = new()
            {
                Properties = properties,
                Required = ["question", "options", "correct_answer", "solution", "distractors", "visual_required"]
            }
        };
    }

    /// <summary>§6 — LLM burada gerçek bir görsel ÜRETMEZ, yalnızca ne istediğinin yapılandırılmış
    /// TARİFİNİ verir; gerçek SVG'yi VisualSpecRenderer (Application/Visuals) bu tarife bakarak
    /// deterministik olarak üretir. visual_required=false ise bu alan tamamen yok sayılır.</summary>
    internal static JsonElement BuildVisualSpecSchema()
    {
        var pointSchema = new
        {
            type = "object",
            properties = new
            {
                x = new { type = "number" },
                y = new { type = "number" },
                label = new { type = "string" }
            },
            required = new[] { "x", "y" }
        };

        var functionSchema = new
        {
            type = "object",
            properties = new
            {
                expression = new { type = "string", description = "Örn. \"2*x-3\", \"x^2/4\", \"sqrt(x)\". Yalnızca x değişkeni ve + - * / ^ sin cos tan sqrt abs log ln exp pi e." },
                label = new { type = "string", description = "Örn. \"f(x)=2x-3\"." }
            },
            required = new[] { "expression" }
        };

        var segmentSchema = new
        {
            type = "object",
            properties = new
            {
                from = new { type = "string", description = "points içindeki bir noktanın label'ı." },
                to = new { type = "string" },
                x1 = new { type = "number" },
                y1 = new { type = "number" },
                x2 = new { type = "number" },
                y2 = new { type = "number" },
                label = new { type = "string" }
            }
        };

        var vectorSchema = new
        {
            type = "object",
            properties = new
            {
                x1 = new { type = "number" }, y1 = new { type = "number" },
                x2 = new { type = "number" }, y2 = new { type = "number" },
                label = new { type = "string" }
            },
            required = new[] { "x1", "y1", "x2", "y2" }
        };

        var sideLabelSchema = new
        {
            type = "object",
            properties = new
            {
                from = new { type = "string", description = "vertices içindeki bir köşenin label'ı." },
                to = new { type = "string" },
                label = new { type = "string", description = "Örn. \"4 cm\"." }
            },
            required = new[] { "from", "to", "label" }
        };

        var angleLabelSchema = new
        {
            type = "object",
            properties = new
            {
                vertex = new { type = "string" },
                label = new { type = "string", description = "Örn. \"90°\"." }
            },
            required = new[] { "vertex", "label" }
        };

        var diagramNodeSchema = new
        {
            type = "object",
            properties = new
            {
                id = new { type = "string", description = "diagram_edges'in from/to ile referans vereceği benzersiz kimlik." },
                label = new { type = "string" },
                x = new { type = "number", description = "Vermezsen otomatik ızgaraya yerleştirilir — çoğu durumda vermene gerek yok." },
                y = new { type = "number" }
            },
            required = new[] { "id", "label" }
        };

        var diagramEdgeSchema = new
        {
            type = "object",
            properties = new
            {
                from = new { type = "string", description = "diagram_nodes içindeki bir düğümün id'si." },
                to = new { type = "string" },
                label = new { type = "string" },
                directed = new { type = "boolean", description = "Varsayılan true (ok ucu)." }
            },
            required = new[] { "from", "to" }
        };

        var iconGroupSchema = new
        {
            type = "object",
            properties = new
            {
                icon = new { type = "string", @enum = new[] { "circle", "square", "triangle", "star" } },
                count = new { type = "integer", description = "1-20 arası mantıklı; daha büyük sayılarda ikon tekrar etmez, '× N' etiketiyle gösterilir." },
                label = new { type = "string", description = "Örn. \"Kırmızı toplar\"." },
                color = new { type = "string", description = "Örn. \"#1a56db\" — vermezsen varsayılan renk kullanılır." }
            },
            required = new[] { "icon", "count" }
        };

        return JsonSerializer.SerializeToElement(new
        {
            type = "object",
            description = "visual_required=true ise DOLDUR. type alanı, istenen görsel türüyle EŞLEŞMELİ.",
            properties = new Dictionary<string, object>
            {
                ["type"] = new { type = "string", @enum = new[] { "function_graph", "coordinate_system", "geometric_shape", "table", "diagram", "infographic", "visual_scenario", "mixed_visual" } },
                ["x_min"] = new { type = "number" },
                ["x_max"] = new { type = "number" },
                ["y_min"] = new { type = "number" },
                ["y_max"] = new { type = "number" },
                ["x_label"] = new { type = "string" },
                ["y_label"] = new { type = "string" },
                ["functions"] = new { type = "array", description = "Yalnızca type=function_graph.", items = functionSchema },
                ["points"] = new { type = "array", description = "function_graph/coordinate_system için işaretlenecek noktalar.", items = pointSchema },
                ["vertices"] = new { type = "array", description = "Yalnızca type=geometric_shape (triangle/rectangle/polygon).", items = pointSchema },
                ["segments"] = new { type = "array", description = "Yalnızca type=coordinate_system.", items = segmentSchema },
                ["vectors"] = new { type = "array", description = "Yalnızca type=coordinate_system.", items = vectorSchema },
                ["shape"] = new { type = "string", description = "Yalnızca type=geometric_shape: triangle/rectangle/polygon/circle.", @enum = new[] { "triangle", "rectangle", "polygon", "circle" } },
                ["circle"] = new
                {
                    type = "object",
                    description = "Yalnızca shape=circle.",
                    properties = new { center_x = new { type = "number" }, center_y = new { type = "number" }, radius = new { type = "number" } },
                    required = new[] { "center_x", "center_y", "radius" }
                },
                ["side_labels"] = new { type = "array", description = "Yalnızca type=geometric_shape.", items = sideLabelSchema },
                ["angle_labels"] = new { type = "array", description = "Yalnızca type=geometric_shape.", items = angleLabelSchema },
                ["headers"] = new { type = "array", description = "type=table: sütun başlıkları. type=infographic: kategori adları.", items = new { type = "string" } },
                ["rows"] = new { type = "array", description = "type=table: her satır headers ile aynı uzunlukta. type=infographic: TEK satır, headers ile aynı uzunlukta SAYISAL değerler.", items = new { type = "array", items = new { type = "string" } } },
                ["diagram_nodes"] = new { type = "array", description = "Yalnızca type=diagram.", items = diagramNodeSchema },
                ["diagram_edges"] = new { type = "array", description = "Yalnızca type=diagram, isteğe bağlı.", items = diagramEdgeSchema },
                ["chart_kind"] = new { type = "string", description = "Yalnızca type=infographic.", @enum = new[] { "bar", "pie" } },
                ["icon_groups"] = new { type = "array", description = "Yalnızca type=visual_scenario.", items = iconGroupSchema }
            },
            required = new[] { "type" }
        });
    }

    internal static string BuildGenerateSystemPrompt(GenerateQuestionRequest request)
    {
        var skillsLine = request.SkillCodes is { Count: > 0 }
            ? $"\n        - Alan becerisi: {string.Join(", ", request.SkillCodes)}"
            : "";
        var frameworksLine = request.ContentFrameworks is { Count: > 0 }
            ? $"\n        - İçerik çerçevesi: {string.Join(", ", request.ContentFrameworks)}"
            : "";
        var componentsLine = request.ProcessComponents is { Count: > 0 }
            ? $"\n\n        SÜREÇ BİLEŞENLERİ (soru bunlardan en az birini gerçekten ÖLÇMELİ):\n        " +
              string.Join("\n        ", request.ProcessComponents.Select(c => $"- {c}"))
            : "";
        // §58/§59 Faz 6 — archetype hint ZATEN SOYUTLANMIŞ bir özet (bkz. GenerateQuestionRequest
        // doc), ham bir kaynak soru DEĞİL; bu yüzden "kopyala" değil "bu TARZI kullan ama özgün
        // üret" talimatı verilir. §52 Orijinallik Kontrolü (Faz 0) bu talimata rağmen üretilen
        // sorunun yine de kaynak/havuza çok benzemesi durumunu zaten AYRICA (üretim sonrası) yakalar
        // — bu blok yalnızca ilk denemenin isabetini artırmak içindir, tek güvence değildir.
        var archetypeBlock = string.IsNullOrWhiteSpace(request.ArchetypeHint)
            ? ""
            : $"""


            ÖNERİLEN MUHAKEME TARZI (bir referans, KOPYALANACAK bir metin DEĞİL):
            {request.ArchetypeHint}
            Bu, daha önce kaliteli bulunmuş sorularda gözlemlenen SOYUT bir muhakeme kalıbıdır.
            Mümkünse BENZER BİR MUHAKEME ZİNCİRİ kullan, ama tamamen YENİ bir bağlam/senaryo/sayılarla,
            tamamen ÖZGÜN bir soru üret. Bu tarz sorunun gereksinimleriyle (kazanım/zorluk/tip)
            UYUŞMUYORSA bu öneriyi YOK SAY — curriculum uyumu her zaman önceliklidir.
            """;
        var visualInstruction = request.VisualUsage switch
        {
            GenerationVisualUsage.None =>
                "\n        - Görsel KULLANMA: visual_required=false. visual_spec döndürme.",
            GenerationVisualUsage.Auto =>
                "\n        - Görsel kullanımı: OTOMATİK. Yalnızca görsel, çözümün ANLAMLI bir " +
                "parçasıysa (dekoratif değilse) visual_required=true yap ve uygun bir visual_spec " +
                "üret; aksi halde visual_required=false bırak.",
            GenerationVisualUsage.FunctionGraph =>
                "\n        - Görsel ZORUNLU: visual_required=true, visual_spec.type=\"function_graph\" olmalı.",
            GenerationVisualUsage.CoordinateSystem =>
                "\n        - Görsel ZORUNLU: visual_required=true, visual_spec.type=\"coordinate_system\" olmalı.",
            GenerationVisualUsage.GeometricShape =>
                "\n        - Görsel ZORUNLU: visual_required=true, visual_spec.type=\"geometric_shape\" olmalı.",
            GenerationVisualUsage.Table =>
                "\n        - Görsel ZORUNLU: visual_required=true, visual_spec.type=\"table\" olmalı.",
            GenerationVisualUsage.Diagram =>
                "\n        - Görsel ZORUNLU: visual_required=true, visual_spec.type=\"diagram\" olmalı " +
                "(diagram_nodes + isteğe bağlı diagram_edges — akış şeması/kavram haritası/hiyerarşi).",
            GenerationVisualUsage.Infographic =>
                "\n        - Görsel ZORUNLU: visual_required=true, visual_spec.type=\"infographic\" olmalı " +
                "(headers=kategoriler, rows[0]=sayısal değerler, chart_kind=\"bar\" veya \"pie\").",
            GenerationVisualUsage.VisualScenario =>
                "\n        - Görsel ZORUNLU: visual_required=true, visual_spec.type=\"visual_scenario\" olmalı " +
                "(icon_groups — sayma/kombinatorik/olasılık senaryosundaki nesneleri basit ikonlarla temsil et).",
            GenerationVisualUsage.MixedVisual =>
                "\n        - Görsel ZORUNLU: visual_required=true, visual_spec.type=\"mixed_visual\" olmalı " +
                "— HEM bir birincil görsel (functions VEYA shape+vertices VEYA points/segments/vectors) HEM " +
                "de bir tablo (headers+rows) BİRLİKTE doldurulmalı, yalnızca biri yeterli değildir.",
            _ => "\n        - Görsel KULLANMA: visual_required=false."
        };

        // §9 maliyet ilkesi: önceki deneme reddedildiyse gerekçeyi kör bir tekrar yerine somut
        // düzeltme talimatı olarak ver — aksi halde aynı hata büyük olasılıkla tekrarlanır ve
        // Generation+CurriculumValidation çağrıları boşa (0 sonuçla) harcanmış olur.
        // "Çoktan Seçmeli" dışındaki bazı tipler (Tablo/Grafik/Görsel Yorumlama, Senaryo/Problem
        // Temelli) de LLM tarafından şıklı üretilebilir — bu yüzden talimat tipin KENDİSİNE değil,
        // "şıklı üretirsen" koşuluna bağlanır (asıl GARANTİ GenerationOrchestrationService'teki
        // Options.Count>0 kontrolüdür, bu yalnızca ilk denemede isabeti artırır).
        var requiredCountForGrade = MultipleChoiceOptionPolicy.RequiredOptionCount(request.Grade);
        var optionCountLine = MultipleChoiceOptionPolicy.IsMultipleChoice(request.QuestionType)
            ? $"\n        - Şık adedi: TAM OLARAK {requiredCountForGrade} şık (Sınıf {request.Grade} için sabit kural, bundan az/çok ASLA)."
            : $"\n        - Bu soru şıklı (çoktan seçmeli biçimli) üretilecekse şık adedi TAM OLARAK " +
              $"{requiredCountForGrade} olmalı (Sınıf {request.Grade} için sabit kural); açık uçlu/eşleştirme " +
              "gibi gerçekten şıksız bir biçimse options alanını boş bırak.";

        var previousAttemptBlock = string.IsNullOrWhiteSpace(request.PreviousAttemptFeedback)
            ? ""
            : $"""


            ÖNCEKİ DENEME REDDEDİLDİ — NEDENİ:
            {request.PreviousAttemptFeedback}

            Bu sefer YUKARIDAKİ SORUNU somut olarak çöz; aynı hatayı tekrarlama. Özellikle
            süreç bileşeninin/muhakeme gerekliliğinin sorunun ÇÖZÜMÜNDE gerçekten kullanılmasını
            sağla (yalnızca yüzeysel bir bağlam değil).
            """;

        return $"""
        Sen Türkiye Yüzyılı Maarif Modeli'ne göre sıfırdan matematik sorusu üreten bir uzmansın.{previousAttemptBlock}

        HEDEF:
        - Sınıf: {request.Grade}, Ders: {request.Subject}
        - Tema: {request.Theme}
        - Kazanım kodu: {request.LearningOutcomeCode}
        - Kazanım açıklaması (soru MUTLAKA bunu ölçmeli, sadece temayı değil): {request.LearningOutcomeDescription}
        - Zorluk: {request.Difficulty}
        - Soru tipi: {request.QuestionType}
        - Muhakeme tipi: {request.ReasoningType}{optionCountLine}{skillsLine}{frameworksLine}{visualInstruction}{componentsLine}{archetypeBlock}

        KURALLAR:
        1. Yalnızca aşağıdaki [KAYNAK n] bloklarına dayanarak kazanım/olgu iddiası üret.
           Kaynakta olmayan bir MEB kazanımını ASLA uydurma.
        2. Sorunun matematiksel olarak doğru ve tek bir doğru cevabı olmasına dikkat et.
        3. Her yanlış şık için bir çeldirici kaydı ver; mümkünse bir öğrenci hata tipini
           (misconception_code) belirt, emin değilsen boş bırak — uydurma.
        4. visual_spec bir görsel DOSYASI değil, yapılandırılmış bir TARİFTİR — gerçek görsel
           ayrı bir motor tarafından bu tarife göre üretilecek. function_graph için expression
           alanına yalnızca x değişkeni ve + - * / ^ sin cos tan sqrt abs log ln exp pi e kullan
           (başka hiçbir sözdizimi render edilemez). "[GRAFİK BURADA]" gibi bir metin/placeholder
           ASLA üretme — ya visual_required=true ile gerçek bir visual_spec ver ya da
           visual_required=false bırak.
        5. Cevabını YALNIZCA submit_generation aracını çağırarak ver.

        {BuildGroundingBlock(request.Grounding)}
        """;
    }

    internal static string BuildGenerateUserContent(GenerateQuestionRequest request) => $"""
        BAĞLAM/SENARYO İSTEĞİ:
        {request.Context}
        """;

    internal static GenerateQuestionResult ParseGenerateResult(IReadOnlyDictionary<string, JsonElement> input, AiUsage usage)
    {
        var options = input.TryGetValue("options", out var optionsEl) && optionsEl.ValueKind == JsonValueKind.Array
            ? optionsEl.EnumerateArray().Select(o => o.GetString() ?? "").ToList()
            : new List<string>();

        var distractors = new List<DistractorDto>();
        if (input.TryGetValue("distractors", out var distractorsEl) && distractorsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in distractorsEl.EnumerateArray())
            {
                var optionLabel = item.TryGetProperty("option_label", out var ol) ? ol.GetString() ?? "" : "";
                var misconceptionCode = item.TryGetProperty("misconception_code", out var mc) && mc.ValueKind == JsonValueKind.String
                    ? mc.GetString() : null;
                var explanation = item.TryGetProperty("explanation", out var ex) && ex.ValueKind == JsonValueKind.String
                    ? ex.GetString() : null;
                distractors.Add(new DistractorDto(optionLabel, misconceptionCode, explanation));
            }
        }

        var visualRequired = input.TryGetValue("visual_required", out var vr)
            && vr.ValueKind is JsonValueKind.True or JsonValueKind.False && vr.GetBoolean();
        var visualSpec = visualRequired && input.TryGetValue("visual_spec", out var vs) && vs.ValueKind == JsonValueKind.Object
            ? ParseVisualSpec(vs)
            : null;

        return new GenerateQuestionResult(
            Question: input.TryGetValue("question", out var q) ? q.GetString() ?? "" : "",
            Options: options,
            CorrectAnswer: input.TryGetValue("correct_answer", out var ca) ? ca.GetString() ?? "" : "",
            Solution: input.TryGetValue("solution", out var sol) ? sol.GetString() ?? "" : "",
            Distractors: distractors,
            Usage: usage,
            VisualRequired: visualRequired,
            VisualSpec: visualSpec);
    }

    /// <summary>Soru Çeşitlendir sistem promptu — GenerateQuestionAsync'in aksine Grade/Subject/
    /// LearningOutcome/Grounding YOK ve prompt açıkça müfredat kazanım/beceri İDDİASI ÜRETME diye
    /// uyarır (bkz. ILLMProvider.VaryQuestionAsync'in doc'u — bu akış curriculum doğrulamasından
    /// muaftır, ayrı bir havuzda saklanır).</summary>
    internal static string BuildVaryQuestionSystemPrompt(VaryQuestionRequest request) => $"""
        Sen verilen bir örnek sorudan, KÜÇÜK ve MANTIKLI değişikliklerle (sayıları değiştirme,
        isim/bağlam/senaryo değiştirme vb.) yapısal olarak AYNI kalan varyasyonlar üreten bir
        asistansın.

        KURALLAR:
        1. Tam olarak {request.Count} adet varyasyon üret.
        2. Her varyasyon, kaynak sorunun soru tipini (çoktan seçmeli/açık uçlu/vb.), zorluk
           seviyesini ve çözüm yöntemini KORUMALI — yalnızca sayısal değerler, isimler,
           bağlam/senaryo gibi yüzeysel unsurlar değişsin.
        3. Kaynak soru çoktan seçmeliyse (şıkları varsa), her varyasyon AYNI SAYIDA şıkka sahip
           olmalı ve doğru cevap + çeldiriciler yeni sayılarla mantıksal olarak tutarlı olmalı.
           Kaynak soru açık uçluysa options alanını boş dizi bırak.
        4. ÖNEMLİ: Bu modda Türkiye Yüzyılı Maarif Modeli müfredat kazanım/beceri doğrulaması
           ARANMAZ — gerçek bir müfredat kodu veya kazanım iddiası ÜRETME, yalnızca kaynak
           sorunun kendi yapısına ve mantığına sadık kal.
        5. Her varyasyon için adım adım çözüm yaz.
        6. Varyasyonlar birbirinden farklı olmalı (aynı sayıları/isimleri tekrarlama).
        7. Cevabını YALNIZCA submit_question_variations aracını çağırarak ver.

        KAYNAK SORU:
        {request.SourceQuestionText}
        """;

    internal static Tool BuildVaryQuestionTool()
    {
        var variationSchema = new
        {
            type = "object",
            properties = new
            {
                question = new { type = "string", description = "Varyasyon soru metni." },
                options = new
                {
                    type = "array",
                    description = "Kaynak soru çoktan seçmeliyse aynı şık sayısıyla; açık uçluysa boş dizi.",
                    items = new { type = "string" }
                },
                correct_answer = new { type = "string", description = "Doğru cevap — options doluysa onlardan biri, açık uçluysa doğrudan cevap metni." },
                solution = new { type = "string", description = "Adım adım çözüm." }
            },
            required = new[] { "question", "options", "correct_answer", "solution" }
        };

        return new Tool
        {
            Name = VaryQuestionToolName,
            Description = "Üretilen soru varyasyonlarının listesini bildir.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["variations"] = JsonSerializer.SerializeToElement(new
                    {
                        type = "array",
                        description = "Her biri kaynak sorunun küçük değişikliklerle çoğaltılmış bir hali.",
                        items = variationSchema
                    })
                },
                Required = ["variations"]
            }
        };
    }

    internal static VaryQuestionResult ParseVaryQuestionResult(IReadOnlyDictionary<string, JsonElement> input, AiUsage usage)
    {
        var variations = new List<QuestionVariantDto>();
        if (input.TryGetValue("variations", out var variationsEl) && variationsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in variationsEl.EnumerateArray())
            {
                var options = item.TryGetProperty("options", out var optionsEl) && optionsEl.ValueKind == JsonValueKind.Array
                    ? optionsEl.EnumerateArray().Select(o => o.GetString() ?? "").ToList()
                    : new List<string>();

                variations.Add(new QuestionVariantDto(
                    Question: item.TryGetProperty("question", out var q) ? q.GetString() ?? "" : "",
                    Options: options,
                    CorrectAnswer: item.TryGetProperty("correct_answer", out var ca) ? ca.GetString() ?? "" : "",
                    Solution: item.TryGetProperty("solution", out var sol) ? sol.GetString() ?? "" : ""));
            }
        }

        return new VaryQuestionResult(variations, usage);
    }

    internal static VisualSpec ParseVisualSpec(JsonElement el)
    {
        static double? GetDouble(JsonElement e, string key) =>
            e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        static string? GetString(JsonElement e, string key) =>
            e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static List<string>? GetStringList(JsonElement e, string key) =>
            e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                : null;

        var type = GetString(el, "type") ?? throw new FormatException("visual_spec.type eksik.");

        PlotPoint ParsePoint(JsonElement p) => new(
            GetDouble(p, "x") ?? throw new FormatException("Nokta x eksik."),
            GetDouble(p, "y") ?? throw new FormatException("Nokta y eksik."),
            GetString(p, "label"));

        List<PlotPoint>? points = el.TryGetProperty("points", out var pointsEl) && pointsEl.ValueKind == JsonValueKind.Array
            ? pointsEl.EnumerateArray().Select(ParsePoint).ToList() : null;
        List<PlotPoint>? vertices = el.TryGetProperty("vertices", out var verticesEl) && verticesEl.ValueKind == JsonValueKind.Array
            ? verticesEl.EnumerateArray().Select(ParsePoint).ToList() : null;

        List<PlotFunction>? functions = el.TryGetProperty("functions", out var fnEl) && fnEl.ValueKind == JsonValueKind.Array
            ? fnEl.EnumerateArray()
                .Select(f => new PlotFunction(GetString(f, "expression") ?? throw new FormatException("function.expression eksik."), GetString(f, "label")))
                .ToList()
            : null;

        List<PlotSegment>? segments = el.TryGetProperty("segments", out var segEl) && segEl.ValueKind == JsonValueKind.Array
            ? segEl.EnumerateArray()
                .Select(s => new PlotSegment(GetString(s, "from"), GetString(s, "to"), GetDouble(s, "x1"), GetDouble(s, "y1"), GetDouble(s, "x2"), GetDouble(s, "y2"), GetString(s, "label")))
                .ToList()
            : null;

        List<PlotVector>? vectors = el.TryGetProperty("vectors", out var vecEl) && vecEl.ValueKind == JsonValueKind.Array
            ? vecEl.EnumerateArray()
                .Select(v => new PlotVector(
                    GetDouble(v, "x1") ?? throw new FormatException("vector.x1 eksik."), GetDouble(v, "y1") ?? throw new FormatException("vector.y1 eksik."),
                    GetDouble(v, "x2") ?? throw new FormatException("vector.x2 eksik."), GetDouble(v, "y2") ?? throw new FormatException("vector.y2 eksik."),
                    GetString(v, "label")))
                .ToList()
            : null;

        PlotCircle? circle = el.TryGetProperty("circle", out var circleEl) && circleEl.ValueKind == JsonValueKind.Object
            ? new PlotCircle(
                GetDouble(circleEl, "center_x") ?? throw new FormatException("circle.center_x eksik."),
                GetDouble(circleEl, "center_y") ?? throw new FormatException("circle.center_y eksik."),
                GetDouble(circleEl, "radius") ?? throw new FormatException("circle.radius eksik."))
            : null;

        List<PlotSideLabel>? sideLabels = el.TryGetProperty("side_labels", out var slEl) && slEl.ValueKind == JsonValueKind.Array
            ? slEl.EnumerateArray()
                .Select(s => new PlotSideLabel(
                    GetString(s, "from") ?? throw new FormatException("side_label.from eksik."),
                    GetString(s, "to") ?? throw new FormatException("side_label.to eksik."),
                    GetString(s, "label") ?? throw new FormatException("side_label.label eksik.")))
                .ToList()
            : null;

        List<PlotAngleLabel>? angleLabels = el.TryGetProperty("angle_labels", out var alEl) && alEl.ValueKind == JsonValueKind.Array
            ? alEl.EnumerateArray()
                .Select(a => new PlotAngleLabel(
                    GetString(a, "vertex") ?? throw new FormatException("angle_label.vertex eksik."),
                    GetString(a, "label") ?? throw new FormatException("angle_label.label eksik.")))
                .ToList()
            : null;

        List<IReadOnlyList<string>>? rows = el.TryGetProperty("rows", out var rowsEl) && rowsEl.ValueKind == JsonValueKind.Array
            ? rowsEl.EnumerateArray()
                .Select(r => (IReadOnlyList<string>)r.EnumerateArray().Select(c => c.GetString() ?? "").ToList())
                .ToList()
            : null;

        List<DiagramNode>? diagramNodes = el.TryGetProperty("diagram_nodes", out var dnEl) && dnEl.ValueKind == JsonValueKind.Array
            ? dnEl.EnumerateArray()
                .Select(n => new DiagramNode(
                    GetString(n, "id") ?? throw new FormatException("diagram_node.id eksik."),
                    GetString(n, "label") ?? throw new FormatException("diagram_node.label eksik."),
                    GetDouble(n, "x"), GetDouble(n, "y")))
                .ToList()
            : null;

        List<DiagramEdge>? diagramEdges = el.TryGetProperty("diagram_edges", out var deEl) && deEl.ValueKind == JsonValueKind.Array
            ? deEl.EnumerateArray()
                .Select(e => new DiagramEdge(
                    GetString(e, "from") ?? throw new FormatException("diagram_edge.from eksik."),
                    GetString(e, "to") ?? throw new FormatException("diagram_edge.to eksik."),
                    GetString(e, "label"),
                    !(e.TryGetProperty("directed", out var d) && d.ValueKind == JsonValueKind.False)))
                .ToList()
            : null;

        List<IconGroup>? iconGroups = el.TryGetProperty("icon_groups", out var igEl) && igEl.ValueKind == JsonValueKind.Array
            ? igEl.EnumerateArray()
                .Select(g => new IconGroup(
                    GetString(g, "icon") ?? throw new FormatException("icon_group.icon eksik."),
                    (int)(GetDouble(g, "count") ?? throw new FormatException("icon_group.count eksik.")),
                    GetString(g, "label"), GetString(g, "color")))
                .ToList()
            : null;

        return new VisualSpec(
            type,
            GetDouble(el, "x_min"), GetDouble(el, "x_max"), GetDouble(el, "y_min"), GetDouble(el, "y_max"),
            GetString(el, "x_label"), GetString(el, "y_label"),
            functions, points, vertices, segments, vectors,
            GetString(el, "shape"), circle, sideLabels, angleLabels,
            GetStringList(el, "headers"), rows,
            diagramNodes, diagramEdges, GetString(el, "chart_kind"), iconGroups);
    }

    internal static string BuildGroundingBlock(IReadOnlyList<GroundingReference> grounding) =>
        grounding.Count == 0
            ? "(RAG'de hiçbir referans bulunamadı. Kaynaksız kazanım/olgu iddiası üretme.)"
            : "RAG BAĞLAMI:\n" + string.Join("\n\n", grounding.Select((g, i) =>
                $"[KAYNAK {i + 1}] (doküman {g.ReferenceDocumentId}, sayfa {g.Page?.ToString() ?? "?"})\n{g.ChunkText}"));

    private static TransformQuestionResult ParseTransformResult(IReadOnlyDictionary<string, JsonElement> input, AiUsage usage)
    {
        var newOptions = input.TryGetValue("new_options", out var optionsEl) && optionsEl.ValueKind == JsonValueKind.Array
            ? optionsEl.EnumerateArray().Select(o => o.GetString() ?? "").ToList()
            : new List<string>();

        var distractors = new List<DistractorDto>();
        if (input.TryGetValue("distractors", out var distractorsEl) && distractorsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in distractorsEl.EnumerateArray())
            {
                var optionLabel = item.TryGetProperty("option_label", out var ol) ? ol.GetString() ?? "" : "";
                var misconceptionCode = item.TryGetProperty("misconception_code", out var mc) && mc.ValueKind == JsonValueKind.String
                    ? mc.GetString() : null;
                var explanation = item.TryGetProperty("explanation", out var ex) && ex.ValueKind == JsonValueKind.String
                    ? ex.GetString() : null;
                distractors.Add(new DistractorDto(optionLabel, misconceptionCode, explanation));
            }
        }

        return new TransformQuestionResult(
            NewQuestion: input.TryGetValue("new_question", out var nq) ? nq.GetString() ?? "" : "",
            NewOptions: newOptions,
            CorrectAnswer: input.TryGetValue("correct_answer", out var ca) ? ca.GetString() ?? "" : "",
            Solution: input.TryGetValue("solution", out var sol) ? sol.GetString() ?? "" : "",
            Distractors: distractors,
            Usage: usage);
    }

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
