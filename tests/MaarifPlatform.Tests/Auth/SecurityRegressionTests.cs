using System.Security.Claims;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Auth;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Tests.Auth;

public class SecurityRegressionTests
{
    private static MaarifDbContext Db() => new InMemoryMaarifDbContext(
        new DbContextOptionsBuilder<MaarifDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly PasswordHasher<AppUser> Hasher = new();
    private static AuthService Auth(MaarifDbContext db) => new(db, Hasher,
        new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('x', 64) })));
    private static ClaimsPrincipal Principal(AppUser user) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Role, user.Role.ToString()),
        new Claim(SessionValidator.StampClaim, user.SecurityStamp)
    }, "test"));

    [Fact]
    public async Task FifthFailedAttempt_LocksAccountAcrossServiceInstances()
    {
        await using var db = Db();
        var user = new AppUser { Email = "a@example.com" };
        user.PasswordHash = Hasher.HashPassword(user, "correct-password");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        for (var i = 0; i < 5; i++) Assert.Null(await Auth(db).ValidateCredentialsAsync(user.Email, "wrong"));
        Assert.Null(await Auth(db).ValidateCredentialsAsync(user.Email, "correct-password"));
        user.LockoutEnd = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        Assert.NotNull(await Auth(db).ValidateCredentialsAsync(user.Email, "correct-password"));
        Assert.Equal(0, user.FailedLoginCount);
    }

    [Fact]
    public async Task RoleAndPasswordChangesAndDeletion_RevokePreviousSessions()
    {
        await using var db = Db();
        var users = new UserManagementService(db, Hasher);
        var user = await users.CreateAsync("User", "u@example.com", "original-password", "Admin");
        var validator = new SessionValidator(db);
        var before = Principal(user);
        Assert.True(await validator.IsValidAsync(before));
        await users.UpdateAsync(user.Id, Guid.NewGuid(), user.Name, user.Email, "Editor", null);
        Assert.False(await validator.IsValidAsync(before));
        var afterRole = Principal(user);
        Assert.True(await validator.IsValidAsync(afterRole));
        await users.UpdateAsync(user.Id, Guid.NewGuid(), user.Name, user.Email, "Editor", "changed-password");
        Assert.False(await validator.IsValidAsync(afterRole));
        var afterPassword = Principal(user);
        await users.DeleteAsync(user.Id, Guid.NewGuid());
        Assert.False(await validator.IsValidAsync(afterPassword));
    }

    [Fact]
    public async Task LegacyPrincipalWithoutStamp_IsRejected()
    {
        await using var db = Db();
        var user = new AppUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, "Admin")
        }, "test"));
        Assert.False(await new SessionValidator(db).IsValidAsync(principal));
    }

    [Theory]
    [InlineData(Permission.QuestionGenerationAccess)]
    [InlineData(Permission.ReferenceDocumentUpload)]
    public async Task RemovingPermission_DeniesEditorImmediately(Permission permission)
    {
        await using var db = Db();
        var permissions = new PermissionService(db);
        await permissions.SetAsync(UserRole.Editor, permission, true);
        var principal = Principal(new AppUser { Role = UserRole.Editor });
        var handler = new PermissionAuthorizationHandler(permissions);
        var requirement = new PermissionRequirement(permission);
        var allowed = new AuthorizationHandlerContext(new[] { requirement }, principal, null);
        await handler.HandleAsync(allowed);
        Assert.True(allowed.HasSucceeded);
        await permissions.SetAsync(UserRole.Editor, permission, false);
        var denied = new AuthorizationHandlerContext(new[] { requirement }, principal, null);
        await handler.HandleAsync(denied);
        Assert.False(denied.HasSucceeded);
    }

    [Theory]
    [InlineData("https://evil.example", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("/%2fevil.example", "/")]
    [InlineData("/%5cevil.example", "/")]
    [InlineData("/questions?x=1", "/questions?x=1")]
    [InlineData(null, "/")]
    public void LoginRedirect_AllowsOnlyLocalPaths(string? input, string expected)
        => Assert.Equal(expected, LocalReturnUrl.Normalize(input));

    [Fact]
    public async Task LoginRateLimit_RejectsEleventhRequestButAllowsOtherEndpoints()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLoginRateLimiting();
        using var provider = services.BuildServiceProvider();
        using var limiter = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter!;
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/auth/login";
        for (var i = 0; i < 10; i++)
        {
            using var lease = await limiter.AcquireAsync(context);
            Assert.True(lease.IsAcquired);
        }
        context.Request.Path = "/API/AUTH/LOGIN/";
        using var rejected = await limiter.AcquireAsync(context);
        Assert.False(rejected.IsAcquired);
        context.Request.Path = "/health";
        using var other = await limiter.AcquireAsync(context);
        Assert.True(other.IsAcquired);
    }
}
