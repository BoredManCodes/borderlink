using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BorderLink.Agent.Interfaces;
using BorderLink.Shared;
using BorderLink.Shared.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Services;

/// <summary>
/// Pushes a <see cref="DeviceMetricSample"/> over the agent hub every
/// 30 seconds. Reuses <see cref="IDeviceInformationService"/> for the
/// memory / storage figures so we don't fork the per-OS calculations.
/// </summary>
internal class MetricsReporter : BackgroundService
{
    // 30s matches the roadmap target for rolling telemetry. Anything
    // shorter and Postgres row count balloons; anything longer and
    // short-window monitor rules (DurationSeconds < 60) become flaky.
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _initialDelay = TimeSpan.FromSeconds(45);

    private readonly IAgentHubConnection _agentHub;
    private readonly IConfigService _configService;
    private readonly IDeviceInformationService _deviceInfoService;
    private readonly ICpuUtilizationSampler _cpuSampler;
    private readonly ILogger<MetricsReporter> _logger;

    public MetricsReporter(
        IAgentHubConnection agentHub,
        IConfigService configService,
        IDeviceInformationService deviceInfoService,
        ICpuUtilizationSampler cpuSampler,
        ILogger<MetricsReporter> logger)
    {
        _agentHub = agentHub;
        _configService = configService;
        _deviceInfoService = deviceInfoService;
        _cpuSampler = cpuSampler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        try
        {
            await Task.Delay(_initialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(_interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReportOnce(stoppingToken);
                _ = await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while reporting metric sample.");
            }
        }
    }

    private async Task ReportOnce(CancellationToken cancellationToken)
    {
        if (!_agentHub.IsConnected)
        {
            return;
        }

        var connectionInfo = _configService.GetConnectionInfo();
        if (string.IsNullOrWhiteSpace(connectionInfo.DeviceID) ||
            string.IsNullOrWhiteSpace(connectionInfo.OrganizationID))
        {
            return;
        }

        var device = await _deviceInfoService.CreateDevice(
            connectionInfo.DeviceID,
            connectionInfo.OrganizationID);

        var sample = new DeviceMetricSample(
            deviceID: connectionInfo.DeviceID,
            capturedAt: DateTimeOffset.UtcNow,
            cpuPercent: ToPercent(_cpuSampler.CurrentUtilization),
            usedMemoryPercent: ToPercent(device.UsedMemory == 0 || device.TotalMemory == 0
                ? 0
                : device.UsedMemory / device.TotalMemory),
            usedStoragePercent: ToPercent(device.UsedStorage == 0 || device.TotalStorage == 0
                ? 0
                : device.UsedStorage / device.TotalStorage),
            agentOnline: true);

        await _agentHub.ReportMetricSample(sample, cancellationToken);
    }

    private static double ToPercent(double fraction)
    {
        // CpuUtilizationSampler and Device.UsedMemoryPercent both return
        // 0.0–1.0; the alerts UI thinks in 0–100. Normalize once here.
        if (double.IsNaN(fraction) || double.IsInfinity(fraction))
        {
            return 0;
        }
        return Math.Round(Math.Clamp(fraction * 100.0, 0, 100), 2);
    }
}
