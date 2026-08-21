namespace MaarifPlatform.Infrastructure.Vision;

/// <summary>§9 Confidence Based Routing + §10 Provider Disagreement. SecondaryProvider boşsa
/// consensus akışı tamamen devre dışıdır (Sprint 5 davranışı korunur, ek maliyet yok) — bu,
/// §elestiri madde 6 (maliyet kontrolsüz büyümesin) ilkesinin bu özellik için uygulanmasıdır.</summary>
public class VisionRoutingOptions
{
    public string PrimaryProvider { get; set; } = "Local";
    public string? SecondaryProvider { get; set; }

    /// <summary>Birincil gözlemin güveni bu eşiğin ALTINDAYSA ikinci sağlayıcı çağrılır (§9).</summary>
    public decimal ConsensusConfidenceThreshold { get; set; } = 0.95m;
}
