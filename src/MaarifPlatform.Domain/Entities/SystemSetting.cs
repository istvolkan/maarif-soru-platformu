namespace MaarifPlatform.Domain.Entities;

/// <summary>Sprint 11 — appsettings.json'ı çalışma zamanında geçersiz kılan anahtar/değer
/// çiftleri (§örn. Ai:Anthropic:ApiKey). DatabaseSettingsProvider tarafından IConfiguration'a
/// katman olarak eklenir; DB'de kayıt yoksa appsettings.json'daki değer geçerli kalır. Key,
/// appsettings.json'daki tam config path'idir (örn. "Ai:Provider").</summary>
public class SystemSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedByUserId { get; set; }
}
