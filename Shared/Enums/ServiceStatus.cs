namespace BorderLink.Shared.Enums;

/// <summary>
/// Normalized service runtime state across Windows, Linux and macOS. Maps from
/// <c>System.ServiceProcess.ServiceControllerStatus</c>, systemd ActiveState
/// and launchctl state respectively.
/// </summary>
public enum ServiceStatus
{
    Other = 0,
    Running = 1,
    Stopped = 2,
    Paused = 3,
    Starting = 4,
    Stopping = 5,
}
