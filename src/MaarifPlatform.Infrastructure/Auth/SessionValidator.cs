using System.Security.Claims;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Auth;

public class SessionValidator(MaarifDbContext db)
{
    public const string StampClaim = "session_stamp";

    public async Task<bool> IsValidAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id))
            return false;

        var stamp = principal.FindFirst(StampClaim)?.Value;
        var role = principal.FindFirst(ClaimTypes.Role)?.Value;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
        return user is not null && stamp == user.SecurityStamp && role == user.Role.ToString();
    }
}
