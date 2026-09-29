using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Infrastructure.Configuration;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace MaarifPlatform.Tests.Configuration;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAARIF_TEST_DB")))
            Skip = "Set MAARIF_TEST_DB to a PostgreSQL connection with permission to create a temporary database.";
    }
}

public class PostgresSettingsTests
{
    [PostgresFact]
    public async Task Migration_LegacyEncryption_CrossHostRefresh_AndAtomicBatch()
    {
        var name = "maarif_security_test_" + Guid.NewGuid().ToString("N");
        var baseConnection = Environment.GetEnvironmentVariable("MAARIF_TEST_DB")!;
        await using var admin = new NpgsqlConnection(baseConnection);
        await admin.OpenAsync();
        // The identifier is generated here exclusively from a fixed prefix and hex GUID.
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin))
            await create.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(baseConnection) { Database = name, Pooling = false }.ConnectionString;
        try
        {
            var options = new DbContextOptionsBuilder<MaarifDbContext>()
                .UseNpgsql(connection, npg => npg.UseVector()).Options;
            await using var db = new MaarifDbContext(options);
            await db.Database.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
            db.SystemSettings.Add(new SystemSetting { Key = "Ai:Anthropic:ApiKey", Value = "legacy-secret" });
            await db.SaveChangesAsync();

            var protector = new SettingsSecretProtector(Convert.ToBase64String(new byte[32]));
            var first = new DatabaseSettingsProvider(connection, protector, NullLogger<DatabaseSettingsProvider>.Instance);
            var second = new DatabaseSettingsProvider(connection, protector, NullLogger<DatabaseSettingsProvider>.Instance);
            first.Load();
            second.Load();
            Assert.True(first.TryGet("Ai:Anthropic:ApiKey", out var value));
            Assert.Equal("legacy-secret", value);
            db.ChangeTracker.Clear();
            var row = await db.SystemSettings.SingleAsync();
            Assert.True(SettingsSecretProtector.IsProtected(row.Value));

            var service = new SystemSettingsService(db, first, protector);
            await service.SetManyAsync(new Dictionary<string, string>
            {
                ["Ai:Provider"] = "Anthropic", ["Ai:Anthropic:ApiKey"] = "new-secret"
            }, null);
            second.SignalReload();
            Assert.True(second.TryGet("Ai:Anthropic:ApiKey", out value));
            Assert.Equal("new-secret", value);

            // A failing batch must not partially persist another setting.
            await Assert.ThrowsAsync<DbUpdateException>(() => service.SetManyAsync(new Dictionary<string, string>
            {
                ["Ai:Provider"] = "Local", ["Invalid:Oversized"] = new string('x', 2001)
            }, null));
            db.ChangeTracker.Clear();
            Assert.Equal("Anthropic", (await db.SystemSettings.SingleAsync(s => s.Key == "Ai:Provider")).Value);

            // A missing table during runtime is an error, not a fallback to empty/default settings.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE system_settings RENAME TO hidden_settings");
            Assert.Throws<PostgresException>(() => second.SignalReload());
            Assert.True(second.TryGet("Ai:Anthropic:ApiKey", out value));
            Assert.Equal("new-secret", value);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name}", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
