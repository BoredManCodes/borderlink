using BorderLink.Shared;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Interfaces;

/// <summary>
/// Per-OS search across the available package managers
/// (winget/choco/apt/brew). Implementations are best-effort and
/// feature-detect each manager — absent tools yield empty results, never
/// exceptions.
/// </summary>
public interface IPackageSearcher
{
    Task<List<SoftwarePackage>> Search(string query, int max, CancellationToken cancellationToken = default);
}
