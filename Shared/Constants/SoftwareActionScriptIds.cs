using System.Collections.Generic;

namespace BorderLink.Shared.Constants;

/// <summary>
/// Stable, hard-coded identifiers for the system-seeded SavedScript rows
/// that back software install/uninstall actions. The row's
/// <c>Content</c> is a parameterised one-liner with a <c>{0}</c>
/// placeholder; the server substitutes the package id at fetch time
/// (see <c>SavedScriptsController.GetScript</c>).
/// </summary>
public static class SoftwareActionScriptIds
{
    public static readonly Guid WingetUninstall = new("68d7eaad-e8ca-483e-9412-97ac289bafd4");
    public static readonly Guid WingetInstall   = new("7e827b82-6bc0-4e2d-9cb5-d1ba851a6a0f");
    public static readonly Guid ChocoUninstall  = new("1f3f6c2f-9f75-4ef1-8614-3a8fb91d61e5");
    public static readonly Guid ChocoInstall    = new("8414c326-695e-4594-9d75-f77459663d41");
    public static readonly Guid AptUninstall    = new("52d96e2e-8ba6-4ae1-bccb-7c7a22d6e119");
    public static readonly Guid AptInstall      = new("848943d1-9c6f-4230-a2b8-5824000e093f");
    public static readonly Guid BrewUninstall   = new("0a41b26a-b7d6-4dd4-86f2-d2319389339b");
    public static readonly Guid BrewInstall     = new("324aad36-4986-4914-a6f1-e07ee65a5d38");
    public static readonly Guid MsiUninstall    = new("4149bf46-2f67-4abf-9b9f-13155d5fb3b1");

    /// <summary>All well-known software-action script ids.</summary>
    public static readonly IReadOnlySet<Guid> All = new HashSet<Guid>
    {
        WingetUninstall, WingetInstall,
        ChocoUninstall, ChocoInstall,
        AptUninstall, AptInstall,
        BrewUninstall, BrewInstall,
        MsiUninstall,
    };

    /// <summary>
    /// Resolve the well-known SavedScript id for a given source/kind, or
    /// <c>null</c> if no scripted action is supported (e.g. MSI install).
    /// </summary>
    public static Guid? Resolve(SoftwareActionSource source, SoftwareActionKind kind)
    {
        return (source, kind) switch
        {
            (SoftwareActionSource.Winget, SoftwareActionKind.Uninstall) => WingetUninstall,
            (SoftwareActionSource.Winget, SoftwareActionKind.Install)   => WingetInstall,
            (SoftwareActionSource.Choco,  SoftwareActionKind.Uninstall) => ChocoUninstall,
            (SoftwareActionSource.Choco,  SoftwareActionKind.Install)   => ChocoInstall,
            (SoftwareActionSource.Apt,    SoftwareActionKind.Uninstall) => AptUninstall,
            (SoftwareActionSource.Apt,    SoftwareActionKind.Install)   => AptInstall,
            (SoftwareActionSource.Brew,   SoftwareActionKind.Uninstall) => BrewUninstall,
            (SoftwareActionSource.Brew,   SoftwareActionKind.Install)   => BrewInstall,
            (SoftwareActionSource.Msi,    SoftwareActionKind.Uninstall) => MsiUninstall,
            _ => null,
        };
    }
}
