namespace MaarifPlatform.Domain.Entities;

/// <summary>§18 Versioning — örn. "MM-MATH-2026-v1.0". Standart değişince hangi sorunun
/// hangi versiyona göre değerlendirildiği buradan izlenir.</summary>
public class MaarifStandardVersion : Entity
{
    public string Code { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
    public string? Notes { get; set; }
}
