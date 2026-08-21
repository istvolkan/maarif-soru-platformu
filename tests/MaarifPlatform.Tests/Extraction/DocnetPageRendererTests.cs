using MaarifPlatform.Infrastructure.Extraction;

namespace MaarifPlatform.Tests.Extraction;

public class DocnetPageRendererTests
{
    private static string AssetPath => Path.Combine(AppContext.BaseDirectory, "TestAssets", "sample.pdf");

    [Fact]
    public async Task RenderPageAsync_ProducesValidPng_WithPositiveDimensions()
    {
        var sut = new DocnetPageRenderer();
        await using var stream = File.OpenRead(AssetPath);

        var result = await sut.RenderPageAsync(stream, pageNo: 1);

        Assert.Equal(1, result.PageNo);
        Assert.True(result.WidthPx > 0);
        Assert.True(result.HeightPx > 0);
        Assert.True(result.PngBytes.Length > 100, "PNG çıktısı boş/anlamsız olmamalı.");

        // PNG magic number: 89 50 4E 47 0D 0A 1A 0A
        byte[] pngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.Equal(pngMagic, result.PngBytes[..8]);
    }
}
