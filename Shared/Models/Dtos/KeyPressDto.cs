using System.Runtime.Serialization;

namespace BorderLink.Shared.Models.Dtos;

[DataContract]
public class KeyPressDto
{
    [DataMember(Name = "Key")]
    public string Key { get; set; } = string.Empty;
}
