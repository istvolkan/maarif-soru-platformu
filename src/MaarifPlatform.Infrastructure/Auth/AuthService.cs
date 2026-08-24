using MaarifPlatform.Application.Auth;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Auth;

public sealed record AuthResult(JwtToken Token, AppUser User);

/// <summary>Sprint 7 login orkestrasyonu. Kendi kullanıcı kaydı (self-registration) YOK —
/// RBAC sistemine açık kayıt eklenmedi; kullanıcılar ilk admin'den (bkz.
/// BootstrapAdminInitializer) veya Admin-only POST /api/users'tan türer.</summary>
public class AuthService(MaarifDbContext db, IPasswordHasher<AppUser> passwordHasher, IJwtTokenService tokenService)
{
    public async Task<AuthResult?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            return null;
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return null;
        }

        return new AuthResult(tokenService.CreateToken(user), user);
    }
}
