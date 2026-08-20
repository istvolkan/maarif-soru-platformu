using MaarifPlatform.Infrastructure.Rag;

namespace MaarifPlatform.Tests.Rag;

public class LocalDeterministicEmbeddingProviderTests
{
    private readonly LocalDeterministicEmbeddingProvider _sut = new();

    [Fact]
    public async Task EmbedAsync_IsDeterministic_ForSameText()
    {
        var v1 = await _sut.EmbedAsync("EBOB ve EKOK kazanımı");
        var v2 = await _sut.EmbedAsync("EBOB ve EKOK kazanımı");

        Assert.Equal(v1, v2);
    }

    [Fact]
    public async Task EmbedAsync_ProducesDifferentVectors_ForDifferentText()
    {
        var v1 = await _sut.EmbedAsync("EBOB ve EKOK kazanımı");
        var v2 = await _sut.EmbedAsync("Denklem çözme kazanımı");

        Assert.NotEqual(v1, v2);
    }

    [Fact]
    public async Task EmbedAsync_ReturnsUnitLengthVector_WithConfiguredDimensions()
    {
        var vector = await _sut.EmbedAsync("örnek metin");

        Assert.Equal(_sut.Dimensions, vector.Length);

        var magnitude = Math.Sqrt(vector.Sum(v => (double)v * v));
        Assert.InRange(magnitude, 0.999, 1.001);
    }
}
