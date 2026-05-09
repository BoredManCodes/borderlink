using BorderLink.Desktop.Shared.Abstractions;
using BorderLink.Shared.Models;
using System.Drawing;

namespace BorderLink.Desktop.Linux.Services;

public class CursorIconWatcherLinux : ICursorIconWatcher
{
#pragma warning disable CS0067
    public event EventHandler<CursorInfo>? OnChange;
#pragma warning restore


    public CursorInfo GetCurrentCursor() => new(Array.Empty<byte>(), Point.Empty, "default");
}
