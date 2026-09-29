namespace MaarifPlatform.Infrastructure.Ai;

/// <summary>Ai:ClaudeCli — ClaudeCliLLMProvider'ın çalışma-zamanlı (IOptionsMonitor) ayarları.
/// API anahtarı YOKTUR: CLI, uygulamanın çalıştığı işletim sistemi kullanıcısının `claude`
/// oturumunu (abonelik girişi veya ortamdaki ANTHROPIC_API_KEY) kullanır.</summary>
public class ClaudeCliOptions
{
    /// <summary>`claude` çalıştırılabilir dosyası. Yalnızca ad verilirse PATH üzerinden çözülür;
    /// makinede birden fazla kurulum varsa tam yol verilmelidir.</summary>
    public string ExecutablePath { get; set; } = "claude";

    /// <summary>`--model` değeri: takma ad (opus/sonnet/haiku) veya tam model adı. Boşsa CLI'ın
    /// kendi varsayılan modeli kullanılır.</summary>
    public string Model { get; set; } = "opus";

    /// <summary>Tek bir CLI çağrısının üst süresi; aşılırsa süreç (alt süreçleriyle) sonlandırılır.</summary>
    public int TimeoutSeconds { get; set; } = 300;
}
