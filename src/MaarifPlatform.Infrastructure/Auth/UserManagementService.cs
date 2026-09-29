using System.ComponentModel.DataAnnotations;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Auth;

public class UserManagementService(MaarifDbContext db, IPasswordHasher<AppUser> hasher)
{
    public async Task<AppUser> CreateAsync(string name, string email, string password, string roleName, CancellationToken ct = default)
    {
        var role = Validate(name, email, roleName, password);
        email = email.Trim();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new InvalidOperationException("Bu e-posta adresi zaten kullanılıyor.");
        var user = new AppUser { Name = name.Trim(), Email = email, Role = role };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateAsync(Guid id, Guid actorId, string name, string email, string roleName, string? password, CancellationToken ct = default)
    {
        var role = Validate(name, email, roleName, string.IsNullOrWhiteSpace(password) ? null : password);
        if (id == actorId && role != UserRole.Admin)
            throw new InvalidOperationException("Kendi yönetici rolünüzü kaldıramazsınız.");
        email = email.Trim();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new InvalidOperationException("Kullanıcı bulunamadı.");
        if (await db.Users.AnyAsync(u => u.Id != id && u.Email == email, ct))
            throw new InvalidOperationException("Bu e-posta adresi zaten kullanılıyor.");
        user.Name = name.Trim();
        user.Email = email;
        user.Role = role;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        if (!string.IsNullOrWhiteSpace(password))
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        if (id == actorId) throw new InvalidOperationException("Kendi hesabınızı silemezsiniz.");
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return;
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
    }

    private static UserRole Validate(string name, string email, string roleName, string? password)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || email.Length > 320 || !new EmailAddressAttribute().IsValid(email))
            throw new InvalidOperationException("Geçerli ad ve e-posta girin.");
        if (!Enum.TryParse<UserRole>(roleName, true, out var role) || !Enum.IsDefined(role))
            throw new InvalidOperationException("Geçersiz rol.");
        if (password is not null && (password.Length < 12 || password.Length > 1024))
            throw new InvalidOperationException("Parola 12–1024 karakter olmalıdır.");
        return role;
    }
}
