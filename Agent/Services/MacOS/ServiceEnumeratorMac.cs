using BorderLink.Agent.Interfaces;
using BorderLink.Shared;
using BorderLink.Shared.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Services.MacOS;

/// <summary>
/// Enumerates launchd jobs via <c>launchctl list</c>. Output is three columns:
/// PID, exit status, label. Numeric PID =&gt; running. Non-numeric (a dash) =&gt;
/// not currently loaded as a running process.
/// </summary>
public class ServiceEnumeratorMac : IServiceEnumerator
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<ServiceEnumeratorMac> _logger;

    public ServiceEnumeratorMac(ILogger<ServiceEnumeratorMac> logger)
    {
        _logger = logger;
    }

    public Task<ServiceInfo[]> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var output = RunTool("launchctl", "list", cancellationToken);
            if (string.IsNullOrWhiteSpace(output))
            {
                return Array.Empty<ServiceInfo>();
            }

            var results = new List<ServiceInfo>();
            var first = true;
            foreach (var rawLine in output.Split('\n'))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (first)
                {
                    first = false;
                    if (line.StartsWith("PID", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                var parts = line.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                {
                    parts = line.Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries);
                }
                if (parts.Length < 3)
                {
                    continue;
                }

                var pidRaw = parts[0].Trim();
                var label = parts[2].Trim();

                int? pid = int.TryParse(pidRaw, out var parsedPid) ? parsedPid : null;
                var status = pid.HasValue ? ServiceStatus.Running : ServiceStatus.Stopped;

                results.Add(new ServiceInfo(
                    name: label,
                    displayName: label,
                    description: null,
                    status: status,
                    startType: ServiceStartType.Other,
                    canStop: pid.HasValue,
                    canPauseAndContinue: false,
                    accountName: null,
                    processId: pid));
            }

            return results
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
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
