using BorderLink.Shared.Enums;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BorderLink.Shared.Entities;

/// <summary>
/// User-defined monitor rule. Evaluated by <c>MonitorEvaluator</c> against
/// recent <see cref="DeviceMetricHistory"/> rows (or device state, for
/// <see cref="MonitorMetric.AgentOffline"/>). Multi-tenant — every query
/// must scope by <see cref="OrganizationID"/>.
/// </summary>
public class MonitorRule
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    public string OrganizationID { get; set; } = string.Empty;

    [JsonIgnore]
    public Organization? Organization { get; set; }

    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    public MonitorMetric Metric { get; set; }

    public MonitorOperator Operator { get; set; }

    public double Threshold { get; set; }

    public int DurationSeconds { get; set; }

    [StringLength(200)]
    public string? DeviceFilterTag { get; set; }

    [StringLength(64)]
    public string? DeviceGroupId { get; set; }

    public MonitorChannel Channel { get; set; }

    [StringLength(500)]
    public string? ChannelTarget { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastFiredAt { get; set; }

    public int CooldownMinutes { get; set; } = 30;
}
