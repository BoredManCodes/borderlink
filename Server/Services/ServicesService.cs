using BorderLink.Server.Hubs;
using BorderLink.Shared;
using BorderLink.Shared.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace BorderLink.Server.Services;

public interface IServicesService
{
    /// <summary>
    /// Asks the connected agent for a fresh service list. Returns an empty
    /// array when the device is offline or the agent fails to respond — the
    /// UI surfaces "no services" rather than an error in those cases so the
    /// tab is consistent with the Apps tab.
    /// </summary>
    Task<ServiceInfo[]> GetServicesAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a start/stop/restart command to the agent. Returns <c>true</c> only
    /// when the agent confirmed the action succeeded.
    /// </summary>
    Task<bool> ControlServiceAsync(
        string deviceId,
        string serviceName,
        string action,
        CancellationToken cancellationToken = default);
}

public class ServicesService : IServicesService
{
    private static readonly TimeSpan _agentInvokeTimeout = TimeSpan.FromMinutes(2);
    private static readonly HashSet<string> _allowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "start",
        "stop",
        "restart",
    };

    private readonly IAgentHubSessionCache _agentSessionCache;
    private readonly IHubContext<AgentHub> _agentHubContext;
    private readonly ILogger<ServicesService> _logger;

    public ServicesService(
        IAgentHubSessionCache agentSessionCache,
        IHubContext<AgentHub> agentHubContext,
        ILogger<ServicesService> logger)
    {
        _agentSessionCache = agentSessionCache;
        _agentHubContext = agentHubContext;
        _logger = logger;
    }

    public async Task<ServiceInfo[]> GetServicesAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Array.Empty<ServiceInfo>();
        }

        if (!_agentSessionCache.TryGetConnectionId(deviceId, out var connectionId) ||
            string.IsNullOrWhiteSpace(connectionId))
        {
            return Array.Empty<ServiceInfo>();
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(_agentInvokeTimeout);

            var services = await _agentHubContext.Clients
                .Client(connectionId)
                .InvokeAsync<ServiceInfo[]>(nameof(IAgentHubClient.GetServices), linkedCts.Token);

            return services ?? Array.Empty<ServiceInfo>();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Service enumeration timed out for device {deviceId}.", deviceId);
            return Array.Empty<ServiceInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate services for device {deviceId}.", deviceId);
            return Array.Empty<ServiceInfo>();
        }
    }

    public async Task<bool> ControlServiceAsync(
        string deviceId,
        string serviceName,
        string action,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(serviceName))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(action) || !_allowedActions.Contains(action))
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
                .InvokeAsync<bool>(
                    nameof(IAgentHubClient.ControlService),
                    serviceName,
                    action.ToLowerInvariant(),
                    linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Service {action} timed out for device {deviceId}, service {service}.",
                action, deviceId, serviceName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to {action} service {service} on device {deviceId}.",
                action, serviceName, deviceId);
            return false;
        }
    }
}
