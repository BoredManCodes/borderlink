using BorderLink.Agent.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Services.Windows;

/// <summary>
/// Start/stop/restart for Windows services using
/// <see cref="System.ServiceProcess.ServiceController"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public class ServiceControllerWin : IServiceController
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<ServiceControllerWin> _logger;

    public ServiceControllerWin(ILogger<ServiceControllerWin> logger)
    {
        _logger = logger;
    }

    public Task<bool> StartAsync(string name, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            try
            {
                using var svc = new ServiceController(name);
                if (svc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
                {
                    return true;
                }

                svc.Start();
                svc.WaitForStatus(ServiceControllerStatus.Running, _waitTimeout);
                return svc.Status == ServiceControllerStatus.Running;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to start service {name}.", name);
                return false;
            }
        }, cancellationToken);
    }

    public Task<bool> StopAsync(string name, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            try
            {
                using var svc = new ServiceController(name);
                if (svc.Status == ServiceControllerStatus.Stopped)
                {
                    return true;
                }

                if (!svc.CanStop)
                {
                    _logger.LogWarning("Service {name} reports CanStop=false; refusing.", name);
                    return false;
                }

                svc.Stop();
                svc.WaitForStatus(ServiceControllerStatus.Stopped, _waitTimeout);
                return svc.Status == ServiceControllerStatus.Stopped;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop service {name}.", name);
                return false;
            }
        }, cancellationToken);
    }

    public async Task<bool> RestartAsync(string name, CancellationToken cancellationToken = default)
    {
        var stopped = await StopAsync(name, cancellationToken);
        if (!stopped)
        {
            return false;
        }
        return await StartAsync(name, cancellationToken);
    }
}
