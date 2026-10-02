using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Curriculum;

public sealed record LearningOutcomeOption(Guid Id, string Code, string Description, Guid? ThemeId, string? ThemeName);
public sealed record ContentFrameworkOption(Guid Id, string Name);
public sealed record FieldSkillOption(Guid Id, string Code, string Name);

/// <summary>Cascading Soru Üret formunun (Sınıf→Ders→Kazanım Kodu→[Tema otomatik gösterilir]→
/// İçerik Çerçevesi→Beceri) TEK veri kaynağı — her metot yalnızca
/// <see cref="ApprovalStatus.Approved"/> kayıtları döner, Draft/Rejected hiçbir zaman dropdown'a
/// sızmaz (bkz. Admin/CurriculumReview.razor). Kazanım seçimi Tema'dan ÖNCE gelir: Tema→Kazanım
/// filtrelemesi, aynı anlama gelip yalnızca büyük/küçük harfle ayrışan kopya Tema satırlarında
/// (bkz. MergeDuplicateCasedThemes migration'ı) kazanımların yanlış tema altına dağılmasına ve
/// kullanıcının seçtiği temanın boş görünmesine yol açıyordu — Kazanım, Grade+Subject'e göre tek
/// bir düz listeden seçilir, Tema o kazanımın zaten bağlı olduğu satırdan salt bilgi amaçlı gösterilir.</summary>
public class CurriculumQueryService(MaarifDbContext db)
{
    public async Task<IReadOnlyList<int>> GetGradesAsync(CancellationToken ct = default) =>
        await db.Themes
            .Where(t => t.ApprovalStatus == ApprovalStatus.Approved)
            .Select(t => t.Grade)
            .Distinct()
            .OrderBy(g => g)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<string>> GetSubjectsAsync(int grade, CancellationToken ct = default) =>
        await db.Themes
            .Where(t => t.ApprovalStatus == ApprovalStatus.Approved && t.Grade == grade)
            .Select(t => t.Subject)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LearningOutcomeOption>> GetLearningOutcomesAsync(int grade, string subject, CancellationToken ct = default) =>
        await db.LearningOutcomes
            .Where(lo => lo.Grade == grade && lo.Subject == subject && lo.ApprovalStatus == ApprovalStatus.Approved)
            .OrderBy(lo => lo.Code)
            .Select(lo => new LearningOutcomeOption(lo.Id, lo.Code, lo.Description, lo.ThemeId, lo.Theme == null ? null : lo.Theme.Name))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ContentFrameworkOption>> GetContentFrameworksAsync(Guid learningOutcomeId, CancellationToken ct = default) =>
        await db.ContentFrameworks
            .Where(cf => cf.LearningOutcomeId == learningOutcomeId && cf.ApprovalStatus == ApprovalStatus.Approved)
            .OrderBy(cf => cf.Name)
            .Select(cf => new ContentFrameworkOption(cf.Id, cf.Name))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FieldSkillOption>> GetFieldSkillsAsync(string subject, CancellationToken ct = default) =>
        await db.FieldSkills
            .Where(f => f.Subject == subject && f.ApprovalStatus == ApprovalStatus.Approved)
            .OrderBy(f => f.Code)
            .Select(f => new FieldSkillOption(f.Id, f.Code, f.Name))
            .ToListAsync(ct);

    /// <summary>§8 hallucination control kapısı — Soru Üret formu bu false dönerse üretimi hiç
    /// başlatmaz, "Bu kombinasyon için doğrulanmış öğretim programı verisi bulunamadı." gösterir.</summary>
    public async Task<bool> HasApprovedCurriculumAsync(int grade, string subject, CancellationToken ct = default) =>
        await db.Themes.AnyAsync(t => t.Grade == grade && t.Subject == subject && t.ApprovalStatus == ApprovalStatus.Approved, ct);
}
