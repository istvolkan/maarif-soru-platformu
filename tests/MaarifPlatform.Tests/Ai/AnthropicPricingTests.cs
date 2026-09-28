using MaarifPlatform.Infrastructure.Ai;

namespace MaarifPlatform.Tests.Ai;

public class AnthropicPricingTests
{
    [Fact]
    public void EstimateCostUsd_NoCache_MatchesPlainInputOutputFormula()
    {
        var cost = AnthropicPricing.EstimateCostUsd("claude-sonnet-5", 1_000_000, 1_000_000);

        Assert.Equal(3.00m + 15.00m, cost);
    }

    [Fact]
    public void EstimateCostUsd_CacheRead_IsCheaperThanPlainInput()
    {
        var withoutCache = AnthropicPricing.EstimateCostUsd("claude-sonnet-5", 1_000_000, 0);
        var allFromCacheRead = AnthropicPricing.EstimateCostUsd("claude-sonnet-5", 0, 0, cacheCreationTokens: 0, cacheReadTokens: 1_000_000);

        Assert.True(allFromCacheRead < withoutCache);
        Assert.Equal(3.00m * 0.1m, allFromCacheRead);
    }

    [Fact]
    public void EstimateCostUsd_CacheCreation_CostsMoreThanPlainInput()
    {
        var withoutCache = AnthropicPricing.EstimateCostUsd("claude-sonnet-5", 1_000_000, 0);
        var cacheWrite = AnthropicPricing.EstimateCostUsd("claude-sonnet-5", 0, 0, cacheCreationTokens: 1_000_000, cacheReadTokens: 0);

        Assert.True(cacheWrite > withoutCache);
        Assert.Equal(3.00m * 1.25m, cacheWrite);
    }

    [Fact]
    public void EstimateCostUsd_UnknownModel_FallsBackToOpusPricing()
    {
        var cost = AnthropicPricing.EstimateCostUsd("some-future-model", 1_000_000, 0);

        Assert.Equal(5.00m, cost);
    }
}
