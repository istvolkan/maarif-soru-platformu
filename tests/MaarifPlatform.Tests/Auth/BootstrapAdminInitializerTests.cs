using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Auth;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Tests.Auth;

public class BootstrapAdminInitializerTests
{
    private static ServiceProvider BuildProvider(BootstrapAdminOptions options)
    {
        var db = new InMemoryMaarifDbContext(new DbContextOptionsBuilder<MaarifDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        var services = new ServiceCollection();
        services.AddSingleton<MaarifDbContext>(db);
        services.AddSingleton(Options.Create(options));
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task EnsureSeededAsync_EmptyUsersTable_CreatesAdmin()
    {
        await using var provider = BuildProvider(new BootstrapAdminOptions
        {
            Email = "admin@maarif.local", Password = "bootstrap-parola", Name = "Kök Yönetici"
        });

        await BootstrapAdminInitializer.EnsureSeededAsync(provider);

        var db = provider.GetRequiredService<MaarifDbContext>();
        var admin = await db.Users.SingleAsync();
        Assert.Equal("admin@maarif.local", admin.Email);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.False(string.IsNullOrWhiteSpace(admin.PasswordHash));
    }

    [Fact]
    public async Task EnsureSeededAsync_NonEmptyTable_NoOp()
    {
        await using var provider = BuildProvider(new BootstrapAdminOptions
        {
            Email = "admin@maarif.local", Password = "bootstrap-parola"
        });
        var db = provider.GetRequiredService<MaarifDbContext>();
        db.Users.Add(new AppUser { Name = "Mevcut", Email = "existing@maarif.local", Role = UserRole.Teacher, PasswordHash = "x" });
        await db.SaveChangesAsync();

        await BootstrapAdminInitializer.EnsureSeededAsync(provider);

        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task EnsureSeededAsync_EmptyTableAndNoConfig_Throws()
    {
        await using var provider = BuildProvider(new BootstrapAdminOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => BootstrapAdminInitializer.EnsureSeededAsync(provider));
    }
}
