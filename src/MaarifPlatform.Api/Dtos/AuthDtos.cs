using System.ComponentModel.DataAnnotations;

namespace MaarifPlatform.Api.Dtos;

public class LoginRequest
{
    [Required] public string Email { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
}

public record LoginResponse(string Token, DateTimeOffset ExpiresAt, string Name, string Role);
