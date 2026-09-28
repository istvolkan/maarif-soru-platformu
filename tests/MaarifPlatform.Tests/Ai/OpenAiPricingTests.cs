using MaarifPlatform.Infrastructure.Ai;

namespace MaarifPlatform.Tests.Ai;

public class OpenAiPricingTests
{
    [Fact]
    public void EstimateCostUsd_NoCachedTokens_MatchesPlainFormula()
    {
        var cost = OpenAiPricing.EstimateCostUsd("gpt-4o", 1_000_000, 1_000_000);

        Assert.Equal(2.50m + 10.00m, cost);
    }

    [Fact]
    public void EstimateCostUsd_CachedTokens_AreDiscountedNotDoubleCounted()
    {
        // 1M input token, tamamı cache'den geldi — cached tokens InputTokenCount İÇİNDE sayılır.
        var allCached = OpenAiPricing.EstimateCostUsd("gpt-4o", 1_000_000, 0, cachedTokens: 1_000_000);
        var noneCached = OpenAiPricing.EstimateCostUsd("gpt-4o", 1_000_000, 0, cachedTokens: 0);

        Assert.Equal(2.50m * 0.5m, allCached);
        Assert.True(allCached < noneCached);
    }
}
