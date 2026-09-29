using MaarifPlatform.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace MaarifPlatform.Infrastructure.Auth;

public static class AuthorizationPolicies
{
    public const string Admin = "AdminOnly";
    public const string QuestionEdit = "QuestionEdit";

    public static void Configure(AuthorizationOptions options)
    {
        foreach (var permission in Enum.GetValues<Permission>())
            options.AddPolicy(permission.ToString(), policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission)));

        options.AddPolicy(Admin, policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));
        options.AddPolicy(QuestionEdit, policy => policy.RequireAuthenticatedUser()
            .RequireRole("Admin", "Editor").AddRequirements(new PermissionRequirement(Permission.QuestionPoolAccess)));
    }
}
