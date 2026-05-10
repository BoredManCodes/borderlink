using BorderLink.Server.Data;
using BorderLink.Shared;
using BorderLink.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderLink.Server.Services;

public interface IMetricHistoryService
{
    Task RecordSampleAsync(string deviceId, DeviceMetricSample sample, CancellationToken cancellationToken);

    Task<DeviceMetricHistory[]> GetHistoryAsync(string deviceId, TimeSpan window, CancellationToken cancellationToken);

    Task PruneOlderThanAsync(TimeSpan keepFor, CancellationToken cancellationToken);
}

internal class MetricHistoryService : IMetricHistoryService
{
    private readonly IAppDbFactory _dbFactory;
    private readonly ILogger<MetricHistoryService> _logger;

    public MetricHistoryService(IAppDbFactory dbFactory, ILogger<MetricHistoryService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task RecordSampleAsync(string deviceId, DeviceMetricSample sample, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || sample is null)
        {
            return;
        }

        try
        {
            await using var db = _dbFactory.GetContext();

            // OrganizationID is a required denormalized column on
            // DeviceMetricHistory; agents don't include it in their sample
            // payload, so resolve it here from the trusted Device row.
            var orgId = await db.Devices
                .Where(x => x.ID == deviceId)
                .Select(x => x.OrganizationID)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(orgId))
            {
                return;
            }

            var row = new DeviceMetricHistory
            {
                Id = Guid.NewGuid(),
                DeviceID = deviceId,
                CapturedAt = sample.CapturedAt == default ? DateTimeOffset.UtcNow : sample.CapturedAt,
                CpuPercent = sample.CpuPercent,
                UsedMemoryPercent = sample.UsedMemoryPercent,
                UsedStoragePercent = sample.UsedStoragePercent,
                OrganizationID = orgId,
            };

            db.MetricHistory.Add(row);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error while persisting metric sample for device {deviceId}.", deviceId);
        }
    }

    public async Task<DeviceMetricHistory[]> GetHistoryAsync(string deviceId, TimeSpan window, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Array.Empty<DeviceMetricHistory>();
        }

        var cutoff = DateTimeOffset.UtcNow - window;
        await using var db = _dbFactory.GetContext();
        return await db.MetricHistory
            .AsNoTracking()
            .Where(x => x.DeviceID == deviceId && x.CapturedAt >= cutoff)
            .OrderBy(x => x.CapturedAt)
            .ToArrayAsync(cancellationToken);
    }

    public async Task PruneOlderThanAsync(TimeSpan keepFor, CancellationToken cancellationToken)
    {
        if (keepFor <= TimeSpan.Zero)
        {
            return;
        }

        var cutoff = DateTimeOffset.UtcNow - keepFor;
        await using var db = _dbFactory.GetContext();
        await db.MetricHistory
            .Where(x => x.CapturedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
