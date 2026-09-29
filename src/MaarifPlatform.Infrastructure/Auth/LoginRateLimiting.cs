using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace MaarifPlatform.Infrastructure.Auth;

public static class LoginRateLimiting
{
    public static IServiceCollection AddLoginRateLimiting(this IServiceCollection services)
        => services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(ctx => Partition(ctx, "all", 100)),
                PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                    Partition(ctx, ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", 10)));
        });

    private static RateLimitPartition<string> Partition(HttpContext context, string key, int limit)
    {
        var path = context.Request.Path.Value?.TrimEnd('/');
        if (!HttpMethods.IsPost(context.Request.Method) ||
            !(string.Equals(path, "/login", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(path, "/api/auth/login", StringComparison.OrdinalIgnoreCase)))
            return RateLimitPartition.GetNoLimiter("other");

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}
