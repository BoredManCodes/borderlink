using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace BorderLink.Shared.Entities;

/// <summary>
/// Records the intent behind a single install/uninstall action. Each row
/// is paired 1:1 with a <see cref="ScriptRun"/> (the underlying delivery
/// vehicle); when the agent fetches the well-known SavedScript content,
/// the server substitutes <see cref="PackageId"/> into the parameterised
/// command template using the linked <c>ScriptRunId</c>.
/// </summary>
public class SoftwareActionRun
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int ScriptRunId { get; set; }

    [JsonIgnore]
    public ScriptRun? ScriptRun { get; set; }

    [Required]
    public string DeviceID { get; set; } = null!;

    public SoftwareActionKind Kind { get; set; }

    public SoftwareActionSource Source { get; set; }

    [Required]
    [StringLength(256)]
    public string PackageId { get; set; } = null!;

    [StringLength(256)]
    public string? PackageName { get; set; }

    public string OrganizationID { get; set; } = null!;

    [JsonIgnore]
    public Organization? Organization { get; set; }

    [StringLength(450)]
    public string? InitiatorId { get; set; }

    [JsonIgnore]
    public BorderLinkUser? Initiator { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [StringLength(256)]
    public string? Notes { get; set; }
}
