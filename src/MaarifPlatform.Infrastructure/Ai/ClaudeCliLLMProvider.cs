using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Models.Messages;
using MaarifPlatform.Application.Providers;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Infrastructure.Ai;

/// <summary>AnthropicLLMProvider'ın prompt/şema/ayrıştırma mantığını AYNEN kullanır, yalnızca
/// modele ulaşma yolunu değiştirir: Messages API yerine yerel `claude` CLI'ı `-p` (print) modunda
/// çalıştırır. Zorunlu tool-use'un karşılığı `--json-schema` yapılandırılmış çıktısıdır — CLI,
/// yanıtı şemaya karşı doğrular ve `structured_output` alanında döner.
///
/// Yalnızca geliştirme/deneme içindir: CLI, sunucudaki işletim sistemi kullanıcısının Claude
/// oturumunu kullanır. Birden çok kullanıcıya hizmet veren bir ortamda API anahtarlı
/// AnthropicLLMProvider kullanılmalıdır (abonelik kişisel kullanım içindir).</summary>
public class ClaudeCliLLMProvider(
    IOptionsMonitor<ClaudeCliOptions> cliOptionsMonitor,
    IOptionsMonitor<AnthropicOptions> anthropicOptionsMonitor) : AnthropicLLMProvider(anthropicOptionsMonitor)
{
    /// <summary>Windows komut satırı ~32K karakterle sınırlı; JSON şeması da argüman olarak
    /// gittiği için system prompt bu sınırı aşarsa stdin'e (kullanıcı mesajının önüne) taşınır.</summary>
    private const int MaxInlineSystemPromptChars = 16_000;

    private const string FallbackSystemPrompt =
        "Sen yapılandırılmış çıktı üreten bir değerlendirme motorusun. Görev talimatları kullanıcı " +
        "mesajının başındaki GÖREV TALİMATLARI bölümündedir; onlara harfiyen uy.";

    public override string Name => "claude-cli";

    protected override async Task<(IReadOnlyDictionary<string, JsonElement> Input, AiUsage Usage)> InvokeToolAsync(
        ToolInvocation call, CancellationToken ct)
    {
        var options = cliOptionsMonitor.CurrentValue;
        var model = call.ModelOverride ?? (string.IsNullOrWhiteSpace(options.Model) ? null : options.Model.Trim());

        // Prompt'lar "YALNIZCA submit_x aracını çağırarak ver" der; CLI'da o isimde bir araç yok,
        // karşılığı yapılandırılmış çıktıdır — modele bu eşlemeyi açıkça söyle.
        var system = $"""
            {call.System}

            ÇIKTI BİÇİMİ: Bu ortamda "{call.Tool.Name}" aracı, yapılandırılmış çıktı (structured
            output) mekanizmasıdır. Cevabını verilen JSON şemasına uyan yapılandırılmış çıktı olarak
            ver. Aracın amacı: {call.Tool.Description}
            Bilmediğin/döndürmemen gereken isteğe bağlı bir alanı ya hiç gönderme ya da null ver.
            Çıktın şema doğrulamasından geçemezse hatayı düzeltip AYNI gerçek içerikle tekrar gönder —
            aracı denemek için ASLA test/yer tutucu veri ("Test question", "A/B/C" vb.) gönderme.
            """;

        var args = new List<string>
        {
            "-p",
            "--output-format", "json",
            "--json-schema", BuildJsonSchema(call.Tool),
            // Ajan davranışını kapat: araç yok, kullanıcı/proje ayarı ve MCP yüklenmez, oturum
            // diske yazılmaz — her çağrı bağımsız, tek bir model yanıtıdır.
            "--tools", "",
            "--setting-sources", "",
            "--strict-mcp-config",
            "--no-session-persistence",
        };

        string stdin;
        if (system.Length <= MaxInlineSystemPromptChars)
        {
            args.AddRange(["--system-prompt", system]);
            stdin = call.UserContent;
        }
        else
        {
            args.AddRange(["--system-prompt", FallbackSystemPrompt]);
            stdin = $"GÖREV TALİMATLARI:\n{system}\n\n---\n\n{call.UserContent}";
        }

        if (model is not null)
        {
            args.AddRange(["--model", model]);
        }

        var stopwatch = Stopwatch.StartNew();
        var (exitCode, stdout, stderr) = await RunAsync(options, args, stdin, ct);
        stopwatch.Stop();

        return ParseOutput(exitCode, stdout, stderr, model, (int)stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Admin Ayarlar ekranındaki "CLI'ı Test Et" butonu için: çalıştırılabilir dosyanın
    /// bulunduğunu ve çalıştığını doğrular, sürüm metnini döner.</summary>
    public static async Task<string> GetVersionAsync(string executablePath, CancellationToken ct = default)
    {
        var options = new ClaudeCliOptions { ExecutablePath = executablePath, TimeoutSeconds = 30 };
        var (exitCode, stdout, stderr) = await RunAsync(options, ["--version"], null, ct);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"claude --version {exitCode} koduyla çıktı: {Truncate(stderr)}");
        }

        return stdout.Trim();
    }

    /// <summary>Anthropic Tool'un input şemasını CLI'ın beklediği tam JSON Schema nesnesine çevirir.
    /// Messages API tool girdisini şemaya karşı katı doğrulamaz; CLI doğrular ve reddederse model
    /// yeniden denemeye girer (gözlemlenen sonuç: yer tutucu "Test question" çıktısı). Prompt'lar
    /// "emin değilsen döndürme" dediği için model isteğe bağlı alanlara sıkça null yazar — bu yüzden
    /// required dışındaki her alan null kabul edecek şekilde gevşetilir. Parse* metotları null'ı
    /// zaten "alan yok" olarak ele alır.</summary>
    public static string BuildJsonSchema(Tool tool)
    {
        var properties = new JsonObject();
        foreach (var (key, value) in tool.InputSchema.Properties ?? new Dictionary<string, JsonElement>())
        {
            properties[key] = JsonNode.Parse(value.GetRawText());
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray((tool.InputSchema.Required ?? []).Select(r => (JsonNode?)JsonValue.Create(r)).ToArray())
        };

        AllowNullForOptionalProperties(schema);
        return schema.ToJsonString();
    }

    private static void AllowNullForOptionalProperties(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return;
        }

        if (obj["properties"] is JsonObject properties)
        {
            var required = (obj["required"] as JsonArray)?.Select(r => r?.GetValue<string>()).ToHashSet() ?? [];
            foreach (var (name, property) in properties)
            {
                if (!required.Contains(name) && property is JsonObject propertySchema)
                {
                    MakeNullable(propertySchema);
                }

                AllowNullForOptionalProperties(property);
            }
        }

        AllowNullForOptionalProperties(obj["items"]);
    }

    private static void MakeNullable(JsonObject schema)
    {
        if (schema["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out var type) && type != "null")
        {
            schema["type"] = new JsonArray(type, "null");
        }

        if (schema["enum"] is JsonArray values && values.All(v => v is not null))
        {
            values.Add(null);
        }
    }

    public static (IReadOnlyDictionary<string, JsonElement> Input, AiUsage Usage) ParseOutput(
        int exitCode, string stdout, string stderr, string? requestedModel, int elapsedMs)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(stdout);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"Claude CLI geçerli bir JSON yanıtı döndürmedi (çıkış kodu {exitCode}). " +
                $"stderr: {Truncate(stderr)} stdout: {Truncate(stdout)}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            var isError = root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
            var subtype = root.TryGetProperty("subtype", out var st) ? st.GetString() : null;
            if (exitCode != 0 || isError || subtype != "success")
            {
                var detail = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : stderr;
                throw new InvalidOperationException(
                    $"Claude CLI çağrısı başarısız (çıkış kodu {exitCode}, durum {subtype ?? "?"}): {Truncate(detail)}");
            }

            if (!root.TryGetProperty("structured_output", out var output) || output.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Claude CLI yanıtında beklenen structured_output nesnesi bulunamadı.");
            }

            var input = output.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

            int GetUsage(string key) =>
                root.TryGetProperty("usage", out var u) && u.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
                    ? v.GetInt32() : 0;

            // CLI'ın bildirdiği maliyet liste fiyatı üzerinden bir TAHMİNDİR; abonelikle
            // kullanımda gerçekte token başına ödeme yapılmaz.
            var cost = root.TryGetProperty("total_cost_usd", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetDecimal() : 0m;
            var latency = root.TryGetProperty("duration_ms", out var d) && d.ValueKind == JsonValueKind.Number
                ? d.GetInt32() : elapsedMs;

            var usage = new AiUsage(
                "claude-cli",
                ResolveModel(root, requestedModel),
                GetUsage("input_tokens"),
                GetUsage("output_tokens"),
                cost,
                latency,
                GetUsage("cache_creation_input_tokens"),
                GetUsage("cache_read_input_tokens"));

            return (input, usage);
        }
    }

    /// <summary>modelUsage, CLI'ın yardımcı çağrılarını (örn. küçük bir Haiku modeli) da içerir;
    /// asıl yanıtı üreten model en çok çıktı token'ı üretendir.</summary>
    private static string ResolveModel(JsonElement root, string? requestedModel)
    {
        if (root.TryGetProperty("modelUsage", out var mu) && mu.ValueKind == JsonValueKind.Object)
        {
            var main = mu.EnumerateObject()
                .OrderByDescending(p => p.Value.TryGetProperty("outputTokens", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : 0)
                .Select(p => p.Name)
                .FirstOrDefault();
            if (main is not null)
            {
                return main;
            }
        }

        return requestedModel ?? "claude-cli-default";
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        ClaudeCliOptions options, IReadOnlyList<string> args, string? stdin, CancellationToken ct)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(options.ExecutablePath) ? "claude" : options.ExecutablePath.Trim(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Proje dizinindeki CLAUDE.md vb. bağlama sızmasın diye nötr bir dizinde çalıştır.
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Uygulama bir Claude Code oturumunun içinden başlatıldıysa iç içe oturum algılamasını tetiklemesin.
        startInfo.Environment.Remove("CLAUDECODE");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"Claude CLI başlatılamadı ('{startInfo.FileName}'): {ex.Message}. Ai:ClaudeCli:ExecutablePath " +
                "ayarını kontrol edin; CLI'ın bu makinede kurulu ve oturum açılmış olması gerekir.", ex);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)));

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin.AsMemory(), timeout.Token);
            }

            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }

            if (ct.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"Claude CLI {options.TimeoutSeconds} saniye içinde yanıt vermedi.");
        }
    }

    private static string Truncate(string? text, int max = 1000) =>
        string.IsNullOrEmpty(text) ? "(boş)" : text.Length <= max ? text.Trim() : text[..max].Trim() + "…";
}
