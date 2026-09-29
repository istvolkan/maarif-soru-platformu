using System.Security.Claims;
using MaarifPlatform.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace MaarifPlatform.Infrastructure.Auth;

/// <summary>[Authorize(Roles="Admin,Editor")] gibi SABİT KODLANMIŞ rol listeleri yerine
/// [Authorize(Policy=nameof(Permission.X))] kullanan sayfalar için — DependencyInjection'daki
/// AddPolicy çağrıları her Permission değeri için bir PermissionRequirement üretir, buradaki TEK
/// handler hepsini karşılar (ASP.NET Core, requirement TİPİNE göre eşler, değerine göre değil).
/// role_permissions tablosu Admin/Kullanıcılar &gt; Yetkiler ekranından değiştirildiğinde bir
/// sonraki denetimde (yeniden giriş gerekmeden) hemen etkili olur — cookie'deki Role claim'i
/// değişmez, yalnızca o rolün hangi Permission'lara sahip olduğu DB'den taze okunur.</summary>
public sealed class PermissionRequirement(Permission permission) : IAuthorizationRequirement
{
    public Permission Permission { get; } = permission;
}

public class PermissionAuthorizationHandler(PermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        var roleClaim = context.User.FindFirst(ClaimTypes.Role)?.Value;
        if (roleClaim is null || !Enum.TryParse<UserRole>(roleClaim, ignoreCase: true, out var role))
        {
            return;
        }

        if (await permissions.HasPermissionAsync(role, requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
