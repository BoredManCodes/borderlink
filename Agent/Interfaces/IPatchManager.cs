using BorderLink.Shared;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Interfaces;

public interface IPatchManager
{
    /// <summary>
    /// Returns the list of pending OS updates available to install on this
    /// device. Empty array when the platform has no native update source
    /// or the query fails.
    /// </summary>
    Task<PatchUpdate[]> GetPendingUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Download and install the update with the given platform-specific id
    /// (Windows: UpdateID GUID; Linux: package name). Progress is streamed
    /// through the supplied <paramref name="progress"/> callback. Returns
    /// <c>true</c> if the install completed successfully.
    /// </summary>
    Task<bool> InstallUpdateAsync(
        string updateId,
        IProgress<PatchInstallProgress> progress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Probe for pending-reboot signals (CBS / WindowsUpdate /
    /// PendingFileRenameOperations on Windows; reboot-required on Linux).
    /// </summary>
    Task<PendingRebootInfo> GetPendingRebootAsync(CancellationToken cancellationToken = default);
}
