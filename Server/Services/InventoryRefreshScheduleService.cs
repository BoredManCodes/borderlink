using BorderLink.Server.Data;
using BorderLink.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderLink.Server.Services;

public interface IInventoryRefreshScheduleService
{
    Task<InventoryRefreshSchedule[]> ListAsync(string organizationId, CancellationToken cancellationToken = default);
    Task<InventoryRefreshSchedule?> GetAsync(string organizationId, Guid id, CancellationToken cancellationToken = default);
    Task<InventoryRefreshSchedule> CreateAsync(InventoryRefreshSchedule schedule, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(InventoryRefreshSchedule schedule, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string organizationId, Guid id, CancellationToken cancellationToken = default);
}

internal class InventoryRefreshScheduleService : IInventoryRefreshScheduleService
{
    private readonly IAppDbFactory _dbFactory;

    public InventoryRefreshScheduleService(IAppDbFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<InventoryRefreshSchedule[]> ListAsync(string organizationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            return Array.Empty<InventoryRefreshSchedule>();
        }

        await using var db = _dbFactory.GetContext();
        return await db.InventoryRefreshSchedules
            .AsNoTracking()
            .Where(x => x.OrganizationID == organizationId)
            .OrderBy(x => x.Name)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<InventoryRefreshSchedule?> GetAsync(string organizationId, Guid id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organizationId) || id == Guid.Empty)
        {
            return null;
        }

        await using var db = _dbFactory.GetContext();
        return await db.InventoryRefreshSchedules
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationID == organizationId && x.Id == id, cancellationToken);
    }

    public async Task<InventoryRefreshSchedule> CreateAsync(InventoryRefreshSchedule schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var toInsert = new InventoryRefreshSchedule
        {
            OrganizationID = schedule.OrganizationID,
            Name = schedule.Name,
            IntervalHours = Math.Max(1, schedule.IntervalHours),
            DeviceGroupId = string.IsNullOrWhiteSpace(schedule.DeviceGroupId) ? null : schedule.DeviceGroupId,
            DeviceTagFilter = string.IsNullOrWhiteSpace(schedule.DeviceTagFilter) ? null : schedule.DeviceTagFilter,
            Enabled = schedule.Enabled,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using var db = _dbFactory.GetContext();
        db.InventoryRefreshSchedules.Add(toInsert);
        await db.SaveChangesAsync(cancellationToken);
        return toInsert;
    }

    public async Task<bool> UpdateAsync(InventoryRefreshSchedule schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        await using var db = _dbFactory.GetContext();
        var existing = await db.InventoryRefreshSchedules
            .FirstOrDefaultAsync(x => x.Id == schedule.Id && x.OrganizationID == schedule.OrganizationID, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        existing.Name = schedule.Name;
        existing.IntervalHours = Math.Max(1, schedule.IntervalHours);
        existing.DeviceGroupId = string.IsNullOrWhiteSpace(schedule.DeviceGroupId) ? null : schedule.DeviceGroupId;
        existing.DeviceTagFilter = string.IsNullOrWhiteSpace(schedule.DeviceTagFilter) ? null : schedule.DeviceTagFilter;
        existing.Enabled = schedule.Enabled;

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(string organizationId, Guid id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organizationId) || id == Guid.Empty)
        {
            return false;
        }

        await using var db = _dbFactory.GetContext();
        var existing = await db.InventoryRefreshSchedules
            .FirstOrDefaultAsync(x => x.OrganizationID == organizationId && x.Id == id, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        db.InventoryRefreshSchedules.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
