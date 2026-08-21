namespace MaarifPlatform.Application.Vision;

/// <summary>§3 VisionRouter. V1 implementasyonu (Infrastructure/Vision/HeuristicVisionRouter)
/// tamamen AI'sız, ücretsizdir (§9); arayüz async tutulur ki ileride cheap-tier bir sınıflandırıcı
/// çağıran bir implementasyonla değiştirilebilsin.</summary>
public interface IVisionRouter
{
    Task<VisionRoutingDecision> DecideAsync(string questionText, string? originalVisualReference, CancellationToken ct = default);
}
