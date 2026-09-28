namespace MaarifPlatform.Infrastructure.Ai;

/// <summary>§9/§M Cost Ledger için kaba maliyet tahmini — AnthropicPricing'in Judge ikincil
/// sağlayıcı karşılığı. Fiyatlar zamanla değişir, faturalama kaynağı değildir. Model
/// AYNI ZAMANDA doğrulanmadan güvenilmemeli notuna tabidir (bkz. OpenAiOptions.Model).</summary>
public static class OpenAiPricing
{
    private static readonly Dictionary<string, (decimal InputPer1M, decimal OutputPer1M)> Prices = new()
    {
        ["gpt-4o"] = (2.50m, 10.00m),
        ["gpt-4o-mini"] = (0.15m, 0.60m),
        // §16 zorluk bazlı yönlendirme — 2026-09-22 tarihli GPT-6 ailesi (Luna/Sol/Astra).
        ["gpt-6-luna"] = (0.10m, 0.50m),
        ["gpt-6-sol"] = (2.00m, 10.00m),
        ["gpt-6-astra"] = (10.00m, 50.00m),
    };

    public static decimal EstimateCostUsd(string model, int inputTokens, int outputTokens)
        => EstimateCostUsd(model, inputTokens, outputTokens, 0);

    /// <summary>OpenAI, Anthropic'in aksine cache_control gerektirmeyen OTOMATİK prompt caching
    /// uygular (aynı prefix'e sahip &gt;1024 token'lık istekler için) — kodda tetiklemeye gerek
    /// yok, yalnızca maliyeti doğru yansıtmak için cachedTokens (usage.InputTokenDetails.
    /// CachedTokenCount) ayrıca düşülüp indirimli fiyatla eklenir. cachedTokens zaten inputTokens
    /// İÇİNDE sayılır (ayrı bir sayaç değil) — bu yüzden taban maliyetten çıkarılıp indirimli
    /// oranla geri eklenir. İndirim oranı (~%50) OpenAI'nin güncel dokümantasyonundan teyit
    /// edilmeli (bkz. OpenAiOptions.Model'deki aynı uyarı).</summary>
    public static decimal EstimateCostUsd(string model, int inputTokens, int outputTokens, int cachedTokens)
    {
        // Bilinmeyen bir model için en pahalı bilinen katmana (Astra) düşer — AnthropicPricing'in
        // Opus fallback'iyle AYNI ilke: maliyeti olduğundan düşük göstermemek.
        var (inputPer1M, outputPer1M) = Prices.TryGetValue(model, out var price) ? price : Prices["gpt-6-astra"];
        var uncachedInputTokens = Math.Max(0, inputTokens - cachedTokens);
        return uncachedInputTokens / 1_000_000m * inputPer1M
            + cachedTokens / 1_000_000m * inputPer1M * 0.5m
            + outputTokens / 1_000_000m * outputPer1M;
    }
}
