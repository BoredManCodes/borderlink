using BorderLink.Shared.Enums;
using MessagePack;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace BorderLink.Shared;

/// <summary>
/// Snapshot of a single service/daemon on a managed device. Mirrors
/// <see cref="InstalledApp"/> in shape — read-only DTO returned by the agent.
/// </summary>
[MessagePackObject]
[DataContract]
public class ServiceInfo
{
    [SerializationConstructor]
    [JsonConstructor]
    public ServiceInfo(
        string name,
        string? displayName,
        string? description,
        ServiceStatus status,
        ServiceStartType startType,
        bool canStop,
        bool canPauseAndContinue,
        string? accountName,
        int? processId)
    {
        Name = name;
        DisplayName = displayName;
        Description = description;
        Status = status;
        StartType = startType;
        CanStop = canStop;
        CanPauseAndContinue = canPauseAndContinue;
        AccountName = accountName;
        ProcessId = processId;
    }

    public ServiceInfo() : this(
        string.Empty,
        null,
        null,
        ServiceStatus.Other,
        ServiceStartType.Other,
        false,
        false,
        null,
        null)
    {
    }

    /// <summary>
    /// Unique service identifier on the device. Win32 service short name on
    /// Windows (e.g. <c>Spooler</c>), unit name on Linux (e.g.
    /// <c>cups.service</c>), label on macOS (e.g. <c>com.apple.Finder</c>).
    /// </summary>
    [Key(0)]
    [DataMember(Order = 0)]
    public string Name { get; set; }

    [Key(1)]
    [DataMember(Order = 1)]
    public string? DisplayName { get; set; }

    [Key(2)]
    [DataMember(Order = 2)]
    public string? Description { get; set; }

    [Key(3)]
    [DataMember(Order = 3)]
    public ServiceStatus Status { get; set; }

    [Key(4)]
    [DataMember(Order = 4)]
    public ServiceStartType StartType { get; set; }

    [Key(5)]
    [DataMember(Order = 5)]
    public bool CanStop { get; set; }

    [Key(6)]
    [DataMember(Order = 6)]
    public bool CanPauseAndContinue { get; set; }

    [Key(7)]
    [DataMember(Order = 7)]
    public string? AccountName { get; set; }

    [Key(8)]
    [DataMember(Order = 8)]
    public int? ProcessId { get; set; }
}
