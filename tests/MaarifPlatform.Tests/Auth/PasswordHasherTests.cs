using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace MaarifPlatform.Tests.Auth;

public class PasswordHasherTests
{
    private static readonly IPasswordHasher<AppUser> Hasher = new PasswordHasher<AppUser>();

    private static AppUser BuildUser() => new() { Name = "Test", Email = "test@maarif.local", Role = UserRole.Teacher };

    [Fact]
    public void HashPassword_ThenVerify_CorrectPasswordSucceeds()
    {
        var user = BuildUser();
        user.PasswordHash = Hasher.HashPassword(user, "correct-horse-battery-staple");

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, "correct-horse-battery-staple");

        Assert.Equal(PasswordVerificationResult.Success, result);
    }

    [Fact]
    public void HashPassword_ThenVerify_WrongPasswordFails()
    {
        var user = BuildUser();
        user.PasswordHash = Hasher.HashPassword(user, "correct-horse-battery-staple");

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, "wrong-password");

        Assert.Equal(PasswordVerificationResult.Failed, result);
    }
}
