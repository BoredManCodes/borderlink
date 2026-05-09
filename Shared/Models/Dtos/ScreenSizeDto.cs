using System.Runtime.Serialization;

namespace BorderLink.Shared.Models.Dtos;

[DataContract]
public class ScreenSizeDto
{
    public ScreenSizeDto(int width, int height)
    {
        Width = width;
        Height = height;
    }

    [DataMember(Name = "Width")]
    public int Width { get; }

    [DataMember(Name = "Height")]
    public int Height { get; }
}
