using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MaarifPlatform.Application.Auth;
using MaarifPlatform.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MaarifPlatform.Infrastructure.Auth;

/// <summary>Rol claim'i olarak doğrudan <c>ClaimTypes.Role</c> + <c>UserRole.ToString()</c>
/// kullanılır — ASP.NET Core'un varsayılan RoleClaimType'ı ile eşleşir, [Authorize(Roles=...)]
/// için ayrıca bir TokenValidationParameters.RoleClaimType ayarına gerek yoktur. Bu, AppUser.Role
/// kolonundaki EF HasConversion&lt;string&gt;() dönüşümünden bağımsız, ayrı bir string temsilidir.</summary>
public class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    public JwtToken CreateToken(AppUser user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var expiresAt = DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            _options.Issuer, _options.Audience, claims,
            expires: expiresAt, signingCredentials: credentials);

        return new JwtToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
