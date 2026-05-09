using System.Runtime.Serialization;

namespace BorderLink.Shared.Models.Dtos;

[DataContract]
public class FrameReceivedDto
{
    [DataMember]
    public long Timestamp { get; set; }
}
