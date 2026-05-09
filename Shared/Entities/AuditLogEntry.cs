using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BorderLink.Shared.Entities;

public class AuditLogEntry
{
    [Key]
    public long Id { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [StringLength(64)]
    public string Action { get; set; } = string.Empty;

    [StringLength(256)]
    public string? UserName { get; set; }

    [StringLength(450)]
    public string? UserId { get; set; }

    public string OrganizationID { get; set; } = null!;

    [JsonIgnore]
    public Organization? Organization { get; set; }

    [StringLength(32)]
    public string? TargetType { get; set; }

    [StringLength(256)]
    public string? TargetId { get; set; }

    [StringLength(256)]
    public string? TargetName { get; set; }

    [StringLength(64)]
    public string? IpAddress { get; set; }

    public bool Success { get; set; } = true;

    [StringLength(512)]
    public string? ResultMessage { get; set; }

    public string? Details { get; set; }
}
