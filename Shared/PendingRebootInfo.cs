using MessagePack;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace BorderLink.Shared;

/// <summary>
/// Result of a reboot-pending probe. Reasons are short tokens
/// (e.g. "CBS", "WindowsUpdate", "PendingFileRenameOperations") for
/// upstream display and alert correlation.
/// </summary>
[MessagePackObject]
[DataContract]
public class PendingRebootInfo
{
    [SerializationConstructor]
    [JsonConstructor]
    public PendingRebootInfo(bool isPending, string[] reasons)
    {
        IsPending = isPending;
        Reasons = reasons ?? System.Array.Empty<string>();
    }

    public PendingRebootInfo() : this(false, System.Array.Empty<string>())
    {
    }

    [Key(0)]
    [DataMember(Order = 0)]
    public bool IsPending { get; set; }

    [Key(1)]
    [DataMember(Order = 1)]
    public string[] Reasons { get; set; }
}
