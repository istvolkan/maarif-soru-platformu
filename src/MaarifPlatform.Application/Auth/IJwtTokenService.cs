using MaarifPlatform.Domain.Entities;

namespace MaarifPlatform.Application.Auth;

public sealed record JwtToken(string Value, DateTimeOffset ExpiresAt);

public interface IJwtTokenService
{
    JwtToken CreateToken(AppUser user);
}
