namespace MaarifPlatform.Infrastructure.Vision;

public class GeminiOptions
{
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>§8.1 varsayılan Vision sağlayıcısı. Google'ın model kataloğu sık değiştiği ve bu
    /// projede canlı bir kaynağım olmadığı için burada verilen değeri KESİN doğru kabul etmeyin —
    /// devreye almadan önce Google AI Studio / Gemini API dokümanından geçerli bir model ID ile
    /// doğrulayıp gerekirse appsettings üzerinden güncelleyin.</summary>
    public string Model { get; set; } = "gemini-2.0-flash";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
}
