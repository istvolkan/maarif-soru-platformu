namespace MaarifPlatform.Domain.Entities;

/// <summary>§13 Visual Asset Extraction — bir soruya ait görsel öğe. İki kaynaktan gelebilir:
/// PDF sayfasından çıkarılmış (tam sayfa görüntüsü veya crop, <see cref="BookPageId"/> dolu) ya
/// da Faz 2 Görsel Üretim Motoru'nun ürettiği deterministik SVG (<see cref="BookPageId"/> null,
/// <see cref="ContentType"/>="image/svg+xml"). Bir soruda birden fazla görsel olabilir.
/// <see cref="AssetHash"/> aynı görselin tekrar Vision API'ye gönderilmesini önleyen cache
/// anahtarıdır (§26).</summary>
public class QuestionVisualAsset : Entity
{
    public Guid QuestionId { get; set; }
    public Question? Question { get; set; }

    public Guid? BookPageId { get; set; }
    public BookPage? BookPage { get; set; }

    public string StorageUri { get; set; } = string.Empty;

    /// <summary>Sayfa içindeki konum — {x, y, width, height}; tam sayfa görüntüsü için null.</summary>
    public string? BoundingBoxJson { get; set; }

    public int? WidthPx { get; set; }
    public int? HeightPx { get; set; }

    /// <summary>SHA-256(görüntü baytları) — §26 Vision result cache anahtarının bir parçası.</summary>
    public string AssetHash { get; set; } = string.Empty;

    /// <summary>Faz 2 öncesi kayıtlarda null (PDF-crop görselleri hep PNG'dir) — media endpoint
    /// bu durumda "image/png" varsayar, geriye dönük kırılmaz. Üretilen görseller için
    /// "image/svg+xml".</summary>
    public string? ContentType { get; set; }
}
