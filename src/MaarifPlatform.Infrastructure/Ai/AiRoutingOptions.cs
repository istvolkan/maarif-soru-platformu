namespace MaarifPlatform.Infrastructure.Ai;

/// <summary>Sprint 11 — Ai:Provider'ın çalışma-zamanlı (IOptionsMonitor) karşılığı.
/// Analyze/Transform/Generate'in birincil ILLMProvider'ı artık DI-kayıt-zamanında değil,
/// her çağrıda ILLMProviderFactory.Get(aiRouting.CurrentValue.Provider) ile seçilir — Judge'ın
/// Sprint 10'da kurduğu isimle-çözümleme deseninin birincile de uygulanmış hali.</summary>
public class AiRoutingOptions
{
    public string Provider { get; set; } = "Local";
}
