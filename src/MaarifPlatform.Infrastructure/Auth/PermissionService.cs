using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Auth;

/// <summary>role_permissions tablosunun okuma/yazma tarafı — hem PermissionAuthorizationHandler
/// (her [Authorize(Policy=...)] denetiminde) hem de Admin/Permissions.razor (Yetkiler ekranı)
/// tarafından kullanılır. Admin rolü buraya HİÇ dahil değildir — her zaman koşulsuz yetkilidir
/// (bkz. HasPermissionAsync), kilitlenme riskini önlemek için.</summary>
public class PermissionService(MaarifDbContext db)
{
    public async Task<bool> HasPermissionAsync(UserRole role, Permission permission, CancellationToken ct = default)
    {
        if (role == UserRole.Admin)
        {
            return true;
        }

        return await db.RolePermissions.AnyAsync(rp => rp.Role == role && rp.Permission == permission, ct);
    }

    /// <summary>Yetkiler ekranındaki matrisi doldurmak için — Admin DIŞINDAKİ her rol × her
    /// Permission için mevcut (verilmiş/verilmemiş) durumu döner.</summary>
    public async Task<HashSet<(UserRole Role, Permission Permission)>> GetGrantedAsync(CancellationToken ct = default)
    {
        var rows = await db.RolePermissions.Select(rp => new { rp.Role, rp.Permission }).ToListAsync(ct);
        return rows.Select(r => (r.Role, r.Permission)).ToHashSet();
    }

    public async Task SetAsync(UserRole role, Permission permission, bool granted, CancellationToken ct = default)
    {
        var existing = await db.RolePermissions
            .FirstOrDefaultAsync(rp => rp.Role == role && rp.Permission == permission, ct);

        if (granted && existing is null)
        {
            db.RolePermissions.Add(new RolePermission { Role = role, Permission = permission });
            await db.SaveChangesAsync(ct);
        }
        else if (!granted && existing is not null)
        {
            db.RolePermissions.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
    }
}
