using BorderLink.Shared.Models;
using System;

namespace BorderLink.Desktop.Core.Interfaces
{
    public interface ICursorIconWatcher
    {
        event EventHandler<CursorInfo> OnChange;

        CursorInfo GetCurrentCursor();
    }

}
