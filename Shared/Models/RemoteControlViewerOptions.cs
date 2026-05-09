using System.Runtime.Serialization;

namespace BorderLink.Shared.Models;

[DataContract]
public class RemoteControlViewerOptions
{
    [DataMember]
    public bool ShouldRecordSession { get; init; }
}
