namespace MaarifPlatform.Infrastructure.Ai;

/// <summary>§9/§M Cost Ledger için kaba maliyet tahmini. Fiyatlar zamanla değişir — bu tablo
/// yalnızca AiRun kayıtlarına yaklaşık bir cost_usd yazmak içindir, faturalama kaynağı değildir.
/// Bilinmeyen bir model için Opus fiyatlandırmasına düşer (muhafazakâr üst sınır).</summary>
public static class AnthropicPricing
{
    private static readonly Dictionary<string, (decimal InputPer1M, decimal OutputPer1M)> Prices = new()
    {
        ["claude-opus-4-8"] = (5.00m, 25.00m),
        ["claude-opus-4-7"] = (5.00m, 25.00m),
        ["claude-sonnet-5"] = (3.00m, 15.00m),
        ["claude-sonnet-4-6"] = (3.00m, 15.00m),
        ["claude-haiku-4-5"] = (1.00m, 5.00m),
    };

    public static decimal EstimateCostUsd(string model, int inputTokens, int outputTokens)
    {
        var (inputPer1M, outputPer1M) = Prices.TryGetValue(model, out var price) ? price : Prices["claude-opus-4-8"];
        return inputTokens / 1_000_000m * inputPer1M + outputTokens / 1_000_000m * outputPer1M;
    }
}
