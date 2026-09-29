using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Domain.Entities;

public class AppUser : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
}
