using System.ComponentModel.DataAnnotations;

namespace BorderLink.Shared.Entities;

public class InventoryRefreshSchedule
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string OrganizationID { get; set; } = string.Empty;

    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    public int IntervalHours { get; set; } = 24;

    public string? DeviceGroupId { get; set; }

    [StringLength(200)]
    public string? DeviceTagFilter { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTimeOffset? LastRunAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
