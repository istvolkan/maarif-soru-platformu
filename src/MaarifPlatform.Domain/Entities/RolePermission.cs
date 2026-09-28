using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Domain.Entities;

/// <summary>Bir (Role, Permission) satırının VARLIĞI o rolün o yetkiye sahip olduğu anlamına
/// gelir — yokluk = yetki yok. Admin rolü için hiç satır tutulmaz (PermissionAuthorizationHandler
/// Admin'i her zaman doğrudan geçirir). Admin/Kullanıcılar > Yetkiler ekranından yönetilir.</summary>
public class RolePermission : Entity
{
    public UserRole Role { get; set; }
    public Permission Permission { get; set; }
}
