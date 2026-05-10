using BorderLink.Shared.Enums;
using MessagePack;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace BorderLink.Shared;

/// <summary>
/// Streaming progress event for an in-flight patch install. Pushed
/// agent-to-server out of band — the originating <c>InstallUpdate</c> hub
/// call returns once the action is queued, not once it completes.
/// </summary>
[MessagePackObject]
[DataContract]
public class PatchInstallProgress
{
    [SerializationConstructor]
    [JsonConstructor]
    public PatchInstallProgress(
        string deviceID,
        string updateId,
        PatchInstallPhase phase,
        int percentComplete,
        string? message)
    {
        DeviceID = deviceID;
        UpdateId = updateId;
        Phase = phase;
        PercentComplete = percentComplete;
        Message = message;
    }

    public PatchInstallProgress() : this(
        string.Empty,
        string.Empty,
        PatchInstallPhase.Downloading,
        0,
        null)
    {
    }

    [Key(0)]
    [DataMember(Order = 0)]
    public string DeviceID { get; set; }

    [Key(1)]
    [DataMember(Order = 1)]
    public string UpdateId { get; set; }

    [Key(2)]
    [DataMember(Order = 2)]
    public PatchInstallPhase Phase { get; set; }

    [Key(3)]
    [DataMember(Order = 3)]
    public int PercentComplete { get; set; }

    [Key(4)]
    [DataMember(Order = 4)]
    public string? Message { get; set; }
}
