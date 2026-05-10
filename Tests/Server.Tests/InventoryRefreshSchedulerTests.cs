#nullable enable
using BorderLink.Server.Data;
using BorderLink.Server.Services;
using BorderLink.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Server.Tests;

[TestClass]
public class InventoryRefreshSchedulerTests
{
#nullable disable
    private TestData _testData;
    private IAppDbFactory _dbFactory;
    private Mock<IInventoryService> _inventory;
    private Mock<IAgentHubSessionCache> _sessionCache;
#nullable enable

    [TestInitialize]
    public async Task Init()
    {
        _testData = new TestData();
        await _testData.Init();
        _dbFactory = IoCActivator.ServiceProvider.GetRequiredService<IAppDbFactory>();
        _inventory = new Mock<IInventoryService>();
        _inventory
            .Setup(x => x.TryRefreshSnapshotInBackground(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _sessionCache = new Mock<IAgentHubSessionCache>();
    }

    [TestMethod]
    public async Task DueSchedule_RefreshesEachOnlineDevice()
    {
        var schedule = await CreateSchedule(_testData.Org1Id, intervalHours: 24, lastRunAt: DateTimeOffset.UtcNow - TimeSpan.FromHours(25));

        // Both Org1 devices are present in the session cache (online).
        SetupOnlineDevices(_testData.Org1Device1.ID, _testData.Org1Device2.ID);

        var scheduler = BuildScheduler();
        await scheduler.TickAsync(CancellationToken.None);

        _inventory.Verify(x => x.TryRefreshSnapshotInBackground(_testData.Org1Device1.ID), Times.Once);
        _inventory.Verify(x => x.TryRefreshSnapshotInBackground(_testData.Org1Device2.ID), Times.Once);

        await using var db = _dbFactory.GetContext();
        var saved = await db.InventoryRefreshSchedules.FirstAsync(x => x.Id == schedule.Id);
        Assert.IsNotNull(saved.LastRunAt);
        Assert.IsTrue(saved.LastRunAt > DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1));
    }

    [TestMethod]
    public async Task NotYetDue_Skipped()
    {
        // Last run was just a moment ago — interval is 24h, so we shouldn't refresh.
        await CreateSchedule(_testData.Org1Id, intervalHours: 24, lastRunAt: DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5));
        SetupOnlineDevices(_testData.Org1Device1.ID, _testData.Org1Device2.ID);

        var scheduler = BuildScheduler();
        await scheduler.TickAsync(CancellationToken.None);

        _inventory.Verify(x => x.TryRefreshSnapshotInBackground(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task DisabledSchedule_Skipped()
    {
        await CreateSchedule(_testData.Org1Id, intervalHours: 24, lastRunAt: null, enabled: false);
        SetupOnlineDevices(_testData.Org1Device1.ID, _testData.Org1Device2.ID);

        var scheduler = BuildScheduler();
        await scheduler.TickAsync(CancellationToken.None);

        _inventory.Verify(x => x.TryRefreshSnapshotInBackground(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task OrgIsolation_DoesNotTouchOtherOrgDevices()
    {
        // Schedule belongs to Org1; the only online device is Org2's.
        await CreateSchedule(_testData.Org1Id, intervalHours: 24, lastRunAt: null);
        SetupOnlineDevices(_testData.Org2Device1.ID);

        var scheduler = BuildScheduler();
        await scheduler.TickAsync(CancellationToken.None);

        _inventory.Verify(x => x.TryRefreshSnapshotInBackground(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task OfflineDevices_Skipped()
    {
        await CreateSchedule(_testData.Org1Id, intervalHours: 24, lastRunAt: null);
        // No devices in the session cache — everyone offline.
        SetupOnlineDevices(Array.Empty<string>());

        var scheduler = BuildScheduler();
        await scheduler.TickAsync(CancellationToken.None);

        _inventory.Verify(x => x.TryRefreshSnapshotInBackground(It.IsAny<string>()), Times.Never);

        await using var db = _dbFactory.GetContext();
        var saved = await db.InventoryRefreshSchedules.FirstAsync(x => x.OrganizationID == _testData.Org1Id);
        Assert.IsNotNull(saved.LastRunAt, "LastRunAt should advance even when no devices were online to avoid retry storms.");
    }

    [TestMethod]
    public void IsDue_Logic()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.IsTrue(InventoryRefreshScheduler.IsDue(
            new InventoryRefreshSchedule { Enabled = true, IntervalHours = 24, LastRunAt = null }, now));
        Assert.IsTrue(InventoryRefreshScheduler.IsDue(
            new InventoryRefreshSchedule { Enabled = true, IntervalHours = 24, LastRunAt = now - TimeSpan.FromHours(25) }, now));
        Assert.IsFalse(InventoryRefreshScheduler.IsDue(
            new InventoryRefreshSchedule { Enabled = true, IntervalHours = 24, LastRunAt = now - TimeSpan.FromHours(1) }, now));
        Assert.IsFalse(InventoryRefreshScheduler.IsDue(
            new InventoryRefreshSchedule { Enabled = false, IntervalHours = 24, LastRunAt = null }, now));
    }

    private void SetupOnlineDevices(params string[] deviceIds)
    {
        foreach (var id in deviceIds)
        {
            var captured = id;
            Device? captureDevice = new Device
            {
                ID = captured,
                DeviceName = captured,
                OrganizationID = string.Empty,
            };
            _sessionCache
                .Setup(x => x.TryGetByDeviceId(captured, out captureDevice!))
                .Returns(true);
        }

        // Default for unknown ids: not online.
        _sessionCache
            .Setup(x => x.TryGetByDeviceId(It.Is<string>(id => !deviceIds.Contains(id)), out It.Ref<Device?>.IsAny))
            .Returns(false);
    }

    private async Task<InventoryRefreshSchedule> CreateSchedule(
        string orgId,
        int intervalHours,
        DateTimeOffset? lastRunAt,
        bool enabled = true,
        string? deviceGroupId = null,
        string? tagFilter = null)
    {
        await using var db = _dbFactory.GetContext();
        var schedule = new InventoryRefreshSchedule
        {
            OrganizationID = orgId,
            Name = $"Test schedule {Guid.NewGuid():N}",
            IntervalHours = intervalHours,
            DeviceGroupId = deviceGroupId,
            DeviceTagFilter = tagFilter,
            Enabled = enabled,
            LastRunAt = lastRunAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.InventoryRefreshSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return schedule;
    }

    private InventoryRefreshScheduler BuildScheduler()
    {
        var services = new ServiceCollection();
        services.AddTransient<IAppDbFactory>(_ => _dbFactory);
        services.AddSingleton<IInventoryService>(_inventory.Object);
        services.AddSingleton<IAgentHubSessionCache>(_sessionCache.Object);
        services.AddTransient<ILogger<InventoryRefreshScheduler>>(_ => NullLogger<InventoryRefreshScheduler>.Instance);
        var provider = services.BuildServiceProvider();
        return new InventoryRefreshScheduler(provider);
    }
}
