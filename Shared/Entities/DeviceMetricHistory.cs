using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BorderLink.Shared.Entities;

/// <summary>
/// Persisted rolling-telemetry row. Written by the agent hub on every
/// <see cref="DeviceMetricSample"/> push and pruned by the data-cleanup
/// service. <see cref="OrganizationID"/> is denormalized so monitor-rule
/// evaluation and pruning never need to join through Device.
/// </summary>
public class DeviceMetricHistory
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    public string DeviceID { get; set; } = string.Empty;

    [JsonIgnore]
    public Device? Device { get; set; }

    public DateTimeOffset CapturedAt { get; set; }

    public double CpuPercent { get; set; }

    public double UsedMemoryPercent { get; set; }

    public double UsedStoragePercent { get; set; }

    public string OrganizationID { get; set; } = string.Empty;
}
