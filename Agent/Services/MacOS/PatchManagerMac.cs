using BorderLink.Agent.Interfaces;
using BorderLink.Shared;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Services.MacOS;

// macOS patching requires the gated, slow `softwareupdate` CLI; deferring to a future phase.
public class PatchManagerMac : IPatchManager
{
    public Task<PatchUpdate[]> GetPendingUpdatesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Array.Empty<PatchUpdate>());

    public Task<bool> InstallUpdateAsync(
        string updateId,
        IProgress<PatchInstallProgress> progress,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<PendingRebootInfo> GetPendingRebootAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PendingRebootInfo(false, Array.Empty<string>()));
}
