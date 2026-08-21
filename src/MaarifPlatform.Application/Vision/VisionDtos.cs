using MaarifPlatform.Application.Providers;

namespace MaarifPlatform.Application.Vision;

/// <summary>§3 VisionRouter kararı. Bir soru gerçekten Vision API'ye gönderilmeden önce bu kararın
/// üretilmesi zorunludur — §9/§21 maliyet ilkesi gereği her soru Vision'a gitmemeli.</summary>
public sealed record VisionRoutingDecision(
    bool RequiresVisual,
    string? VisualType,
    string Reason,
    decimal Confidence);

public sealed record VisualElement(string Id, string Type, string? Label, string? Value, string? Unit, decimal Confidence);

public sealed record VisualRelation(string Subject, string Relation, string Object, decimal Confidence);

/// <summary>§5/§6 — düşük güvenli veya belirsiz görsel-metin ilişkisi durumunda üretilir; kritik
/// ilişkilerde varlığı soruyu MANUAL_REVIEW_REQUIRED'a düşürmelidir.</summary>
public sealed record VisualWarning(string Type, string Message, decimal Confidence);

/// <summary>§29 KRİTİK KURAL: bu, Vision modelinin ürettiği bir GÖZLEMdir — "ground truth" değildir.
/// Reasoning Engine (mevcut ILLMProvider) bunu soru metni ve Maarif bilgi tabanıyla birlikte,
/// doğrulanması gereken bir girdi olarak ele almalıdır.</summary>
public sealed record VisualObservation(
    string VisualType,
    string Description,
    decimal Confidence,
    IReadOnlyList<VisualElement> Elements,
    IReadOnlyList<VisualRelation> Relations,
    IReadOnlyList<string> VisualText,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<string> Measurements,
    IReadOnlyList<VisualWarning> Warnings,
    AiUsage Usage);
