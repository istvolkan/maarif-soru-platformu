using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Configuration;

public class SystemSettingsService(MaarifDbContext db, ISettingsReloader settingsProvider, SettingsSecretProtector secrets)
{
    public Task SetAsync(string key, string value, Guid? updatedByUserId, CancellationToken ct = default)
        => SetManyAsync(new Dictionary<string, string> { [key] = value }, updatedByUserId, ct);

    public async Task SetManyAsync(IReadOnlyDictionary<string, string> values, Guid? updatedByUserId, CancellationToken ct = default)
    {
        var keys = values.Keys.ToArray();
        var existing = await db.SystemSettings.Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, ct);
        foreach (var (key, value) in values)
        {
            if (!existing.TryGetValue(key, out var row))
            {
                row = new SystemSetting { Key = key };
                db.SystemSettings.Add(row);
            }
            row.Value = SettingsSecretProtector.IsSecret(key) ? secrets.Protect(key, value) : value;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByUserId = updatedByUserId;
        }
        // EF executes all writes in one transaction. Refresh exactly once after commit.
        await db.SaveChangesAsync(ct);
        settingsProvider.SignalReload();
    }
}
