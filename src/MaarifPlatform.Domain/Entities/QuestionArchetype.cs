using Pgvector;

namespace MaarifPlatform.Domain.Entities;

/// <summary>§45/§46/§54 Question Archetype — Question Intelligence Engine Faz 3. "Pattern
/// Library" (§46) ve "Archetype" (§45) kavramları spec'te ayrı bölümler olarak anlatılsa da aynı
/// şeyi tarif ediyorlar (birden fazla soru/kitapta gözlemlenen soyut bir soru kalıbı) — bilinçli
/// olarak TEK bir entity'de birleştirildi, Pattern+Archetype ayrımı mevcut kod tabanında bir
/// karşılığı olmayan gereksiz bir ikilem olurdu.
///
/// Kümeye atama LLM'e SORULMAZ — <see cref="CentroidEmbedding"/>'e karşı deterministik
/// en-yakın-komşu (nearest-centroid) aramasıyla yapılır (bkz. ArchetypeClusteringService,
/// MultipleChoiceOptionPolicy/GenerationBlueprintBuilder'daki "LLM'e sorma, kodda hesapla"
/// felsefesiyle aynı). Yeni bir üye eklendikçe merkez ağırlıklı ortalamayla güncellenir —
/// kümeler zamanla, ekstra bir LLM çağrısı olmadan netleşir.
///
/// <see cref="QualityScore"/> bilinçli olarak nullable ve bu fazda HİÇ doldurulmuyor: o veri
/// Transform/Judge aşamasından gelir (bkz. QuestionDna.QualityScore), DNA analizi ise yalnızca
/// Analysis aşamasında çalışır — ileride bir fazda (Faz 7 feedback döngüsüyle birlikte) Transform
/// tamamlanan üyelerden geriye dönük zenginleştirilebilir.</summary>
public class QuestionArchetype : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public int GradeRangeMin { get; set; }
    public int GradeRangeMax { get; set; }
    public string? VisualType { get; set; }

    // §54 Kitaplar Arası Öğrenme — sıklık TEK BAŞINA kalite anlamına gelmez, bu yüzden hem toplam
    // üye sayısı hem de BAĞIMSIZ kitap sayısı ayrı tutulur (aynı kitaptan 50 soru, 5 farklı
    // kitaptan 5 soru kadar güvenilir bir sinyal değildir).
    public int SourceSupportCount { get; set; }
    public int DistinctBookSupportCount { get; set; }

    // Üye soruların MaarifAlignmentScore'larının ağırlıklı ortalaması (bkz. ArchetypeClusteringService).
    public decimal? MaarifAffinity { get; set; }

    // Bilinçli olarak bu fazda hiç doldurulmuyor — bkz. sınıf doc'u.
    public decimal? QualityScore { get; set; }

    public Vector CentroidEmbedding { get; set; } = null!;

    public ICollection<QuestionArchetypeMember> Members { get; set; } = new List<QuestionArchetypeMember>();
}

/// <summary>Bir <see cref="QuestionArchetype"/>'ın hangi somut sorulardan (hangi kitaplardan)
/// oluştuğunu izleyen üyelik kaydı — §54'teki "Pattern P-119, Kitap A → 14 kez" gibi kitap bazlı
/// sayımı BookId join'siz yapabilmek için BookId denormalize edilir (ReferenceChunk/QuestionEmbedding'
/// deki aynı desen).</summary>
public class QuestionArchetypeMember : Entity
{
    public Guid ArchetypeId { get; set; }
    public QuestionArchetype? Archetype { get; set; }

    public Guid QuestionVersionId { get; set; }
    public QuestionVersion? QuestionVersion { get; set; }

    public Guid BookId { get; set; }
}
