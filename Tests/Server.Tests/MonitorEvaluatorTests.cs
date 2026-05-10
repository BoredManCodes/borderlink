#nullable enable
using BorderLink.Server.Data;
using BorderLink.Server.Services;
using BorderLink.Shared.Entities;
using BorderLink.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Server.Tests;

[TestClass]
public class MonitorEvaluatorTests
{
#nullable disable
    private TestData _testData;
    private IServiceScopeFactory _scopeFactory;
    private IAppDbFactory _dbFactory;
    private Mock<IMonitorNotifier> _notifier;
#nullable enable

    [TestInitialize]
    public async Task Init()
    {
        _testData = new TestData();
        await _testData.Init();
        _scopeFactory = IoCActivator.ServiceProvider.GetRequiredService<IServiceScopeFactory>();
        _dbFactory = IoCActivator.ServiceProvider.GetRequiredService<IAppDbFactory>();
        _notifier = new Mock<IMonitorNotifier>();
        _notifier
            .Setup(x => x.NotifyAsync(It.IsAny<MonitorFiringContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [TestMethod]
    public async Task ConditionHoldsForFullWindow_Fires()
    {
        var rule = await CreateRule(_testData.Org1Id, MonitorMetric.CpuPercent, MonitorOperator.GreaterThan, 90, durationSeconds: 60);
        // 60 seconds of history; observed span is well above the
        // half-window threshold the evaluator requires before firing.
        await SeedSamples(_testData.Org1Device1, _testData.Org1Id,
            (TimeSpan.FromSeconds(55), 95),
            (TimeSpan.FromSeconds(40), 96),
            (TimeSpan.FromSeconds(20), 97),
            (TimeSpan.FromSeconds(5), 98));

        var evaluator = BuildEvaluator();
        await evaluator.EvaluateAsync(CancellationToken.None);

        await using var db = _dbFactory.GetContext();
        var firings = await db.MonitorRuleFirings
            .Where(x => x.MonitorRuleId == rule.Id)
            .ToArrayAsync();

        Assert.AreEqual(1, firings.Length, "Sustained breach should fire exactly once.");
        Assert.AreEqual(_testData.Org1Device1.ID, firings[0].DeviceID);
        _notifier.Verify(x => x.NotifyAsync(It.IsAny<MonitorFiringContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ConditionHoldsForLessThanWindow_DoesNotFire()
    {
        var rule = await CreateRule(_testData.Org1Id, MonitorMetric.CpuPercent, MonitorOperator.GreaterThan, 90, durationSeconds: 120);

        // Only one sample in the last 30s — 90 seconds short of the window.
        await SeedSamples(_testData.Org1Device1, _testData.Org1Id,
            (TimeSpan.FromSeconds(10), 95));

        var evaluator = BuildEvaluator();
        await evaluator.EvaluateAsync(CancellationToken.None);

        await using var db = _dbFactory.GetContext();
        var firings = await db.MonitorRuleFirings
            .Where(x => x.MonitorRuleId == rule.Id)
            .ToArrayAsync();

        Assert.AreEqual(0, firings.Length);
    }

    [TestMethod]
    public async Task WithinCooldown_DoesNotFire()
    {
        var rule = await CreateRule(_testData.Org1Id, MonitorMetric.CpuPercent, MonitorOperator.GreaterThan, 90, durationSeconds: 60, cooldownMinutes: 30);

        await SeedSamples(_testData.Org1Device1, _testData.Org1Id,
            (TimeSpan.FromSeconds(60), 95),
            (TimeSpan.FromSeconds(30), 96),
            (TimeSpan.FromSeconds(0), 97));

        // Pre-seed a firing within the cooldown window.
        await using (var db = _dbFactory.GetContext())
        {
            db.MonitorRuleFirings.Add(new MonitorRuleFiring
            {
                Id = Guid.NewGuid(),
                MonitorRuleId = rule.Id,
                DeviceID = _testData.Org1Device1.ID,
                OrganizationID = _testData.Org1Id,
                FiredAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5),
                ValueAtFire = 95,
            });
            await db.SaveChangesAsync();
        }

        var evaluator = BuildEvaluator();
        await evaluator.EvaluateAsync(CancellationToken.None);

        await using var db2 = _dbFactory.GetContext();
        var firings = await db2.MonitorRuleFirings
            .Where(x => x.MonitorRuleId == rule.Id)
            .ToArrayAsync();

        Assert.AreEqual(1, firings.Length, "Should not have re-fired within the cooldown window.");
    }

    [TestMethod]
    public async Task RuleDisabled_DoesNotFire()
    {
        var rule = await CreateRule(_testData.Org1Id, MonitorMetric.CpuPercent, MonitorOperator.GreaterThan, 90, durationSeconds: 60, enabled: false);

        await SeedSamples(_testData.Org1Device1, _testData.Org1Id,
            (TimeSpan.FromSeconds(60), 95),
            (TimeSpan.FromSeconds(30), 96),
            (TimeSpan.FromSeconds(0), 97));

        var evaluator = BuildEvaluator();
        await evaluator.EvaluateAsync(CancellationToken.None);

        await using var db = _dbFactory.GetContext();
        var firings = await db.MonitorRuleFirings
            .Where(x => x.MonitorRuleId == rule.Id)
            .ToArrayAsync();

        Assert.AreEqual(0, firings.Length);
    }

    [TestMethod]
    public async Task PendingReboot_RecentRebootRequiredRunExists_Fires()
    {
        var rule = await CreateRule(_testData.Org1Id, MonitorMetric.PendingReboot, MonitorOperator.Equals, 1, durationSeconds: 0);

        // Persist a recent completed install that requires a reboot.
        await using (var db = _dbFactory.GetContext())
        {
            db.PatchInstallRuns.Add(new PatchInstallRun
            {
                Id = Guid.NewGuid(),
                DeviceID = _testData.Org1Device1.ID,
                OrganizationID = _testData.Org1Id,
                UpdateId = "abcd-1234",
                UpdateTitle = "Test Update Requiring Reboot",
                Status = PatchInstallStatus.Completed,
                RebootRequired = true,
                StartedAt = DateTimeOffset.UtcNow - TimeSpan.FromHours(1),
                CompletedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(30),
            });
            await db.SaveChangesAsync();
        }

        var evaluator = BuildEvaluator();
        await evaluator.EvaluateAsync(CancellationToken.None);

        await using var db2 = _dbFactory.GetContext();
        var firings = await db2.MonitorRuleFirings
            .Where(x => x.MonitorRuleId == rule.Id)
            .ToArrayAsync();

        Assert.AreEqual(1, firings.Length, "Pending-reboot rule should fire when a recent reboot-required install exists.");
        Assert.AreEqual(_testData.Org1Device1.ID, firings[0].DeviceID);
    }

    [TestMethod]
    public async Task PendingReboot_OldRebootRequiredRun_DoesNotFire()
    {
        var rule = await CreateRule(_testData.Org1Id, MonitorMetric.PendingReboot, MonitorOperator.Equals, 1, durationSeconds: 0);

        // Older than the 24h window the evaluator considers.
        await using (var db = _dbFactory.GetContext())
        {
            db.PatchInstallRuns.Add(new PatchInstallRun
            {
                Id = Guid.NewGuid(),
                DeviceID = _testData.Org1Device1.ID,
                OrganizationID = _testData.Org1Id,
                UpdateId = "abcd-1234",
                UpdateTitle = "Old Update",
                Status = PatchInstallStatus.Completed,
                RebootRequired = true,
                StartedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(3),
                CompletedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(2),
            });
            await db.SaveChangesAsync();
        }

        var evaluator = BuildEvaluator();
        await evaluator.EvaluateAsync(CancellationToken.None);

        await using var db2 = _dbFactory.GetContext();
        var firings = await db2.MonitorRuleFirings
            .Where(x => x.MonitorRuleId == rule.Id)
            .ToArrayAsync();

        Assert.AreEqual(0, firings.Length);
    }

    [TestMethod]
    public async Task OrgIsolation_RuleInOrgADoesNotSeeDeviceInOrgB()
    {
        // Rule is in Org1, breaching samples are on Org2's device.
        var rule = await CreateRule(_testData.Org1Id, MonitorMetric.CpuPercent, MonitorOperator.GreaterThan, 90, durationSeconds: 60);

        await SeedSamples(_testData.Org2Device1, _testData.Org2Id,
            (TimeSpan.FromSeconds(60), 95),
            (TimeSpan.FromSeconds(30), 96),
            (TimeSpan.FromSeconds(0), 97));

        var evaluator = BuildEvaluator();
        await evaluator.EvaluateAsync(CancellationToken.None);

        await using var db = _dbFactory.GetContext();
        var firings = await db.MonitorRuleFirings
            .Where(x => x.MonitorRuleId == rule.Id)
            .ToArrayAsync();

        Assert.AreEqual(0, firings.Length, "Rule must not match a device outside its organization.");
    }

    private MonitorEvaluator BuildEvaluator()
    {
        var auditLog = new Mock<IAuditLogService>();
        auditLog
            .Setup(x => x.LogAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddTransient<IAppDbFactory>(_ => _dbFactory);
        services.AddSingleton<IMonitorNotifier>(_notifier.Object);
        services.AddSingleton<IAuditLogService>(auditLog.Object);

        // Wire the data service from the test harness so the evaluator's
        // "fire" side-effects (Alert insertion) actually succeed.
        services.AddTransient<IDataService>(_ => IoCActivator.ServiceProvider.GetRequiredService<IDataService>());

        services.AddTransient<ILogger<MonitorEvaluator>>(_ => NullLogger<MonitorEvaluator>.Instance);

        var provider = services.BuildServiceProvider();
        return new MonitorEvaluator(provider);
    }

    private async Task<MonitorRule> CreateRule(
        string orgId,
        MonitorMetric metric,
        MonitorOperator op,
        double threshold,
        int durationSeconds,
        int cooldownMinutes = 30,
        bool enabled = true)
    {
        await using var db = _dbFactory.GetContext();
        var rule = new MonitorRule
        {
            Id = Guid.NewGuid(),
            OrganizationID = orgId,
            Name = $"Test rule {Guid.NewGuid():N}",
            Metric = metric,
            Operator = op,
            Threshold = threshold,
            DurationSeconds = durationSeconds,
            Channel = MonitorChannel.Email,
            ChannelTarget = "alerts@example.com",
            Enabled = enabled,
            CooldownMinutes = cooldownMinutes,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.MonitorRules.Add(rule);
        await db.SaveChangesAsync();
        return rule;
    }

    private async Task SeedSamples(Device device, string orgId, params (TimeSpan ago, double cpu)[] points)
    {
        await using var db = _dbFactory.GetContext();
        var now = DateTimeOffset.UtcNow;
        foreach (var (ago, cpu) in points)
        {
            db.MetricHistory.Add(new DeviceMetricHistory
            {
                Id = Guid.NewGuid(),
                DeviceID = device.ID,
                OrganizationID = orgId,
                CapturedAt = now - ago,
                CpuPercent = cpu,
                UsedMemoryPercent = 0,
                UsedStoragePercent = 0,
            });
        }
        await db.SaveChangesAsync();
    }
}
