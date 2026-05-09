using System.Runtime.Serialization;

namespace BorderLink.Shared.Models.Dtos;

[DataContract]
public class ToggleBlockInputDto
{
    [DataMember(Name = "ToggleOn")]
    public bool ToggleOn { get; set; }
}
