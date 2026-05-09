using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BorderLink.Shared.Entities;

/// <summary>
/// A point-in-time capture of installed software on a device.
/// The <see cref="Apps"/> collection is persisted as JSON via a value
/// converter configured in <c>AppDb.OnModelCreating</c>.
/// </summary>
public class DeviceInventorySnapshot
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string DeviceID { get; set; } = null!;

    [JsonIgnore]
    public Device? Device { get; set; }

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<InstalledApp> Apps { get; set; } = new();
}
