using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Tests.Auth;

public class JwtTokenServiceTests
{
    private static JwtTokenService BuildService(int expiryMinutes = 60) =>
        new(Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = "unit-test-signing-key-at-least-32-bytes-long!!",
            ExpiryMinutes = expiryMinutes
        }));

    [Fact]
    public void CreateToken_ValidUser_EmbedsSubEmailRoleClaims()
    {
        var user = new AppUser { Name = "Test", Email = "test@maarif.local", Role = UserRole.Editor };
        var service = BuildService();

        var token = service.CreateToken(user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);
        Assert.Equal(user.Id.ToString(), jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(user.Email, jwt.Claims.Single(c => c.Type == ClaimTypes.Email).Value);
        Assert.Equal("Editor", jwt.Claims.Single(c => c.Type == ClaimTypes.Role).Value);
    }

    [Fact]
    public void CreateToken_ExpiryMinutesConfigured_SetsMatchingExpiry()
    {
        var user = new AppUser { Name = "Test", Email = "test@maarif.local", Role = UserRole.Admin };
        var service = BuildService(expiryMinutes: 15);

        var before = DateTime.UtcNow;
        var token = service.CreateToken(user);

        Assert.True(token.ExpiresAt.UtcDateTime >= before.AddMinutes(15).AddSeconds(-5));
        Assert.True(token.ExpiresAt.UtcDateTime <= before.AddMinutes(15).AddSeconds(5));
    }
}
