namespace MaarifPlatform.Application.Intelligence;

/// <summary>Soru Üret'in (§16) oluşturduğu placeholder kitapların adlandırma şeması. 2026-10
/// kullanıcı kararıyla İKİ AYRI "Generated" kitap ailesi aynı (Grade,Subject) için var olabilir:
/// genel havuz (yeni üretilen her soru önce buraya düşer) ve Maarif Uyumlu onaylılar (editör
/// onayı sırasında, yalnızca PoolClassification=MaarifAligned olan sorular buraya TAŞINIR — bkz.
/// Pool.razor). SourceType+Grade+Subject ÜÇLÜSÜ bu ikisini ayırt etmeye YETMEZ (ikisi de
/// SourceType.Generated), bu yüzden TAM BAŞLIK eşleşmesi kullanılır
/// (GenerationOrchestrationService.FindOrCreatePlaceholderBookAsync, Pool.razor).
/// Başlıklar YALNIZCA kod tarafından üretilir, kullanıcı tarafından asla elle girilmez (Generated
/// tipi kitaplar Admin/Books.razor'ın manuel yükleme formunda hiç görünmez) — bu yüzden tam metin
/// eşleşmesi güvenlidir, ayrı bir ayırt edici kolon/migration gerektirmez.</summary>
public static class GeneratedBookNaming
{
    public static string GeneralPoolTitle(int grade, string subject) => $"AI Üretilen Sorular — {grade}. Sınıf {subject}";

    public static string MaarifAlignedTitle(int grade, string subject) => $"Maarif Uyumlu AI Soruları — {grade}. Sınıf {subject}";
}
