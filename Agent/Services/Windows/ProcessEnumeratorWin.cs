using BorderLink.Agent.Interfaces;
using BorderLink.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Services.Windows;

/// <summary>
/// Process enumeration via <see cref="System.Diagnostics.Process"/>. We don't
/// expose CpuPercent here — sampling CPU per-process is expensive and would
/// dwarf the cost of the rest of the call. The Processes tab can render
/// without it.
/// </summary>
[SupportedOSPlatform("windows")]
public class ProcessEnumeratorWin : IProcessEnumerator
{
    private readonly ILogger<ProcessEnumeratorWin> _logger;

    public ProcessEnumeratorWin(ILogger<ProcessEnumeratorWin> logger)
    {
        _logger = logger;
    }

    public Task<ProcessInfo[]> GetProcessesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                return Process.GetProcesses()
                    .Select(p =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            DateTime? startedAt = null;
                            try { startedAt = p.StartTime; }
                            catch { /* Access denied for some system processes. */ }

                            return new ProcessInfo(
                                pid: p.Id,
                                name: p.ProcessName,
                                parentPid: null,
                                userName: null,
                                workingSetBytes: p.WorkingSet64,
                                cpuPercent: null,
                                startedAt: startedAt);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to read process {pid}.", p.Id);
                            return null;
                        }
                        finally
                        {
                            p.Dispose();
                        }
                    })
                    .Where(x => x is not null)
                    .Cast<ProcessInfo>()
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enumerate processes.");
                return Array.Empty<ProcessInfo>();
            }
        }, cancellationToken);
    }

    public Task<bool> KillAsync(int pid, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(5000);
                return proc.HasExited;
            }
            catch (ArgumentException)
            {
                // Process already exited.
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill PID {pid}.", pid);
                return false;
            }
        }, cancellationToken);
    }
}
