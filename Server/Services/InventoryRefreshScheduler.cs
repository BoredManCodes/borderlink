using BorderLink.Server.Data;
using BorderLink.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderLink.Server.Services;

/// <summary>
/// Wakes every 5 minutes and re-runs inventory refresh for any
/// <see cref="InventoryRefreshSchedule"/> whose interval has elapsed.
/// Modeled on <see cref="ScriptScheduler"/>.
/// </summary>
public class InventoryRefreshScheduler : IHostedService, IDisposable
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);

    private static readonly SemaphoreSlim _tickLock = new(1, 1);

    private readonly IServiceProvider _serviceProvider;
    private System.Timers.Timer? _timer;

    public InventoryRefreshScheduler(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = new System.Timers.Timer(TickInterval);
        _timer.Elapsed += (_, _) => _ = TickAsync(CancellationToken.None);
        _timer.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        if (!await _tickLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<InventoryRefreshScheduler>>();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IAppDbFactory>();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var sessionCache = scope.ServiceProvider.GetRequiredService<IAgentHubSessionCache>();

            try
            {
                await RunDueSchedules(dbFactory, inventoryService, sessionCache, logger, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during inventory refresh tick.");
            }
        }
        finally
        {
            _tickLock.Release();
        }
    }

    private static async Task RunDueSchedules(
        IAppDbFactory dbFactory,
        IInventoryService inventoryService,
        IAgentHubSessionCache sessionCache,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var db = dbFactory.GetContext();

        var now = DateTimeOffset.UtcNow;

        // Pull all enabled schedules — interval check happens in memory so the
        // SQL stays portable across providers.
        var schedules = await db.InventoryRefreshSchedules
            .Where(x => x.Enabled)
            .ToArrayAsync(cancellationToken);

        if (schedules.Length == 0)
        {
            return;
        }

        foreach (var schedule in schedules)
        {
            try
            {
                if (!IsDue(schedule, now))
                {
                    continue;
                }

                await RunSchedule(db, schedule, inventoryService, sessionCache, now, logger, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Error while running inventory refresh schedule {scheduleId}.",
                    schedule.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static bool IsDue(InventoryRefreshSchedule schedule, DateTimeOffset now)
    {
        if (!schedule.Enabled)
        {
            return false;
        }
        var interval = TimeSpan.FromHours(Math.Max(1, schedule.IntervalHours));
        if (schedule.LastRunAt is null)
        {
            return true;
        }
        return now - schedule.LastRunAt.Value >= interval;
    }

    private static async Task RunSchedule(
        AppDb db,
        InventoryRefreshSchedule schedule,
        IInventoryService inventoryService,
        IAgentHubSessionCache sessionCache,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var devicesQuery = db.Devices
            .AsNoTracking()
            .Where(x => x.OrganizationID == schedule.OrganizationID);

        if (!string.IsNullOrWhiteSpace(schedule.DeviceGroupId))
        {
            devicesQuery = devicesQuery.Where(x => x.DeviceGroupID == schedule.DeviceGroupId);
        }

        if (!string.IsNullOrWhiteSpace(schedule.DeviceTagFilter))
        {
            var tag = schedule.DeviceTagFilter;
            devicesQuery = devicesQuery.Where(x => x.Tags != null && x.Tags.Contains(tag));
        }

        var devices = await devicesQuery
            .Select(x => new { x.ID })
            .ToArrayAsync(cancellationToken);

        var refreshed = 0;
        foreach (var device in devices)
        {
            if (!sessionCache.TryGetByDeviceId(device.ID, out _))
            {
                continue;
            }

            await inventoryService.TryRefreshSnapshotInBackground(device.ID);
            refreshed++;
        }

        // Mark the schedule run regardless — we don't want a long-offline
        // org's schedule to try its full device list every 5 minutes.
        var tracked = await db.InventoryRefreshSchedules
            .FirstOrDefaultAsync(x => x.Id == schedule.Id, cancellationToken);
        if (tracked is not null)
        {
            tracked.LastRunAt = now;
        }

        logger.LogInformation(
            "Inventory schedule {scheduleId} ({name}) refreshed {refreshed} of {total} devices.",
            schedule.Id, schedule.Name, refreshed, devices.Length);
    }
}
