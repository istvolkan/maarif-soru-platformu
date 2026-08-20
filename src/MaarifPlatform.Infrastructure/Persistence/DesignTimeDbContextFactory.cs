using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MaarifPlatform.Infrastructure.Persistence;

/// <summary>`dotnet ef migrations` komutlarının Api projesini ayağa kaldırmadan çalışabilmesi
/// için tasarım-zamanı fabrikası. Bağlantı dizesi burada sadece migration üretimi içindir;
/// çalışma zamanında gerçek bağlantı Api'nin appsettings'inden gelir (bkz. Program.cs).</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MaarifDbContext>
{
    public MaarifDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MAARIF_DB_CONNECTION")
            ?? "Host=localhost;Port=5433;Database=maarif;Username=maarif;Password=maarif_dev_only";

        var optionsBuilder = new DbContextOptionsBuilder<MaarifDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npg => npg.UseVector());

        return new MaarifDbContext(optionsBuilder.Options);
    }
}
