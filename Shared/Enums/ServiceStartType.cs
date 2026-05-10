namespace BorderLink.Shared.Enums;

/// <summary>
/// Normalized service start mode. Maps from Win32 service StartType, systemd
/// UnitFileState (enabled/disabled/static), and launchctl Disabled state.
/// </summary>
public enum ServiceStartType
{
    Other = 0,
    Auto = 1,
    Manual = 2,
    Disabled = 3,
    Boot = 4,
    System = 5,
}
