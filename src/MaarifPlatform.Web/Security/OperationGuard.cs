using MaarifPlatform.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;

namespace MaarifPlatform.Web.Security;

// Circuit principals outlive HTTP requests: validate against the database before each action.
public sealed class OperationGuard(AuthenticationStateProvider auth, IServiceScopeFactory scopes)
{
    public async Task RequireAsync(string policy)
    {
        var principal = (await auth.GetAuthenticationStateAsync()).User;
        await using var scope = scopes.CreateAsyncScope();
        if (!await scope.ServiceProvider.GetRequiredService<SessionValidator>().IsValidAsync(principal) ||
            !(await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
                .AuthorizeAsync(principal, null, policy)).Succeeded)
            throw new UnauthorizedAccessException("Oturum veya işlem yetkisi geçersiz. Yeniden giriş yapın.");
    }
}
