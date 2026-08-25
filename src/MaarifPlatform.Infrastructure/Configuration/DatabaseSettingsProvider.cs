using Microsoft.Extensions.Configuration;
using Npgsql;

namespace MaarifPlatform.Infrastructure.Configuration;

/// <summary>Sprint 11 — `system_settings` tablosundaki satırları appsettings.json'ın ÜSTÜNE
/// katman olarak ekler (DB değeri varsa kazanır, yoksa appsettings.json fallback kalır). Ham
/// Npgsql ile konuşur, EF/DI ÜZERİNDEN DEĞİL — IConfiguration kaynakları DI konteyneri henüz
/// kurulmadan inşa edildiği için (chicken-and-egg). İlk açılışta migration henüz çalışmamış
/// olabilir; bu durumda sessizce boş veriye düşer (appsettings.json geçerli kalır).</summary>
public sealed class DatabaseSettingsProvider(string connectionString) : ConfigurationProvider
{
    public override void Load()
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand("SELECT \"Key\", \"Value\" FROM system_settings", conn);
            using var reader = cmd.ExecuteReader();

            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                data[reader.GetString(0)] = reader.GetString(1);
            }

            Data = data;
        }
        catch (Exception)
        {
            Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Bir ayar kaydedildikten sonra çağrılır — Load()'u yeniden çalıştırır ve
    /// OnReload() ile IConfiguration'ın change token'ını tetikler. IOptionsMonitor&lt;T&gt;
    /// zaten bu mekanizmayı dinliyor, ek bir kablo bağlamaya gerek yok.</summary>
    public void SignalReload()
    {
        Load();
        OnReload();
    }
}

public sealed class DatabaseSettingsSource(DatabaseSettingsProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}
