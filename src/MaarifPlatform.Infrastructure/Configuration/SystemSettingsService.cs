using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Configuration;

/// <summary>Sprint 11 — Admin Ayarlar ekranının yazma yolu. Okuma tarafı gerekmez: DB'ye
/// yazılan her değer DatabaseSettingsProvider aracılığıyla IConfiguration'a zaten katman
/// olarak eklenmiştir, bu yüzden çağıran taraf mevcut etkin değeri doğrudan
/// IConfiguration["Ai:Provider"] gibi okuyabilir — burada ayrı bir Get metoduna gerek yok.</summary>
public class SystemSettingsService(MaarifDbContext db, DatabaseSettingsProvider settingsProvider)
{
    public async Task SetAsync(string key, string value, Guid? updatedByUserId, CancellationToken ct = default)
    {
        var existing = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing is null)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedByUserId = updatedByUserId
            });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedByUserId = updatedByUserId;
        }

        await db.SaveChangesAsync(ct);
        settingsProvider.SignalReload();
    }
}
