using BorderLink.Shared.Enums;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BorderLink.Shared.Entities;

/// <summary>
/// Persisted record of a single patch install request, mirroring
/// <see cref="ScriptRun"/> in shape. Each row covers one (device, update)
/// pair; the agent updates Status as the install progresses via
/// <c>ReportPatchInstallProgress</c>.
/// </summary>
public class PatchInstallRun
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    [StringLength(128)]
    public string DeviceID { get; set; } = string.Empty;

    public string OrganizationID { get; set; } = string.Empty;

    [JsonIgnore]
    public Organization? Organization { get; set; }

    [StringLength(256)]
    public string UpdateId { get; set; } = string.Empty;

    [StringLength(512)]
    public string UpdateTitle { get; set; } = string.Empty;

    [StringLength(450)]
    public string? InitiatorId { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public PatchInstallStatus Status { get; set; }

    public bool RebootRequired { get; set; }

    [StringLength(1024)]
    public string? Notes { get; set; }
}
