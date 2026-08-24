using System.ComponentModel.DataAnnotations;

namespace MaarifPlatform.Api.Dtos;

public class CreateUserRequest
{
    [Required] public string Name { get; set; } = string.Empty;
    [Required] public string Email { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
    [Required] public string Role { get; set; } = string.Empty;
}

public record UserResponse(Guid Id, string Name, string Email, string Role, DateTimeOffset CreatedAt);
