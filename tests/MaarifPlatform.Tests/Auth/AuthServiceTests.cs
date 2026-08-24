using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Auth;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Tests.Auth;

public class AuthServiceTests
{
    private static readonly IPasswordHasher<AppUser> Hasher = new PasswordHasher<AppUser>();

    private static MaarifDbContext BuildDb() =>
        new InMemoryMaarifDbContext(new DbContextOptionsBuilder<MaarifDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static JwtTokenService BuildTokenService() =>
        new(Options.Create(new JwtOptions
        {
            Issuer = "test", Audience = "test",
            SigningKey = "unit-test-signing-key-at-least-32-bytes-long!!",
            ExpiryMinutes = 60
        }));

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsToken()
    {
        await using var db = BuildDb();
        var user = new AppUser { Name = "Ayşe", Email = "ayse@maarif.local", Role = UserRole.Editor };
        user.PasswordHash = Hasher.HashPassword(user, "gecerli-parola");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, Hasher, BuildTokenService());
        var result = await service.LoginAsync("ayse@maarif.local", "gecerli-parola");

        Assert.NotNull(result);
        Assert.Equal(user.Id, result!.User.Id);
        Assert.False(string.IsNullOrWhiteSpace(result.Token.Value));
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsNull()
    {
        await using var db = BuildDb();
        var user = new AppUser { Name = "Ayşe", Email = "ayse@maarif.local", Role = UserRole.Editor };
        user.PasswordHash = Hasher.HashPassword(user, "gecerli-parola");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, Hasher, BuildTokenService());
        var result = await service.LoginAsync("ayse@maarif.local", "yanlis-parola");

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_UnknownEmail_ReturnsNull()
    {
        await using var db = BuildDb();

        var service = new AuthService(db, Hasher, BuildTokenService());
        var result = await service.LoginAsync("yok@maarif.local", "herhangi");

        Assert.Null(result);
    }
}
