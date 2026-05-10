namespace BorderLink.Shared;

/// <summary>
/// Identifies which package management ecosystem a software action targets.
/// </summary>
public enum SoftwareActionSource
{
    Winget,
    Choco,
    Apt,
    Brew,
    Msi,
}
