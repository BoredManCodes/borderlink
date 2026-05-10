using BorderLink.Shared;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Interfaces;

/// <summary>
/// Per-OS process enumeration plus a kill primitive.
/// </summary>
public interface IProcessEnumerator
{
    Task<ProcessInfo[]> GetProcessesAsync(CancellationToken cancellationToken = default);

    Task<bool> KillAsync(int pid, CancellationToken cancellationToken = default);
}
