using BorderLink.Shared;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Interfaces;

/// <summary>
/// Per-OS enumeration of installed software. Implementations are read-only,
/// best-effort, and feature-detect optional package managers.
/// </summary>
public interface IInstalledAppEnumerator
{
    Task<List<InstalledApp>> GetInstalledApps(CancellationToken cancellationToken = default);
}
