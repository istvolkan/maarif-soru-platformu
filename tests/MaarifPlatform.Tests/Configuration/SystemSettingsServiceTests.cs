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

    private sealed class Reloader : ISettingsReloader
    {
        public int Calls { get; private set; }
        public void SignalReload() => Calls++;
    }
    private static SettingsSecretProtector Protector() => new(Convert.ToBase64String(new byte[32]));

    [Fact]
    public async Task Batch_EncryptsSecretsAndReloadsOnce()
    {
        await using var db = BuildDb();
        var reloader = new Reloader();
        var protector = Protector();
        var service = new SystemSettingsService(db, reloader, protector);
        await service.SetManyAsync(new Dictionary<string, string>
        {
            ["Ai:Provider"] = "Anthropic",
            ["Ai:Anthropic:ApiKey"] = "test-secret"
        }, null);
        Assert.Equal(1, reloader.Calls);
        var row = await db.SystemSettings.SingleAsync(s => s.Key.EndsWith(":ApiKey"));
        Assert.DoesNotContain("test-secret", row.Value);
        Assert.Equal("test-secret", protector.Unprotect(row.Key, row.Value));
    }

    [Fact]
    public async Task SetAsync_NewKey_InsertsRow()
    {
        await using var db = BuildDb();
        var service = new SystemSettingsService(db, new Reloader(), Protector());
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
        var service = new SystemSettingsService(db, new Reloader(), Protector());

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
        var service = new SystemSettingsService(db, new Reloader(), Protector());

        await service.SetAsync("Ai:Provider", "Anthropic", null);
        await service.SetAsync("Judge:SecondaryProvider", "OpenAI", null);

        Assert.Equal(2, await db.SystemSettings.CountAsync());
    }
}
