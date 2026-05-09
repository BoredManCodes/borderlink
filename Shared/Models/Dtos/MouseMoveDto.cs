using System.Runtime.Serialization;

namespace BorderLink.Shared.Models.Dtos;

[DataContract]
public class MouseMoveDto
{

    [DataMember(Name = "PercentX")]
    public double PercentX { get; set; }

    [DataMember(Name = "PercentY")]
    public double PercentY { get; set; }
}
