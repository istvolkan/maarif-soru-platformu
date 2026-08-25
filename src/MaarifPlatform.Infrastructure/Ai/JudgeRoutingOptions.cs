namespace MaarifPlatform.Infrastructure.Ai;

/// <summary>§8/§9/§10 Judge Provider Disagreement — Vision'ın VisionRoutingOptions'ıyla aynı
/// desen. SecondaryProvider boşsa (varsayılan) consensus akışı tamamen devre dışıdır, Sprint 8
/// davranışı bire bir korunur — ek maliyet yalnızca açıkça yapılandırıldığında oluşur.
/// Birincil sağlayıcı burada YOK — o hâlâ Ai:Provider switch'inden gelir, değişmedi.</summary>
public class JudgeRoutingOptions
{
    public string? SecondaryProvider { get; set; }

    /// <summary>Birincil Judge'ın QualityScore/100'ü bu eşiğin ALTINDAYSA ikinci sağlayıcı
    /// çağrılır (§9) — Vision'ın Confidence eşiğiyle aynı tetikleme mantığı.</summary>
    public decimal ConsensusConfidenceThreshold { get; set; } = 0.7m;

    /// <summary>İki sağlayıcının QualityScore'ları arasındaki fark bu değeri aşarsa
    /// disagreement uyarısı üretilir (bkz. JudgeConsensusChecker).</summary>
    public int ConsensusScoreDeltaThreshold { get; set; } = 20;
}
