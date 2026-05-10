using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BorderLink.Shared.Entities;

/// <summary>
/// Audit-style log of every (rule, device) firing. The cooldown check on
/// <c>MonitorEvaluator</c> reads from this — without it, a rule could
/// re-fire every evaluation tick while the underlying condition holds.
/// </summary>
public class MonitorRuleFiring
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid MonitorRuleId { get; set; }

    [JsonIgnore]
    public MonitorRule? MonitorRule { get; set; }

    public string DeviceID { get; set; } = string.Empty;

    [JsonIgnore]
    public Device? Device { get; set; }

    public string OrganizationID { get; set; } = string.Empty;

    public DateTimeOffset FiredAt { get; set; } = DateTimeOffset.UtcNow;

    public double ValueAtFire { get; set; }
}
