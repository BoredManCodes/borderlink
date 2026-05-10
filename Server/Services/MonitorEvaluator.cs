using BorderLink.Server.Data;
using BorderLink.Shared;
using BorderLink.Shared.Entities;
using BorderLink.Shared.Enums;
using BorderLink.Shared.Utilities;
using Microsoft.EntityFrameworkCore;

namespace BorderLink.Server.Services;

/// <summary>
/// Wakes every <see cref="EvaluationInterval"/> and walks every enabled
/// <see cref="MonitorRule"/>, firing alerts whose conditions hold across
/// the rule's <c>DurationSeconds</c> trailing window. Modeled on
/// <see cref="ScriptScheduler"/>.
/// </summary>
public class MonitorEvaluator : IHostedService, IDisposable
{
    public static readonly TimeSpan EvaluationInterval = EnvironmentHelper.IsDebug
        ? TimeSpan.FromSeconds(15)
        : TimeSpan.FromSeconds(60);

    private static readonly SemaphoreSlim _tickLock = new(1, 1);

    private readonly IServiceProvider _serviceProvider;
    private System.Timers.Timer? _timer;

    public MonitorEvaluator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = new System.Timers.Timer(EvaluationInterval);
        _timer.Elapsed += (_, _) => _ = EvaluateAsync(CancellationToken.None);
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

    public async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        if (!await _tickLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<MonitorEvaluator>>();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IAppDbFactory>();
            var notifier = scope.ServiceProvider.GetRequiredService<IMonitorNotifier>();
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
            var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();

            try
            {
                await EvaluateRules(dbFactory, notifier, auditService, dataService, logger, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during monitor evaluation tick.");
            }
        }
        finally
        {
            _tickLock.Release();
        }
    }

    private static async Task EvaluateRules(
        IAppDbFactory dbFactory,
        IMonitorNotifier notifier,
        IAuditLogService auditService,
        IDataService dataService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var db = dbFactory.GetContext();

        var rules = await db.MonitorRules
            .AsNoTracking()
            .Where(x => x.Enabled)
            .ToArrayAsync(cancellationToken);

        if (rules.Length == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var rule in rules)
        {
            try
            {
                await EvaluateRule(db, rule, now, notifier, auditService, dataService, logger, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error while evaluating rule {ruleId}.", rule.Id);
            }
        }
    }

    private static async Task EvaluateRule(
        AppDb db,
        MonitorRule rule,
        DateTimeOffset now,
        IMonitorNotifier notifier,
        IAuditLogService auditService,
        IDataService dataService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var devicesQuery = db.Devices
            .AsNoTracking()
            .Where(x => x.OrganizationID == rule.OrganizationID);

        if (!string.IsNullOrWhiteSpace(rule.DeviceGroupId))
        {
            devicesQuery = devicesQuery.Where(x => x.DeviceGroupID == rule.DeviceGroupId);
        }

        if (!string.IsNullOrWhiteSpace(rule.DeviceFilterTag))
        {
            var tag = rule.DeviceFilterTag;
            devicesQuery = devicesQuery.Where(x => x.Tags != null && x.Tags.Contains(tag));
        }

        var devices = await devicesQuery.ToArrayAsync(cancellationToken);
        if (devices.Length == 0)
        {
            return;
        }

        foreach (var device in devices)
        {
            // Cooldown is per-(rule, device) — a rule that just fired against
            // device A shouldn't be muted for device B.
            if (rule.CooldownMinutes > 0)
            {
                var cooldownCutoff = now - TimeSpan.FromMinutes(rule.CooldownMinutes);
                var firedRecently = await db.MonitorRuleFirings
                    .AsNoTracking()
                    .AnyAsync(
                        x => x.MonitorRuleId == rule.Id &&
                             x.DeviceID == device.ID &&
                             x.FiredAt >= cooldownCutoff,
                        cancellationToken);

                if (firedRecently)
                {
                    continue;
                }
            }

            var (matched, observedValue) = await EvaluateAgainstDevice(db, rule, device, now, cancellationToken);
            if (!matched)
            {
                continue;
            }

            await FireRule(db, rule, device, observedValue, notifier, auditService, dataService, logger, cancellationToken);
        }
    }

    private static async Task<(bool matched, double value)> EvaluateAgainstDevice(
        AppDb db,
        MonitorRule rule,
        Device device,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        switch (rule.Metric)
        {
            case MonitorMetric.AgentOffline:
            {
                if (device.IsOnline)
                {
                    return (false, 0);
                }
                var offlineFor = now - device.LastOnline;
                var matched = offlineFor.TotalSeconds >= rule.DurationSeconds;
                return (matched, offlineFor.TotalSeconds);
            }

            case MonitorMetric.PendingReboot:
            {
                // Pragmatic proxy: any reboot-required PatchInstallRun in
                // the last 24h on this device. The robust check would
                // round-trip the agent (via IPatchService.GetPendingReboot)
                // and reconcile against Device.LastOnline > CompletedAt;
                // we accept the simpler version because (a) patch installs
                // that need a reboot almost always do mean a reboot is
                // pending, and (b) anything older than 24h has either
                // rebooted or become noise. Tighten this when the wider
                // monitor evaluator can do per-rule async work.
                var cutoff = now - TimeSpan.FromHours(24);
                var matched = await db.PatchInstallRuns
                    .AsNoTracking()
                    .AnyAsync(
                        x => x.DeviceID == device.ID &&
                             x.RebootRequired &&
                             x.CompletedAt != null &&
                             x.CompletedAt > cutoff,
                        cancellationToken);
                return (matched, matched ? 1 : 0);
            }

            case MonitorMetric.CpuPercent:
            case MonitorMetric.MemoryPercent:
            case MonitorMetric.DiskPercent:
            {
                var window = TimeSpan.FromSeconds(Math.Max(1, rule.DurationSeconds));
                var cutoff = now - window;

                var samples = await db.MetricHistory
                    .AsNoTracking()
                    .Where(x => x.DeviceID == device.ID && x.CapturedAt >= cutoff)
                    .OrderBy(x => x.CapturedAt)
                    .ToArrayAsync(cancellationToken);

                if (samples.Length == 0)
                {
                    return (false, 0);
                }

                // Require the trailing window to actually span (most of)
                // the rule's duration before declaring a sustained breach —
                // otherwise a single outlier sample arriving fresh on a
                // brand-new device would fire a 10-minute rule immediately.
                var first = samples[0];
                var last = samples[^1];
                var observedSpan = last.CapturedAt - first.CapturedAt;
                var requiredSpan = TimeSpan.FromSeconds(rule.DurationSeconds * 0.5);
                if (observedSpan < requiredSpan)
                {
                    return (false, 0);
                }

                var allMatch = samples.All(s => Compare(GetMetric(s, rule.Metric), rule.Operator, rule.Threshold));
                if (!allMatch)
                {
                    return (false, 0);
                }

                var latest = samples[^1];
                return (true, GetMetric(latest, rule.Metric));
            }

            default:
                return (false, 0);
        }
    }

    private static double GetMetric(DeviceMetricHistory sample, MonitorMetric metric) => metric switch
    {
        MonitorMetric.CpuPercent => sample.CpuPercent,
        MonitorMetric.MemoryPercent => sample.UsedMemoryPercent,
        MonitorMetric.DiskPercent => sample.UsedStoragePercent,
        _ => 0,
    };

    private static bool Compare(double observed, MonitorOperator op, double threshold) => op switch
    {
        MonitorOperator.GreaterThan => observed > threshold,
        MonitorOperator.LessThan => observed < threshold,
        MonitorOperator.Equals => Math.Abs(observed - threshold) < 0.0001,
        MonitorOperator.NotEquals => Math.Abs(observed - threshold) >= 0.0001,
        _ => false,
    };

    private static async Task FireRule(
        AppDb db,
        MonitorRule rule,
        Device device,
        double observedValue,
        IMonitorNotifier notifier,
        IAuditLogService auditService,
        IDataService dataService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var firedAt = DateTimeOffset.UtcNow;

        var firing = new MonitorRuleFiring
        {
            Id = Guid.NewGuid(),
            MonitorRuleId = rule.Id,
            DeviceID = device.ID,
            OrganizationID = rule.OrganizationID,
            FiredAt = firedAt,
            ValueAtFire = observedValue,
        };
        db.MonitorRuleFirings.Add(firing);

        var ruleRow = await db.MonitorRules.FirstOrDefaultAsync(x => x.Id == rule.Id, cancellationToken);
        if (ruleRow is not null)
        {
            ruleRow.LastFiredAt = firedAt;
        }

        await db.SaveChangesAsync(cancellationToken);

        var alertMessage = $"Monitor rule '{rule.Name}' fired on {device.DeviceName ?? device.ID}.";
        var details = $"Metric={rule.Metric}, Operator={rule.Operator}, Threshold={rule.Threshold}, Observed={observedValue:F2}";

        try
        {
            await dataService.AddAlert(device.ID, rule.OrganizationID, alertMessage, details);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to insert Alert for monitor rule {ruleId}.", rule.Id);
        }

        await notifier.NotifyAsync(
            new MonitorFiringContext(rule, device, observedValue, firedAt),
            cancellationToken);

        await auditService.LogAsync(
            AuditActions.MonitorAlertFired,
            rule.OrganizationID,
            targetType: "MonitorRule",
            targetId: rule.Id.ToString(),
            targetName: rule.Name,
            resultMessage: details,
            cancellationToken: cancellationToken);
    }
}
