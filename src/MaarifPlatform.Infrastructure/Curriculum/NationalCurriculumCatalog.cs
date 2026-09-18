namespace MaarifPlatform.Infrastructure.Curriculum;

/// <summary>MEB haftalık ders çizelgesindeki gerçek sınıf/ders adlarının statik listesi —
/// yalnızca Sınıf/Ders SEÇİM listelerini doldurmak için kullanılır, kazanım/tema/içerik verisi
/// DEĞİLDİR. Bu yüzden <see cref="CurriculumQueryService"/>'in "hiçbir kayıt admin onayı
/// olmadan kullanılamaz" ilkesini ihlal etmez — bir dersin burada listelenmesi o ders için
/// onaylı müfredat verisi olduğu anlamına gelmez (bkz. Questions/Generate.razor'daki
/// "doğrulanmış öğretim programı verisi bulunamadı" kontrolü, bu liste ile bağımsız çalışır).</summary>
public static class NationalCurriculumCatalog
{
    public static readonly IReadOnlyList<int> Grades = Enumerable.Range(1, 12).ToList();

    private static readonly Dictionary<int, string[]> CoursesByGrade = new()
    {
        [1] = ["Türkçe", "Matematik", "Hayat Bilgisi", "Görsel Sanatlar", "Müzik", "Beden Eğitimi ve Oyun"],
        [2] = ["Türkçe", "Matematik", "Hayat Bilgisi", "Yabancı Dil (İngilizce)", "Görsel Sanatlar", "Müzik", "Beden Eğitimi ve Oyun"],
        [3] = ["Türkçe", "Matematik", "Fen Bilimleri", "Hayat Bilgisi", "Yabancı Dil (İngilizce)", "Görsel Sanatlar", "Müzik", "Beden Eğitimi ve Oyun"],
        [4] = ["Türkçe", "Matematik", "Fen Bilimleri", "Sosyal Bilgiler", "Din Kültürü ve Ahlak Bilgisi", "Yabancı Dil (İngilizce)", "Görsel Sanatlar", "Müzik", "Beden Eğitimi ve Spor", "İnsan Hakları, Yurttaşlık ve Demokrasi", "Trafik Güvenliği"],
        [5] = ["Türkçe", "Matematik", "Fen Bilimleri", "Sosyal Bilgiler", "Din Kültürü ve Ahlak Bilgisi", "Yabancı Dil (İngilizce)", "Bilişim Teknolojileri ve Yazılım", "Görsel Sanatlar", "Müzik", "Beden Eğitimi ve Spor"],
        [6] = ["Türkçe", "Matematik", "Fen Bilimleri", "Sosyal Bilgiler", "Din Kültürü ve Ahlak Bilgisi", "Yabancı Dil (İngilizce)", "İkinci Yabancı Dil", "Bilişim Teknolojileri ve Yazılım", "Görsel Sanatlar", "Müzik", "Beden Eğitimi ve Spor"],
        [7] = ["Türkçe", "Matematik", "Fen Bilimleri", "Sosyal Bilgiler", "Din Kültürü ve Ahlak Bilgisi", "Yabancı Dil (İngilizce)", "İkinci Yabancı Dil", "Teknoloji ve Tasarım", "Görsel Sanatlar", "Müzik", "Beden Eğitimi ve Spor"],
        [8] = ["Türkçe", "Matematik", "Fen Bilimleri", "T.C. İnkılap Tarihi ve Atatürkçülük", "Din Kültürü ve Ahlak Bilgisi", "Yabancı Dil (İngilizce)", "Teknoloji ve Tasarım", "Görsel Sanatlar", "Müzik", "Beden Eğitimi ve Spor"],
        [9] = ["Türk Dili ve Edebiyatı", "Matematik", "Fizik", "Kimya", "Biyoloji", "Tarih", "Coğrafya", "Din Kültürü ve Ahlak Bilgisi", "Yabancı Dil (İngilizce)", "İkinci Yabancı Dil", "Görsel Sanatlar/Müzik", "Beden Eğitimi ve Spor", "Sağlık Bilgisi ve Trafik Kültürü", "Bilgi Kuramı"],
        [10] = ["Türk Dili ve Edebiyatı", "Matematik", "Fizik", "Kimya", "Biyoloji", "Tarih", "Coğrafya", "Felsefe", "Din Kültürü ve Ahlak Bilgisi", "Yabancı Dil (İngilizce)", "İkinci Yabancı Dil", "Beden Eğitimi ve Spor"],
        [11] = ["Türk Dili ve Edebiyatı", "Matematik", "Fizik", "Kimya", "Biyoloji", "Tarih", "Coğrafya", "Felsefe Grubu (Psikoloji, Sosyoloji, Mantık)", "Din Kültürü ve Ahlak Bilgisi", "Yabancı Dil (İngilizce)"],
        [12] = ["Türk Dili ve Edebiyatı", "Matematik", "Fizik", "Kimya", "Biyoloji", "Coğrafya", "T.C. İnkılap Tarihi ve Atatürkçülük", "Felsefe Grubu (Psikoloji, Sosyoloji, Mantık)", "Din Kültürü ve Ahlak Bilgisi", "Yabancı Dil (İngilizce)"],
    };

    public static IReadOnlyList<string> GetCoursesForGrade(int grade) =>
        CoursesByGrade.TryGetValue(grade, out var courses) ? courses : Array.Empty<string>();

    public static readonly IReadOnlyList<(string Value, string Label)> DocumentTypes =
    [
        ("Curriculum", "Öğretim Programı (Curriculum)"),
        ("Textbook", "Ders Kitabı (Textbook)"),
        ("Guide", "Kılavuz (Guide)")
    ];

    public static readonly IReadOnlyList<string> KnownAuthorities =
    [
        "MEB",
        "Talim ve Terbiye Kurulu Başkanlığı (TTKB)",
        "Ölçme, Değerlendirme ve Sınav Hizmetleri Genel Müdürlüğü (ÖDSGM)",
        "Millî Eğitim Bakanlığı Yayınları"
    ];

    public const string OtherAuthorityValue = "__diger__";
}
