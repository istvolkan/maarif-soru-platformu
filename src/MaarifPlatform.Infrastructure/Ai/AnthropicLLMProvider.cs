using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Rubric;
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
    private const string TransformToolName = "submit_transformation";
    private const string EvaluateToolName = "submit_evaluation";
    private const string GenerateToolName = "submit_generation";

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
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(parameters, ct);
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
            options.Model,
            inputTokens,
            outputTokens,
            AnthropicPricing.EstimateCostUsd(options.Model, inputTokens, outputTokens),
            (int)stopwatch.ElapsedMilliseconds);

        return ParseResult(toolUse.Input, usage);
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
        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = options.MaxTokens,
            System = BuildEvaluateSystemPrompt(request),
            Tools = [BuildEvaluationTool()],
            ToolChoice = new ToolChoiceTool { Name = EvaluateToolName },
            Messages = [new() { Role = Role.User, Content = BuildEvaluateUserContent(request) }],
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == EvaluateToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_evaluation tool_use bloğu bulunamadı.");

        var usage = BuildUsage(response, stopwatch, options);
        return ParseEvaluateResult(toolUse.Input, usage);
    }

    public async Task<GenerateQuestionResult> GenerateQuestionAsync(GenerateQuestionRequest request, CancellationToken ct = default)
    {
        var (options, client) = Current();
        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = options.MaxTokens,
            System = BuildGenerateSystemPrompt(request),
            Tools = [BuildGenerationTool()],
            ToolChoice = new ToolChoiceTool { Name = GenerateToolName },
            Messages = [new() { Role = Role.User, Content = BuildGenerateUserContent(request) }],
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await client.Messages.Create(parameters, ct);
        stopwatch.Stop();

        var toolUse = response.Content
            .Select(b => b.Value)
            .OfType<ToolUseBlock>()
            .FirstOrDefault(b => b.Name == GenerateToolName)
            ?? throw new InvalidOperationException("Anthropic yanıtında beklenen submit_generation tool_use bloğu bulunamadı.");

        var usage = BuildUsage(response, stopwatch, options);
        return ParseGenerateResult(toolUse.Input, usage);
    }

    private AiUsage BuildUsage(Message response, Stopwatch stopwatch, AnthropicOptions options)
    {
        var inputTokens = (int)response.Usage.InputTokens;
        var outputTokens = (int)response.Usage.OutputTokens;
        return new AiUsage(
            Name, options.Model, inputTokens, outputTokens,
            AnthropicPricing.EstimateCostUsd(options.Model, inputTokens, outputTokens),
            (int)stopwatch.ElapsedMilliseconds);
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
            5. Cevabını YALNIZCA submit_analysis aracını çağırarak ver.

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

    private static JsonElement BuildDistractorsSchema() => JsonSerializer.SerializeToElement(new
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

    private static Tool BuildGenerationTool()
    {
        var properties = new Dictionary<string, JsonElement>
        {
            ["question"] = Schema("string", "Üretilen soru metni."),
            ["options"] = JsonSerializer.SerializeToElement(new
            {
                type = "array",
                description = "3-6 şık.",
                items = new { type = "string" },
                minItems = 3,
                maxItems = 6
            }),
            ["correct_answer"] = Schema("string", "Doğru şıkkın metni (options içindeki değerlerden biri)."),
            ["solution"] = Schema("string", "Adım adım çözüm."),
            ["distractors"] = BuildDistractorsSchema()
        };

        return new Tool
        {
            Name = GenerateToolName,
            Description = "Üretilen sorunun yapılandırılmış sonucunu bildir.",
            InputSchema = new()
            {
                Properties = properties,
                Required = ["question", "options", "correct_answer", "solution", "distractors"]
            }
        };
    }

    private static string BuildGenerateSystemPrompt(GenerateQuestionRequest request) => $"""
        Sen Türkiye Yüzyılı Maarif Modeli'ne göre sıfırdan matematik sorusu üreten bir uzmansın.

        HEDEF:
        - Sınıf: {request.Grade}, Ders: {request.Subject}
        - Tema: {request.Theme}
        - Kazanım kodu: {request.LearningOutcomeCode}
        - Zorluk: {request.Difficulty}
        - Soru tipi: {request.QuestionType}
        - Muhakeme tipi: {request.ReasoningType}

        KURALLAR:
        1. Yalnızca aşağıdaki [KAYNAK n] bloklarına dayanarak kazanım/olgu iddiası üret.
           Kaynakta olmayan bir MEB kazanımını ASLA uydurma.
        2. Sorunun matematiksel olarak doğru ve tek bir doğru cevabı olmasına dikkat et.
        3. Her yanlış şık için bir çeldirici kaydı ver; mümkünse bir öğrenci hata tipini
           (misconception_code) belirt, emin değilsen boş bırak — uydurma.
        4. Cevabını YALNIZCA submit_generation aracını çağırarak ver.

        {BuildGroundingBlock(request.Grounding)}
        """;

    private static string BuildGenerateUserContent(GenerateQuestionRequest request) => $"""
        BAĞLAM/SENARYO İSTEĞİ:
        {request.Context}
        """;

    private static GenerateQuestionResult ParseGenerateResult(IReadOnlyDictionary<string, JsonElement> input, AiUsage usage)
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

        return new GenerateQuestionResult(
            Question: input.TryGetValue("question", out var q) ? q.GetString() ?? "" : "",
            Options: options,
            CorrectAnswer: input.TryGetValue("correct_answer", out var ca) ? ca.GetString() ?? "" : "",
            Solution: input.TryGetValue("solution", out var sol) ? sol.GetString() ?? "" : "",
            Distractors: distractors,
            Usage: usage);
    }

    private static string BuildGroundingBlock(IReadOnlyList<GroundingReference> grounding) =>
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
