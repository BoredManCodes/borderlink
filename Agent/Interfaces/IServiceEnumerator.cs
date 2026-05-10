using BorderLink.Shared;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Interfaces;

/// <summary>
/// Per-OS enumeration of installed services / daemons. Implementations are
/// read-only and must return an empty array (rather than throw) on transient
/// failures so the UI can render an "online but empty" state cleanly.
/// </summary>
public interface IServiceEnumerator
{
    Task<ServiceInfo[]> GetServicesAsync(CancellationToken cancellationToken = default);
}
