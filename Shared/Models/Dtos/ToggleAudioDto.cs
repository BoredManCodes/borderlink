using System.Runtime.Serialization;

namespace BorderLink.Shared.Models.Dtos;

[DataContract]
public class ToggleAudioDto
{
    [DataMember(Name = "ToggleOn")]
    public bool ToggleOn { get; set; }
}
