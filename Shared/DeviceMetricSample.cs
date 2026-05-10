using MessagePack;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace BorderLink.Shared;

/// <summary>
/// A single rolling-telemetry sample reported by an agent. Server-side
/// monitor rules evaluate <see cref="DeviceMetricHistory"/> rows derived
/// from these samples. <see cref="AgentOnline"/> is always <c>true</c>
/// when an agent itself reports a sample — offline detection is driven
/// off <see cref="Entities.Device.IsOnline"/> on the server.
/// </summary>
[DataContract]
public class DeviceMetricSample
{
    [SerializationConstructor]
    [JsonConstructor]
    public DeviceMetricSample(
        string deviceID,
        DateTimeOffset capturedAt,
        double cpuPercent,
        double usedMemoryPercent,
        double usedStoragePercent,
        bool agentOnline)
    {
        DeviceID = deviceID;
        CapturedAt = capturedAt;
        CpuPercent = cpuPercent;
        UsedMemoryPercent = usedMemoryPercent;
        UsedStoragePercent = usedStoragePercent;
        AgentOnline = agentOnline;
    }

    public DeviceMetricSample() : this(string.Empty, DateTimeOffset.UtcNow, 0, 0, 0, true)
    {
    }

    [DataMember(Order = 0)]
    public string DeviceID { get; set; }

    [DataMember(Order = 1)]
    public DateTimeOffset CapturedAt { get; set; }

    [DataMember(Order = 2)]
    public double CpuPercent { get; set; }

    [DataMember(Order = 3)]
    public double UsedMemoryPercent { get; set; }

    [DataMember(Order = 4)]
    public double UsedStoragePercent { get; set; }

    [DataMember(Order = 5)]
    public bool AgentOnline { get; set; }
}
