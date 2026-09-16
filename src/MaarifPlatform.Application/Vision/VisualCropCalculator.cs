namespace MaarifPlatform.Application.Vision;

public sealed record CropRectanglePx(int X, int Y, int Width, int Height);

/// <summary>Vision modelinin döndürdüğü normalize <see cref="VisualBoundingBox"/>'ı, tam sayfa
/// görüntüsünün gerçek piksel boyutuna göre bir kırpma dikdörtgenine çevirir. Modelin ürettiği
/// koordinatlar (bazen hafif taşan/negatif) asla doğrudan güvenilmez — burada doğrulanır,
/// [0,1] aralığına kenetlenir ve figürün kenarlarının kırpılmaması için küçük bir pay eklenir.
/// Kutu geçersizse (yok/sıfır alan/tamamen sayfa dışı) null döner — çağıran bu durumda tam sayfa
/// görüntüsünü kırpmadan kullanmaya devam etmelidir (bkz. QuestionVisualAsset.BoundingBoxJson).</summary>
public static class VisualCropCalculator
{
    private const decimal PaddingRatio = 0.03m;

    public static CropRectanglePx? Compute(VisualBoundingBox? box, int pageWidthPx, int pageHeightPx)
    {
        if (box is null || pageWidthPx <= 0 || pageHeightPx <= 0)
        {
            return null;
        }

        if (box.Width <= 0 || box.Height <= 0)
        {
            return null;
        }

        var x0 = Clamp01(box.X);
        var y0 = Clamp01(box.Y);
        var x1 = Clamp01(box.X + box.Width);
        var y1 = Clamp01(box.Y + box.Height);

        if (x1 <= x0 || y1 <= y0)
        {
            return null;
        }

        // Model figürün tam kenarını işaretlemiş olabilir — küçük bir pay bırakılmazsa
        // kırpılan görüntüde çizgi/etiket kenardan kesilebilir.
        var padX = (x1 - x0) * PaddingRatio;
        var padY = (y1 - y0) * PaddingRatio;
        x0 = Clamp01(x0 - padX);
        y0 = Clamp01(y0 - padY);
        x1 = Clamp01(x1 + padX);
        y1 = Clamp01(y1 + padY);

        var pxX = (int)Math.Floor(x0 * pageWidthPx);
        var pxY = (int)Math.Floor(y0 * pageHeightPx);
        var pxRight = (int)Math.Ceiling(x1 * pageWidthPx);
        var pxBottom = (int)Math.Ceiling(y1 * pageHeightPx);

        pxRight = Math.Min(pxRight, pageWidthPx);
        pxBottom = Math.Min(pxBottom, pageHeightPx);

        var width = pxRight - pxX;
        var height = pxBottom - pxY;

        // Aşırı küçük kırpma (ör. model neredeyse sıfır boyutlu bir kutu döndürdü) muhtemelen
        // yanlış tespit — tam sayfaya geri dönmek, okunaksız 2-3 piksellik bir görsel göstermekten
        // daha güvenlidir.
        if (width < 20 || height < 20)
        {
            return null;
        }

        return new CropRectanglePx(pxX, pxY, width, height);
    }

    private static decimal Clamp01(decimal value) => Math.Min(1m, Math.Max(0m, value));
}
