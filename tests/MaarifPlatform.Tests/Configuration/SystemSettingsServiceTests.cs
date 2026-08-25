using MaarifPlatform.Infrastructure.Configuration;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Tests.Configuration;

public class SystemSettingsServiceTests
{
    private static MaarifDbContext BuildDb() =>
        new InMemoryMaarifDbContext(new DbContextOptionsBuilder<MaarifDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // Gerçek bir Postgres olmadan Load() sessizce boş veriye düşer (bkz. DatabaseSettingsProvider'daki
    // try/catch) — bu testler yalnızca SystemSettingsService'in upsert mantığını doğrular, canlı
    // yeniden yükleme mekanizmasının kendisini değil (o, gerçek DB ile E2E'de doğrulanır).
    private static DatabaseSettingsProvider BuildSettingsProvider() => new("Host=unused;Database=unused");

    [Fact]
    public async Task SetAsync_NewKey_InsertsRow()
    {
        await using var db = BuildDb();
        var service = new SystemSettingsService(db, BuildSettingsProvider());
        var userId = Guid.NewGuid();

        await service.SetAsync("Ai:Provider", "Anthropic", userId);

        var row = await db.SystemSettings.SingleAsync();
        Assert.Equal("Ai:Provider", row.Key);
        Assert.Equal("Anthropic", row.Value);
        Assert.Equal(userId, row.UpdatedByUserId);
    }

    [Fact]
    public async Task SetAsync_ExistingKey_UpdatesValueInPlace()
    {
        await using var db = BuildDb();
        var service = new SystemSettingsService(db, BuildSettingsProvider());

        await service.SetAsync("Ai:Provider", "Local", Guid.NewGuid());
        await service.SetAsync("Ai:Provider", "Anthropic", Guid.NewGuid());

        Assert.Equal(1, await db.SystemSettings.CountAsync());
        var row = await db.SystemSettings.SingleAsync();
        Assert.Equal("Anthropic", row.Value);
    }

    [Fact]
    public async Task SetAsync_DifferentKeys_InsertsSeparateRows()
    {
        await using var db = BuildDb();
        var service = new SystemSettingsService(db, BuildSettingsProvider());

        await service.SetAsync("Ai:Provider", "Anthropic", null);
        await service.SetAsync("Judge:SecondaryProvider", "OpenAI", null);

        Assert.Equal(2, await db.SystemSettings.CountAsync());
    }
}
