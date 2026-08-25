namespace MaarifPlatform.Infrastructure.Ai;

public class OpenAiOptions
{
    /// <summary>Boşsa OpenAiLLMProvider açık bir hata fırlatır; Judge:SecondaryProvider boş
    /// bırakılırsa bu sağlayıcı hiç örneklenmez (aynı Ai:Anthropic deseni).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>DİKKAT: bu varsayılan doğrulanmadan güvenilmemeli — OpenAI model isimlendirmesi
    /// sık değişiyor ve tüketici (ChatGPT) isimleri API model-id'leriyle birebir eşleşmiyor.
    /// Devreye almadan önce OpenAI'nin güncel model dokümantasyonundan teyit edilmeli
    /// (bkz. GeminiOptions.Model'deki aynı uyarı).</summary>
    public string Model { get; set; } = "gpt-4o";

    public int MaxTokens { get; set; } = 2048;
}
