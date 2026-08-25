namespace MaarifPlatform.Application.Providers;

/// <summary>§10 Judge Provider Disagreement. Analyze/Transform/Generate'in birincil sağlayıcısı
/// hâlâ doğrudan enjekte edilen ILLMProvider'dan gelir (Ai:Provider switch'i, değişmedi) — bu
/// factory yalnızca Judge'ın ikincil/consensus çağrısı için isimle çözümleme sağlar
/// (IVisionProviderFactory'nin birebir aynısı).</summary>
public interface ILLMProviderFactory
{
    ILLMProvider Get(string providerName);
}
