using MaarifPlatform.Application.Auth;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Auth;

public sealed record AuthResult(JwtToken Token, AppUser User);

public class AuthService(MaarifDbContext db, IPasswordHasher<AppUser> passwordHasher, IJwtTokenService tokenService)
{
    private static readonly AppUser DummyUser = new();
    private static readonly string DummyHash = new PasswordHasher<AppUser>().HashPassword(DummyUser, Guid.NewGuid().ToString());

    public async Task<AuthResult?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await ValidateCredentialsAsync(email, password, ct);
        return user is null ? null : new AuthResult(tokenService.CreateToken(user), user);
    }

    public async Task<AppUser?> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default)
    {
        if (email.Length > 320 || password.Length > 1024) return null;
        email = email.Trim();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
            if (user is null)
            {
                passwordHasher.VerifyHashedPassword(DummyUser, DummyHash, password);
                return null;
            }
            if (user.LockoutEnd > DateTimeOffset.UtcNow) return null;
            if (user.LockoutEnd is not null)
            {
                user.FailedLoginCount = 0;
                user.LockoutEnd = null;
            }
            var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (result == PasswordVerificationResult.Failed)
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= 5) user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
            }
            else
            {
                user.FailedLoginCount = 0;
                user.LockoutEnd = null;
                if (result == PasswordVerificationResult.SuccessRehashNeeded)
                    user.PasswordHash = passwordHasher.HashPassword(user, password);
            }
            try
            {
                await db.SaveChangesAsync(ct);
                return result == PasswordVerificationResult.Failed ? null : user;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another host changed the account/attempt counter. Re-read; never lose failures.
                db.Entry(user).State = EntityState.Detached;
            }
        }
        return null;
    }
}
