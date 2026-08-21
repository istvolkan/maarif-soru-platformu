namespace MaarifPlatform.Domain.Entities;

/// <summary>§13 Visual Asset Extraction — bir soruya ait, PDF sayfasından çıkarılmış görsel öğe
/// (tam sayfa görüntüsü veya crop). Bir soruda birden fazla görsel olabilir. <see cref="AssetHash"/>
/// aynı görselin tekrar Vision API'ye gönderilmesini önleyen cache anahtarıdır (§26).</summary>
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
}
