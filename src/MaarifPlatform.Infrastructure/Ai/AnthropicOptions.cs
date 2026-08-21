namespace MaarifPlatform.Infrastructure.Ai;

public class AnthropicOptions
{
    /// <summary>Boşsa AnthropicLLMProvider açık bir hata fırlatır; Embeddings:Provider deseniyle
    /// tutarlı olsun diye Ai:Provider=Local varsayılanında bu sağlayıcı hiç örneklenmez.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>§H model routing: Analysis "orta katman" bir görevdir. Varsayılan claude-opus-4-8'dir
    /// (maliyet için sessizce düşürülmez) — üretimde maliyet/kalite dengesini kurmak isteyen operatör
    /// bu alanı (örn. bir Sonnet modeline) açıkça değiştirebilir.</summary>
    public string Model { get; set; } = "claude-opus-4-8";

    public int MaxTokens { get; set; } = 4096;
}
