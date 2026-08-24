using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Infrastructure.Auth;

/// <summary>RBAC sisteminde açık self-registration olmadığından (bkz. AuthService), ilk Admin
/// kullanıcısı uygulama açılışında bir kez, boş bir `users` tablosu üzerinde otomatik seed
/// edilir — bu, Vision/Ai/Embeddings sağlayıcılarındaki "Local" dev-varsayılanı deseniyle aynı
/// ruhtadır (dış müdahale gerektirmeden çalışır durumda başlar). Tablo boşken config'te
/// Email/Password tanımlı DEĞİLSE sessizce atlamak yerine FIRLATILIR — aksi halde operatör
/// kimsenin giriş yapamadığı, kilitli bir veritabanıyla baş başa kalır.</summary>
public static class BootstrapAdminInitializer
{
    public static async Task EnsureSeededAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<MaarifDbContext>();
        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        var options = services.GetRequiredService<IOptions<BootstrapAdminOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.Email) || string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException(
                "users tablosu boş ve Auth:BootstrapAdmin:Email/Password tanımlı değil — " +
                "hiç kimse giriş yapamayacak. appsettings'te (dev) veya user-secrets/key vault'ta " +
                "(gerçek ortam) bir bootstrap admin tanımlayın.");
        }

        var passwordHasher = services.GetRequiredService<IPasswordHasher<AppUser>>();
        var admin = new AppUser
        {
            Name = options.Name,
            Email = options.Email,
            Role = UserRole.Admin
        };
        admin.PasswordHash = passwordHasher.HashPassword(admin, options.Password);

        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
    }
}
