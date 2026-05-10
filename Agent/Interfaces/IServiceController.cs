using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Interfaces;

/// <summary>
/// Per-OS service lifecycle control (start / stop / restart). Returns
/// <c>true</c> only when the underlying tool reports success.
/// </summary>
public interface IServiceController
{
    Task<bool> StartAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> StopAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> RestartAsync(string name, CancellationToken cancellationToken = default);
}
