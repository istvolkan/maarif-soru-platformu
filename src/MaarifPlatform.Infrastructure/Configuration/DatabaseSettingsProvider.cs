using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MaarifPlatform.Infrastructure.Configuration;

public interface ISettingsReloader
{
    void SignalReload();
}

public sealed class DatabaseSettingsProvider(string connectionString, SettingsSecretProtector secrets,
    ILogger<DatabaseSettingsProvider> logger) : ConfigurationProvider, ISettingsReloader
{
    private readonly object _gate = new();
    private bool _loaded;

    public override void Load()
    {
        lock (_gate)
        {
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            try
            {
                using var cmd = new NpgsqlCommand("SELECT \"Key\", \"Value\" FROM system_settings", conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) data[reader.GetString(0)] = reader.GetString(1);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable && !_loaded)
            {
                logger.LogWarning("system_settings is not created yet; apply database migrations.");
                return;
            }

            // Upgrade legacy plaintext secrets atomically. A concurrent setting edit wins.
            using var transaction = conn.BeginTransaction();
            foreach (var key in data.Keys.ToArray())
            {
                if (!SettingsSecretProtector.IsSecret(key)) continue;
                var value = data[key]!;
                if (SettingsSecretProtector.IsProtected(value))
                {
                    data[key] = secrets.Unprotect(key, value);
                    continue;
                }
                using var update = new NpgsqlCommand("UPDATE system_settings SET \"Value\" = @encrypted WHERE \"Key\" = @key AND \"Value\" = @old", conn, transaction);
                update.Parameters.AddWithValue("encrypted", secrets.Protect(key, value));
                update.Parameters.AddWithValue("key", key);
                update.Parameters.AddWithValue("old", value);
                update.ExecuteNonQuery();
            }
            transaction.Commit();
            var changed = data.Count != Data.Count || data.Any(pair => !Data.TryGetValue(pair.Key, out var old) || old != pair.Value);
            Data = data;
            _loaded = true;
            if (changed) OnReload();
        }
    }

    public void SignalReload() => Load();
}

public sealed class DatabaseSettingsSource(DatabaseSettingsProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}
