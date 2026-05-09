using BorderLink.Server.Data;
using BorderLink.Server.Hubs;
using BorderLink.Shared;
using BorderLink.Shared.Entities;
using BorderLink.Shared.Interfaces;
using BorderLink.Shared.Utilities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BorderLink.Server.Services;

public interface IInventoryService
{
    /// <summary>
    /// Returns the most recent inventory snapshot for the given device, or
    /// <c>null</c> if no snapshot has ever been captured.
    /// </summary>
    Task<DeviceInventorySnapshot?> GetLatestSnapshot(string deviceId);

    /// <summary>
    /// Asks the connected agent for a fresh installed-apps list, persists it
    /// as a new snapshot, and prunes older snapshots beyond the retention
    /// limit. Returns <c>Fail</c> if the device is not currently connected
    /// or the agent did not respond.
    /// </summary>
    Task<Result<DeviceInventorySnapshot>> RefreshSnapshot(string deviceId);

    /// <summary>
    /// Fire-and-forget background refresh, used immediately after a device
    /// comes online so the portal has data to show without manual action.
    /// </summary>
    Task TryRefreshSnapshotInBackground(string deviceId);
}

public class InventoryService : IInventoryService
{
    private const int RetentionCount = 3;
    private static readonly TimeSpan _agentInvokeTimeout = TimeSpan.FromMinutes(2);

    private readonly IAppDbFactory _appDbFactory;
    private readonly IAgentHubSessionCache _agentSessionCache;
    private readonly IHubContext<AgentHub> _agentHubContext;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(
        IAppDbFactory appDbFactory,
        IAgentHubSessionCache agentSessionCache,
        IHubContext<AgentHub> agentHubContext,
        ILogger<InventoryService> logger)
    {
        _appDbFactory = appDbFactory;
        _agentSessionCache = agentSessionCache;
        _agentHubContext = agentHubContext;
        _logger = logger;
    }

    public async Task<DeviceInventorySnapshot?> GetLatestSnapshot(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        using var dbContext = _appDbFactory.GetContext();
        return await dbContext.DeviceInventorySnapshots
            .AsNoTracking()
            .Where(x => x.DeviceID == deviceId)
            .OrderByDescending(x => x.CapturedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<Result<DeviceInventorySnapshot>> RefreshSnapshot(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Result.Fail<DeviceInventorySnapshot>("Device ID is required.");
        }

        if (!_agentSessionCache.TryGetConnectionId(deviceId, out var connectionId) ||
            string.IsNullOrWhiteSpace(connectionId))
        {
            return Result.Fail<DeviceInventorySnapshot>("Device is not currently online.");
        }

        try
        {
            using var cts = new CancellationTokenSource(_agentInvokeTimeout);
            var apps = await _agentHubContext.Clients
                .Client(connectionId)
                .InvokeAsync<List<InstalledApp>>(nameof(IAgentHubClient.GetInstalledApps), cts.Token);

            apps ??= new List<InstalledApp>();

            var snapshot = await PersistSnapshot(deviceId, apps);
            return Result.Ok(snapshot);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Inventory refresh timed out for device {deviceId}.", deviceId);
            return Result.Fail<DeviceInventorySnapshot>("The agent did not respond in time.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh inventory for device {deviceId}.", deviceId);
            return Result.Fail<DeviceInventorySnapshot>(ex);
        }
    }

    public async Task TryRefreshSnapshotInBackground(string deviceId)
    {
        try
        {
            await RefreshSnapshot(deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Background inventory refresh failed for device {deviceId}.", deviceId);
        }
    }

    private async Task<DeviceInventorySnapshot> PersistSnapshot(string deviceId, List<InstalledApp> apps)
    {
        using var dbContext = _appDbFactory.GetContext();

        var snapshot = new DeviceInventorySnapshot
        {
            Id = Guid.NewGuid(),
            DeviceID = deviceId,
            CapturedAt = DateTimeOffset.UtcNow,
            Apps = apps,
        };

        dbContext.DeviceInventorySnapshots.Add(snapshot);

        // Retention: keep the most recent N snapshots per device.
        var stale = await dbContext.DeviceInventorySnapshots
            .Where(x => x.DeviceID == deviceId)
            .OrderByDescending(x => x.CapturedAt)
            .Skip(RetentionCount - 1)
            .ToListAsync();

        if (stale.Count > 0)
        {
            dbContext.DeviceInventorySnapshots.RemoveRange(stale);
        }

        await dbContext.SaveChangesAsync();
        return snapshot;
    }
}
