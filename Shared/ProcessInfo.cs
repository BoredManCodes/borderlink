using MessagePack;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace BorderLink.Shared;

/// <summary>
/// Lightweight running-process descriptor. Used by the Processes tab — point-in-time
/// snapshot, not persisted.
/// </summary>
[MessagePackObject]
[DataContract]
public class ProcessInfo
{
    [SerializationConstructor]
    [JsonConstructor]
    public ProcessInfo(
        int pid,
        string name,
        int? parentPid,
        string? userName,
        long workingSetBytes,
        double? cpuPercent,
        DateTime? startedAt)
    {
        Pid = pid;
        Name = name;
        ParentPid = parentPid;
        UserName = userName;
        WorkingSetBytes = workingSetBytes;
        CpuPercent = cpuPercent;
        StartedAt = startedAt;
    }

    public ProcessInfo() : this(0, string.Empty, null, null, 0, null, null)
    {
    }

    [Key(0)]
    [DataMember(Order = 0)]
    public int Pid { get; set; }

    [Key(1)]
    [DataMember(Order = 1)]
    public string Name { get; set; }

    [Key(2)]
    [DataMember(Order = 2)]
    public int? ParentPid { get; set; }

    [Key(3)]
    [DataMember(Order = 3)]
    public string? UserName { get; set; }

    [Key(4)]
    [DataMember(Order = 4)]
    public long WorkingSetBytes { get; set; }

    [Key(5)]
    [DataMember(Order = 5)]
    public double? CpuPercent { get; set; }

    [Key(6)]
    [DataMember(Order = 6)]
    public DateTime? StartedAt { get; set; }
}
