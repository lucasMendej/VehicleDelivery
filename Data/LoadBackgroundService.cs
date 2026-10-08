using Afleverlijst.Configuration;
using Microsoft.Extensions.Options;

namespace Afleverlijst.Data;

public sealed class LoadBackgroundService(LoadCoordinator coordinator, ILogger<LoadBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var status in coordinator.AllStatuses)
            {
                if (coordinator.ActiveSessions(status.Set) == 0) continue;
                if (coordinator.StartSyncIfDue(status.Set))
                    logger.LogInformation("Sync is due voor _set={Set}; sync wordt gestart.", status.Set);
            }
        }
    }
}
