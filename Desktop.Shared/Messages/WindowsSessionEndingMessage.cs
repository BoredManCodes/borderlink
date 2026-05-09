using BorderLink.Shared.Enums;

namespace BorderLink.Desktop.Shared.Messages;

public class WindowsSessionEndingMessage
{
    public WindowsSessionEndingMessage(SessionEndReasonsEx reason)
    {
        Reason = reason;
    }

    public SessionEndReasonsEx Reason { get; }
}
