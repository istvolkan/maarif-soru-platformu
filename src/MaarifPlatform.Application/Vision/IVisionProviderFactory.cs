namespace MaarifPlatform.Application.Vision;

/// <summary>§7 Multi-Provider Vision. Tek bir IVisionProvider DI kaydı yerine (Sprint 5),
/// birden fazla sağlayıcının AYNI ANDA kullanılabilmesi (§10 Provider Disagreement) için
/// isimle çözümleme sağlar. "local", "gemini", "anthropic" desteklenir; bilinmeyen/boş isim
/// "local" (mock) sağlayıcıya düşer.</summary>
public interface IVisionProviderFactory
{
    IVisionProvider Get(string providerName);
}
