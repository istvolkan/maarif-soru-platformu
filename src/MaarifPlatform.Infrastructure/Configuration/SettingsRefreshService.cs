using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MaarifPlatform.Infrastructure.Configuration;

public sealed class SettingsRefreshService(DatabaseSettingsProvider provider, ILogger<SettingsRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { provider.SignalReload(); }
            catch (Exception ex)
            {
                // Polling retains last good configuration on errors; initial load still fails closed.
                logger.LogError(ex, "Settings refresh failed; retaining last successful configuration.");
            }
        }
    }
}
