using BorderLink.Server.Data;
using BorderLink.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderLink.Server.Services;

public interface IMonitorRuleService
{
    Task<MonitorRule[]> ListAsync(string organizationId, CancellationToken cancellationToken = default);
    Task<MonitorRule?> GetAsync(string organizationId, Guid id, CancellationToken cancellationToken = default);
    Task<MonitorRule> CreateAsync(MonitorRule rule, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(MonitorRule rule, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string organizationId, Guid id, CancellationToken cancellationToken = default);
}

internal class MonitorRuleService : IMonitorRuleService
{
    private readonly IAppDbFactory _dbFactory;

    public MonitorRuleService(IAppDbFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<MonitorRule[]> ListAsync(string organizationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            return Array.Empty<MonitorRule>();
        }

        await using var db = _dbFactory.GetContext();
        return await db.MonitorRules
            .AsNoTracking()
            .Where(x => x.OrganizationID == organizationId)
            .OrderBy(x => x.Name)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<MonitorRule?> GetAsync(string organizationId, Guid id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organizationId) || id == Guid.Empty)
        {
            return null;
        }

        await using var db = _dbFactory.GetContext();
        return await db.MonitorRules
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationID == organizationId && x.Id == id, cancellationToken);
    }

    public async Task<MonitorRule> CreateAsync(MonitorRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.Id == Guid.Empty)
        {
            rule = CloneWithNewId(rule);
        }
        rule.CreatedAt = DateTimeOffset.UtcNow;

        await using var db = _dbFactory.GetContext();
        db.MonitorRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<bool> UpdateAsync(MonitorRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        await using var db = _dbFactory.GetContext();
        var existing = await db.MonitorRules
            .FirstOrDefaultAsync(x => x.Id == rule.Id && x.OrganizationID == rule.OrganizationID, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        existing.Name = rule.Name;
        existing.Metric = rule.Metric;
        existing.Operator = rule.Operator;
        existing.Threshold = rule.Threshold;
        existing.DurationSeconds = rule.DurationSeconds;
        existing.DeviceFilterTag = rule.DeviceFilterTag;
        existing.DeviceGroupId = rule.DeviceGroupId;
        existing.Channel = rule.Channel;
        existing.ChannelTarget = rule.ChannelTarget;
        existing.Enabled = rule.Enabled;
        existing.CooldownMinutes = rule.CooldownMinutes;

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
        var existing = await db.MonitorRules
            .FirstOrDefaultAsync(x => x.OrganizationID == organizationId && x.Id == id, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        db.MonitorRules.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static MonitorRule CloneWithNewId(MonitorRule rule)
    {
        return new MonitorRule
        {
            OrganizationID = rule.OrganizationID,
            Name = rule.Name,
            Metric = rule.Metric,
            Operator = rule.Operator,
            Threshold = rule.Threshold,
            DurationSeconds = rule.DurationSeconds,
            DeviceFilterTag = rule.DeviceFilterTag,
            DeviceGroupId = rule.DeviceGroupId,
            Channel = rule.Channel,
            ChannelTarget = rule.ChannelTarget,
            Enabled = rule.Enabled,
            CooldownMinutes = rule.CooldownMinutes,
            CreatedAt = rule.CreatedAt,
            LastFiredAt = rule.LastFiredAt,
        };
    }
}
