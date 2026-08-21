namespace MaarifPlatform.Application.Vision;

/// <summary>§7 Multi-Provider Vision. Mevcut <c>ILLMProvider</c> deseniyle simetrik: somut
/// implementasyonlar (Gemini/OpenAI/Anthropic) Infrastructure'da yaşar, domain logic hiçbirine
/// bağımlı olmaz. Hangi sağlayıcının kullanılacağı config/routing üzerinden belirlenir.</summary>
public interface IVisionProvider
{
    /// <summary>Sağlayıcı adı (routing konfigürasyonuyla eşleştirmek için, örn. "gemini", "anthropic").</summary>
    string Name { get; }

    /// <summary>Tüm sayfayı analiz eder — soru sınırları henüz belirlenmemişken kaba bir genel bakış içindir.</summary>
    Task<VisualObservation> AnalyzePageAsync(byte[] pageImagePng, CancellationToken ct = default);

    /// <summary>Belirli bir sorunun görselini, soru metniyle birlikte analiz eder (§11 Text+Visual Fusion
    /// için gerekli bağlam — soru metni "ABC üçgeninde" diyorsa görselde gerçekten var mı kontrol edilir).</summary>
    Task<VisualObservation> AnalyzeQuestionImageAsync(byte[] questionImagePng, string questionText, CancellationToken ct = default);

    /// <summary>Ders/görsel türüne özel, derinlemesine ilişki çıkarımı (§5: point_on_segment,
    /// perpendicular, series_connection, bond_type vb.).</summary>
    Task<VisualObservation> ExtractVisualStructureAsync(byte[] imagePng, string visualType, CancellationToken ct = default);

    /// <summary>§6 Mathematical/Scientific Fidelity — üretilen gözlemi tutarlılık açısından denetler,
    /// düşük güvenli/belirsiz iddiaları <see cref="VisualWarning"/> olarak döner (boş liste = temiz).</summary>
    Task<IReadOnlyList<VisualWarning>> ValidateVisualStructureAsync(VisualObservation observation, CancellationToken ct = default);
}
