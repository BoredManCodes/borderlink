using BorderLink.Server.Hubs;
using BorderLink.Shared;
using BorderLink.Shared.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace BorderLink.Server.Services;

public interface IProcessesService
{
    /// <summary>
    /// Asks the connected agent for a snapshot of running processes. Returns
    /// an empty array when the device is offline or unresponsive.
    /// </summary>
    Task<ProcessInfo[]> GetProcessesAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the agent to terminate the given PID.
    /// </summary>
    Task<bool> KillProcessAsync(string deviceId, int pid, CancellationToken cancellationToken = default);
}

public class ProcessesService : IProcessesService
{
    private static readonly TimeSpan _agentInvokeTimeout = TimeSpan.FromMinutes(2);

    private readonly IAgentHubSessionCache _agentSessionCache;
    private readonly IHubContext<AgentHub> _agentHubContext;
    private readonly ILogger<ProcessesService> _logger;

    public ProcessesService(
        IAgentHubSessionCache agentSessionCache,
        IHubContext<AgentHub> agentHubContext,
        ILogger<ProcessesService> logger)
    {
        _agentSessionCache = agentSessionCache;
        _agentHubContext = agentHubContext;
        _logger = logger;
    }

    public async Task<ProcessInfo[]> GetProcessesAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Array.Empty<ProcessInfo>();
        }

        if (!_agentSessionCache.TryGetConnectionId(deviceId, out var connectionId) ||
            string.IsNullOrWhiteSpace(connectionId))
        {
            return Array.Empty<ProcessInfo>();
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(_agentInvokeTimeout);

            var processes = await _agentHubContext.Clients
                .Client(connectionId)
                .InvokeAsync<ProcessInfo[]>(nameof(IAgentHubClient.GetProcesses), linkedCts.Token);

            return processes ?? Array.Empty<ProcessInfo>();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Process enumeration timed out for device {deviceId}.", deviceId);
            return Array.Empty<ProcessInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate processes for device {deviceId}.", deviceId);
            return Array.Empty<ProcessInfo>();
        }
    }

    public async Task<bool> KillProcessAsync(string deviceId, int pid, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || pid <= 0)
        {
            return false;
        }

        if (!_agentSessionCache.TryGetConnectionId(deviceId, out var connectionId) ||
            string.IsNullOrWhiteSpace(connectionId))
        {
            return false;
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(_agentInvokeTimeout);

            return await _agentHubContext.Clients
                .Client(connectionId)
                .InvokeAsync<bool>(nameof(IAgentHubClient.KillProcess), pid, linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Process kill timed out for device {deviceId}, pid {pid}.",
                deviceId, pid);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to kill PID {pid} on device {deviceId}.", pid, deviceId);
            return false;
        }
    }
}
