using BorderLink.Server.Data;
using BorderLink.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BorderLink.Server.Services;

public interface IAuditLogService
{
    Task LogAsync(
        string action,
        string organizationId,
        string? userName = null,
        string? userId = null,
        string? targetType = null,
        string? targetId = null,
        string? targetName = null,
        string? ipAddress = null,
        bool success = true,
        string? resultMessage = null,
        object? details = null,
        CancellationToken cancellationToken = default);

    Task<AuditQueryResult> QueryAsync(
        string organizationId,
        int skip = 0,
        int take = 100,
        string? action = null,
        string? userName = null,
        string? targetId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);

    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}

public record AuditQueryResult(IReadOnlyList<AuditLogEntry> Entries, int TotalCount);

internal class AuditLogService : IAuditLogService
{
    private readonly IAppDbFactory _dbFactory;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(IAppDbFactory dbFactory, ILogger<AuditLogService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task LogAsync(
        string action,
        string organizationId,
        string? userName = null,
        string? userId = null,
        string? targetType = null,
        string? targetId = null,
        string? targetName = null,
        string? ipAddress = null,
        bool success = true,
        string? resultMessage = null,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(organizationId))
        {
            return;
        }

        try
        {
            await using var db = _dbFactory.GetContext();
            var entry = new AuditLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Action = action,
                OrganizationID = organizationId,
                UserName = userName,
                UserId = userId,
                TargetType = targetType,
                TargetId = targetId,
                TargetName = targetName,
                IpAddress = ipAddress,
                Success = success,
                ResultMessage = Truncate(resultMessage, 512),
                Details = details is null ? null : JsonSerializer.Serialize(details)
            };
            db.AuditLogEntries.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Never let audit log writes break the user-facing flow.
            _logger.LogWarning(ex, "Failed to write audit log entry for action {Action}.", action);
        }
    }

    public async Task<AuditQueryResult> QueryAsync(
        string organizationId,
        int skip = 0,
        int take = 100,
        string? action = null,
        string? userName = null,
        string? targetId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = _dbFactory.GetContext();
        var query = db.AuditLogEntries
            .Where(x => x.OrganizationID == organizationId);

        if (!string.IsNullOrWhiteSpace(action))   query = query.Where(x => x.Action == action);
        if (!string.IsNullOrWhiteSpace(userName)) query = query.Where(x => x.UserName == userName);
        if (!string.IsNullOrWhiteSpace(targetId)) query = query.Where(x => x.TargetId == targetId);
        if (from.HasValue) query = query.Where(x => x.Timestamp >= from.Value);
        if (to.HasValue)   query = query.Where(x => x.Timestamp <= to.Value);

        var total = await query.CountAsync(cancellationToken);
        var entries = await query
            .OrderByDescending(x => x.Timestamp)
            .Skip(skip)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        return new AuditQueryResult(entries, total);
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var db = _dbFactory.GetContext();
        return await db.AuditLogEntries
            .Where(x => x.Timestamp < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
        return value[..max];
    }
}
