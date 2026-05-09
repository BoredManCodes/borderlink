using MessagePack;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace BorderLink.Shared;

/// <summary>
/// Represents a single piece of software installed on a managed device.
/// Surfaced read-only by the inventory feature.
/// </summary>
[DataContract]
public class InstalledApp
{
    [SerializationConstructor]
    [JsonConstructor]
    public InstalledApp(
        string name,
        string? version,
        string? publisher,
        DateTime? installDate,
        string source,
        string? uninstallCommand,
        string? architecture)
    {
        Name = name;
        Version = version;
        Publisher = publisher;
        InstallDate = installDate;
        Source = source;
        UninstallCommand = uninstallCommand;
        Architecture = architecture;
    }

    public InstalledApp() : this(string.Empty, null, null, null, string.Empty, null, null)
    {
    }

    [DataMember(Order = 0)]
    public string Name { get; set; }

    [DataMember(Order = 1)]
    public string? Version { get; set; }

    [DataMember(Order = 2)]
    public string? Publisher { get; set; }

    [DataMember(Order = 3)]
    public DateTime? InstallDate { get; set; }

    /// <summary>
    /// Source of this entry. One of: "registry", "winget", "dpkg", "rpm",
    /// "flatpak", "snap", "system_profiler", "brew".
    /// </summary>
    [DataMember(Order = 4)]
    public string Source { get; set; }

    [DataMember(Order = 5)]
    public string? UninstallCommand { get; set; }

    [DataMember(Order = 6)]
    public string? Architecture { get; set; }
}
