using BorderLink.Agent.Interfaces;
using BorderLink.Shared;
using BorderLink.Shared.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Services.Windows;

/// <summary>
/// Enumerates Windows services via <see cref="ServiceController"/>. Description and
/// account name require a registry/WMI lookup which we deliberately skip here to
/// keep the enumeration cheap — the UI shows DisplayName, which is sufficient for
/// the common case.
/// </summary>
[SupportedOSPlatform("windows")]
public class ServiceEnumeratorWin : IServiceEnumerator
{
    private readonly ILogger<ServiceEnumeratorWin> _logger;

    public ServiceEnumeratorWin(ILogger<ServiceEnumeratorWin> logger)
    {
        _logger = logger;
    }

    public Task<ServiceInfo[]> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                return ServiceController.GetServices()
                    .Select(svc =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            return Map(svc);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to map service {name}.", svc.ServiceName);
                            return null;
                        }
                        finally
                        {
                            svc.Dispose();
                        }
                    })
                    .Where(x => x is not null)
                    .Cast<ServiceInfo>()
                    .OrderBy(x => x.DisplayName ?? x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enumerate Windows services.");
                return Array.Empty<ServiceInfo>();
            }
        }, cancellationToken);
    }

    private static ServiceInfo Map(ServiceController svc)
    {
        return new ServiceInfo(
            name: svc.ServiceName,
            displayName: svc.DisplayName,
            description: null,
            status: MapStatus(svc.Status),
            startType: MapStartType(svc.StartType),
            canStop: svc.CanStop,
            canPauseAndContinue: svc.CanPauseAndContinue,
            accountName: null,
            processId: null);
    }

    private static ServiceStatus MapStatus(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => ServiceStatus.Running,
        ServiceControllerStatus.Stopped => ServiceStatus.Stopped,
        ServiceControllerStatus.Paused => ServiceStatus.Paused,
        ServiceControllerStatus.StartPending => ServiceStatus.Starting,
        ServiceControllerStatus.ContinuePending => ServiceStatus.Starting,
        ServiceControllerStatus.PausePending => ServiceStatus.Stopping,
        ServiceControllerStatus.StopPending => ServiceStatus.Stopping,
        _ => ServiceStatus.Other,
    };

    private static ServiceStartType MapStartType(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Automatic => ServiceStartType.Auto,
        ServiceStartMode.Manual => ServiceStartType.Manual,
        ServiceStartMode.Disabled => ServiceStartType.Disabled,
        ServiceStartMode.Boot => ServiceStartType.Boot,
        ServiceStartMode.System => ServiceStartType.System,
        _ => ServiceStartType.Other,
    };
}
