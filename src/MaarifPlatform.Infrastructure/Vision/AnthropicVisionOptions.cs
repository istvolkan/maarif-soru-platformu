namespace MaarifPlatform.Infrastructure.Vision;

public class AnthropicVisionOptions
{
    /// <summary>Ayrı tutulur (Ai:Anthropic'ten farklı) — operatör Vision ve Analysis için farklı
    /// hesap/limit kullanmak isteyebilir; aynı anahtarı paylaşmak istiyorsa ikisine de aynı
    /// değeri yazabilir.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "claude-opus-4-8";
    public int MaxTokens { get; set; } = 4096;
}
