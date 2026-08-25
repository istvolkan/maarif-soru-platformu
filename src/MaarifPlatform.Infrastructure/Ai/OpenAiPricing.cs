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
    };

    public static decimal EstimateCostUsd(string model, int inputTokens, int outputTokens)
    {
        var (inputPer1M, outputPer1M) = Prices.TryGetValue(model, out var price) ? price : Prices["gpt-4o"];
        return inputTokens / 1_000_000m * inputPer1M + outputTokens / 1_000_000m * outputPer1M;
    }
}
