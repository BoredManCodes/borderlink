using BorderLink.Server.Data;
using BorderLink.Server.Hubs;
using BorderLink.Shared;
using BorderLink.Shared.Entities;
using BorderLink.Shared.Enums;
using BorderLink.Shared.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BorderLink.Server.Services;

public interface IPatchService
{
    /// <summary>
    /// Round-trips to the agent for a fresh pending-update list. Empty
    /// array if the device is offline or the agent fails to respond inside
    /// <see cref="AgentInvokeTimeout"/>.
    /// </summary>
    Task<PatchUpdate[]> GetPendingUpdatesAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Round-trips to the agent for a pending-reboot probe.
    /// </summary>
    Task<PendingRebootInfo> GetPendingRebootAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a <see cref="PatchInstallRun"/> row, asks the agent to start
    /// the install, and returns the persisted row. Returns <c>null</c>
    /// when the device isn't online or the agent rejects the request.
    /// </summary>
    Task<PatchInstallRun?> RequestInstallAsync(
        string deviceId,
        string updateId,
        string updateTitle,
        string initiatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent <see cref="PatchInstallRun"/> rows for the
    /// device, newest first, capped at <paramref name="max"/>.
    /// </summary>
    Task<PatchInstallRun[]> GetRecentRunsAsync(string deviceId, int max, CancellationToken cancellationToken = default);
}

public class PatchService : IPatchService
{
    // Microsoft.Update.Session search can take 30+ seconds; pad the timeout.
    public static readonly TimeSpan AgentInvokeTimeout = TimeSpan.FromSeconds(60);

    private readonly IAppDbFactory _dbFactory;
    private readonly IAgentHubSessionCache _agentSessionCache;
    private readonly IHubContext<AgentHub> _agentHubContext;
    private readonly ILogger<PatchService> _logger;

    public PatchService(
        IAppDbFactory dbFactory,
        IAgentHubSessionCache agentSessionCache,
        IHubContext<AgentHub> agentHubContext,
        ILogger<PatchService> logger)
    {
        _dbFactory = dbFactory;
        _agentSessionCache = agentSessionCache;
        _agentHubContext = agentHubContext;
        _logger = logger;
    }

    public async Task<PatchUpdate[]> GetPendingUpdatesAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Array.Empty<PatchUpdate>();
        }

        if (!_agentSessionCache.TryGetConnectionId(deviceId, out var connectionId) ||
            string.IsNullOrWhiteSpace(connectionId))
        {
            return Array.Empty<PatchUpdate>();
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(AgentInvokeTimeout);

            var updates = await _agentHubContext.Clients
                .Client(connectionId)
                .InvokeAsync<PatchUpdate[]>(nameof(IAgentHubClient.GetPendingUpdates), linkedCts.Token);

            return updates ?? Array.Empty<PatchUpdate>();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Pending-updates query timed out for device {deviceId}.", deviceId);
            return Array.Empty<PatchUpdate>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query pending updates for device {deviceId}.", deviceId);
            return Array.Empty<PatchUpdate>();
        }
    }

    public async Task<PendingRebootInfo> GetPendingRebootAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return new PendingRebootInfo(false, Array.Empty<string>());
        }

        if (!_agentSessionCache.TryGetConnectionId(deviceId, out var connectionId) ||
            string.IsNullOrWhiteSpace(connectionId))
        {
            return new PendingRebootInfo(false, Array.Empty<string>());
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(30));

            var info = await _agentHubContext.Clients
                .Client(connectionId)
                .InvokeAsync<PendingRebootInfo>(nameof(IAgentHubClient.GetPendingReboot), linkedCts.Token);

            return info ?? new PendingRebootInfo(false, Array.Empty<string>());
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Pending-reboot probe timed out for device {deviceId}.", deviceId);
            return new PendingRebootInfo(false, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to probe pending reboot for device {deviceId}.", deviceId);
            return new PendingRebootInfo(false, Array.Empty<string>());
        }
    }

    public async Task<PatchInstallRun?> RequestInstallAsync(
        string deviceId,
        string updateId,
        string updateTitle,
        string initiatorId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(updateId))
        {
            return null;
        }

        if (!_agentSessionCache.TryGetByDeviceId(deviceId, out var device))
        {
            return null;
        }

        if (!_agentSessionCache.TryGetConnectionId(deviceId, out var connectionId) ||
            string.IsNullOrWhiteSpace(connectionId))
        {
            return null;
        }

        var run = new PatchInstallRun
        {
            Id = Guid.NewGuid(),
            DeviceID = deviceId,
            OrganizationID = device.OrganizationID,
            UpdateId = updateId,
            UpdateTitle = string.IsNullOrWhiteSpace(updateTitle) ? updateId : updateTitle,
            InitiatorId = string.IsNullOrWhiteSpace(initiatorId) ? null : initiatorId,
            StartedAt = DateTimeOffset.UtcNow,
            Status = PatchInstallStatus.Pending,
        };

        await using (var db = _dbFactory.GetContext())
        {
            db.PatchInstallRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(30));

            var queued = await _agentHubContext.Clients
                .Client(connectionId)
                .InvokeAsync<bool>(nameof(IAgentHubClient.InstallUpdate), updateId, linkedCts.Token);

            if (!queued)
            {
                await UpdateRunStatusAsync(run.Id, PatchInstallStatus.Failed, "Agent rejected the install request.", cancellationToken);
                run.Status = PatchInstallStatus.Failed;
                run.Notes = "Agent rejected the install request.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue install for device {deviceId}, update {updateId}.", deviceId, updateId);
            await UpdateRunStatusAsync(run.Id, PatchInstallStatus.Failed, ex.Message, cancellationToken);
            run.Status = PatchInstallStatus.Failed;
            run.Notes = ex.Message;
        }

        return run;
    }

    public async Task<PatchInstallRun[]> GetRecentRunsAsync(string deviceId, int max, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Array.Empty<PatchInstallRun>();
        }

        var capped = max <= 0 ? 50 : Math.Min(max, 200);

        await using var db = _dbFactory.GetContext();
        return await db.PatchInstallRuns
            .AsNoTracking()
            .Where(x => x.DeviceID == deviceId)
            .OrderByDescending(x => x.StartedAt)
            .Take(capped)
            .ToArrayAsync(cancellationToken);
    }

    private async Task UpdateRunStatusAsync(
        Guid runId,
        PatchInstallStatus status,
        string? notes,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = _dbFactory.GetContext();
            var row = await db.PatchInstallRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
            if (row is null)
            {
                return;
            }

            row.Status = status;
            row.Notes = notes;
            if (status is PatchInstallStatus.Completed or PatchInstallStatus.Failed)
            {
                row.CompletedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to update PatchInstallRun {runId}.", runId);
        }
    }
}
