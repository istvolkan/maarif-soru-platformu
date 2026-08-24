using MaarifPlatform.Api.Dtos;
using MaarifPlatform.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MaarifPlatform.Api.Controllers;

/// <summary>Sprint 7 Auth/RBAC. Açık kayıt yok — kullanıcılar bootstrap admin'den veya
/// Admin-only POST /api/users'tan türer (bkz. UsersController).</summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request.Email, request.Password, ct);
        if (result is null)
        {
            return Unauthorized("E-posta veya parola hatalı.");
        }

        return new LoginResponse(result.Token.Value, result.Token.ExpiresAt, result.User.Name, result.User.Role.ToString());
    }
}
