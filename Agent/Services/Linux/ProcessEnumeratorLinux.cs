using BorderLink.Agent.Interfaces;
using BorderLink.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Services.Linux;

/// <summary>
/// Process enumeration via <c>ps -eo pid,ppid,user,rss,comm</c>.
/// </summary>
public class ProcessEnumeratorLinux : IProcessEnumerator
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<ProcessEnumeratorLinux> _logger;

    public ProcessEnumeratorLinux(ILogger<ProcessEnumeratorLinux> logger)
    {
        _logger = logger;
    }

    public Task<ProcessInfo[]> GetProcessesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var output = RunTool("ps", "-eo pid,ppid,user,rss,comm --no-headers", cancellationToken);
            if (string.IsNullOrWhiteSpace(output))
            {
                return Array.Empty<ProcessInfo>();
            }

            var results = new List<ProcessInfo>();
            foreach (var rawLine in output.Split('\n'))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.TrimStart().Split(new[] { ' ', '\t' }, 5, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                {
                    continue;
                }

                if (!int.TryParse(parts[0], out var pid))
                {
                    continue;
                }

                int? ppid = int.TryParse(parts[1], out var parsedPpid) ? parsedPpid : null;
                var user = parts[2];
                long rssKb = long.TryParse(parts[3], out var rss) ? rss : 0;
                var name = parts[4].Trim();

                results.Add(new ProcessInfo(
                    pid: pid,
                    name: name,
                    parentPid: ppid,
                    userName: user,
                    workingSetBytes: rssKb * 1024L,
                    cpuPercent: null,
                    startedAt: null));
            }

            return results
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    public Task<bool> KillAsync(int pid, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("kill", $"-TERM {pid}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                using var proc = Process.Start(psi);
                if (proc is null)
                {
                    return false;
                }

                proc.WaitForExit(5000);
                return proc.ExitCode == 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to kill PID {pid}.", pid);
                return false;
            }
        }, cancellationToken);
    }

    private string RunTool(string fileName, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return string.Empty;
            }

            if (!proc.WaitForExit(_timeout))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return string.Empty;
            }

            return proc.StandardOutput.ReadToEnd();
        }
        catch (Win32Exception)
        {
            return string.Empty;
        }
        catch (FileNotFoundException)
        {
            return string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Error invoking {tool}.", fileName);
            return string.Empty;
        }
    }
}
