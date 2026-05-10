using MessagePack;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace BorderLink.Shared;

/// <summary>
/// A package available for installation, returned by the agent's package
/// search (winget, choco, apt, brew). Used by the install picker UI; not
/// persisted.
/// </summary>
[MessagePackObject]
[DataContract]
public class SoftwarePackage
{
    [SerializationConstructor]
    [JsonConstructor]
    public SoftwarePackage(
        string id,
        string name,
        string? version,
        string? publisher,
        string source,
        string? description)
    {
        Id = id;
        Name = name;
        Version = version;
        Publisher = publisher;
        Source = source;
        Description = description;
    }

    public SoftwarePackage() : this(string.Empty, string.Empty, null, null, string.Empty, null)
    {
    }

    /// <summary>
    /// Package id understood by the underlying package manager (e.g.
    /// <c>Git.Git</c> for winget, <c>git</c> for apt/brew/choco).
    /// </summary>
    [Key(0)]
    [DataMember(Order = 0)]
    public string Id { get; set; }

    [Key(1)]
    [DataMember(Order = 1)]
    public string Name { get; set; }

    [Key(2)]
    [DataMember(Order = 2)]
    public string? Version { get; set; }

    [Key(3)]
    [DataMember(Order = 3)]
    public string? Publisher { get; set; }

    /// <summary>
    /// Source ecosystem string. One of: "winget", "choco", "apt", "brew".
    /// </summary>
    [Key(4)]
    [DataMember(Order = 4)]
    public string Source { get; set; }

    [Key(5)]
    [DataMember(Order = 5)]
    public string? Description { get; set; }
}
