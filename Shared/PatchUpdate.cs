using BorderLink.Shared.Enums;
using MessagePack;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace BorderLink.Shared;

/// <summary>
/// A pending OS update reported by an agent. <see cref="Id"/> is platform
/// specific — Windows uses the COM <c>UpdateID</c> GUID string, Linux uses
/// the package name.
/// </summary>
[MessagePackObject]
[DataContract]
public class PatchUpdate
{
    [SerializationConstructor]
    [JsonConstructor]
    public PatchUpdate(
        string id,
        string title,
        string? kbNumber,
        string? description,
        PatchSeverity severity,
        long sizeBytes,
        bool rebootRequired,
        DateTime? publishedAt,
        bool isDownloaded,
        bool isInstalled)
    {
        Id = id;
        Title = title;
        KbNumber = kbNumber;
        Description = description;
        Severity = severity;
        SizeBytes = sizeBytes;
        RebootRequired = rebootRequired;
        PublishedAt = publishedAt;
        IsDownloaded = isDownloaded;
        IsInstalled = isInstalled;
    }

    public PatchUpdate() : this(
        string.Empty,
        string.Empty,
        null,
        null,
        PatchSeverity.Unknown,
        0,
        false,
        null,
        false,
        false)
    {
    }

    [Key(0)]
    [DataMember(Order = 0)]
    public string Id { get; set; }

    [Key(1)]
    [DataMember(Order = 1)]
    public string Title { get; set; }

    [Key(2)]
    [DataMember(Order = 2)]
    public string? KbNumber { get; set; }

    [Key(3)]
    [DataMember(Order = 3)]
    public string? Description { get; set; }

    [Key(4)]
    [DataMember(Order = 4)]
    public PatchSeverity Severity { get; set; }

    [Key(5)]
    [DataMember(Order = 5)]
    public long SizeBytes { get; set; }

    [Key(6)]
    [DataMember(Order = 6)]
    public bool RebootRequired { get; set; }

    [Key(7)]
    [DataMember(Order = 7)]
    public DateTime? PublishedAt { get; set; }

    [Key(8)]
    [DataMember(Order = 8)]
    public bool IsDownloaded { get; set; }

    [Key(9)]
    [DataMember(Order = 9)]
    public bool IsInstalled { get; set; }
}
