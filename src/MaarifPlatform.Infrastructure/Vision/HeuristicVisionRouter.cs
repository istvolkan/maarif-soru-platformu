using MaarifPlatform.Application.Vision;

namespace MaarifPlatform.Infrastructure.Vision;

/// <summary>§3/§9 VisionRouter — ücretsiz, AI'sız ön-filtre. Anahtar kelime + OriginalVisualReference
/// doluluğuna dayanır; her soruyu Vision API'ye göndermemek için ilk (ve çoğu durumda tek) katmandır.
/// Belirsiz durumlarda cheap-tier bir sınıflandırıcıyla değiştirilebilecek şekilde
/// <see cref="IVisionRouter"/> arkasında tutulur (§elestiri: bu V1'de kasıtlı bir sınırlamadır).</summary>
public class HeuristicVisionRouter : IVisionRouter
{
    private static readonly (string VisualType, string[] Keywords)[] KeywordGroups =
    [
        // Her grupta Türkçe noktalı harfli asıl biçimin yanında ASCII'ye katlanmış bir yedek de
        // tutulur — eski taranmış/OCR'lı kitaplarda veya bozuk font kodlamasında (ş/ç/ı/ğ kaybı)
        // extraction metni noktasız gelebilir; router bu durumda da sessizce kaçırmamalı.
        ("geometry_diagram", ["şekilde", "sekilde", "şekil üzerinde", "sekil uzerinde", "üçgeninde",
            "ucgeninde", "çemberinde", "cemberinde", "dörtgeninde", "yukarıdaki şekil", "yukaridaki sekil",
            "verilen şekilde", "geometrik şekil", "açıortay", "kenar uzunluk"]),
        ("coordinate_system", ["koordinat düzleminde", "koordinat sisteminde", "x ekseninde", "y ekseninde"]),
        ("function_graph", ["fonksiyonun grafiği", "grafiğe göre", "aşağıdaki grafik", "grafikte verilen"]),
        ("statistical_chart", ["sütun grafiği", "pasta grafiği", "histogram", "çubuk grafik"]),
        ("data_table", ["tabloda", "tabloya göre", "aşağıdaki tablo", "tabloda verilen"]),
        ("electric_circuit", ["devrede", "devre şeması", "elektrik devresi", "ampermetre", "voltmetre"]),
        ("physics_diagram", ["kuvvet diyagramı", "serbest cisim diyagramı", "vektör", "hareket grafiği"]),
        ("optics_diagram", ["mercek", "ayna", "ışın", "kırılma açısı"]),
        ("experimental_setup", ["deney düzeneği", "deney düzeneğinde"]),
        ("chemical_structure", ["molekül yapısı", "kimyasal bağ", "reaksiyon şeması"]),
        ("lewis_structure", ["lewis yapısı", "lewis gösterimi"]),
        ("periodic_table_fragment", ["periyodik tablo"])
    ];

    public Task<VisionRoutingDecision> DecideAsync(string questionText, string? originalVisualReference, CancellationToken ct = default)
    {
        var text = questionText ?? string.Empty;
        var hasReference = !string.IsNullOrWhiteSpace(originalVisualReference);

        foreach (var (visualType, keywords) in KeywordGroups)
        {
            var matched = keywords.FirstOrDefault(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
            {
                var confidence = hasReference ? 0.90m : 0.65m;
                var reason = $"Metin '{matched}' tetikleyicisini içeriyor" +
                    (hasReference ? " ve görsel referansı (OriginalVisualReference) dolu." : ".");
                return Task.FromResult(new VisionRoutingDecision(true, visualType, reason, confidence));
            }
        }

        if (hasReference)
        {
            return Task.FromResult(new VisionRoutingDecision(
                true, "mixed_visual_question",
                "Metinde tetikleyici kelime yok ama OriginalVisualReference dolu — dekoratif olabilir, düşük güvenle işaretlendi.",
                0.40m));
        }

        return Task.FromResult(new VisionRoutingDecision(
            false, null, "Metinde görsel tetikleyici kelime veya referans bulunamadı.", 0.85m));
    }
}
