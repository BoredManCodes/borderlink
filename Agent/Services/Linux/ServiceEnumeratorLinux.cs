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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Services.Linux;

/// <summary>
/// Enumerates systemd services. Uses
/// <c>systemctl list-units --type=service --all --no-pager --output=json</c>
/// when available; if systemd isn't installed we return an empty array.
/// </summary>
public class ServiceEnumeratorLinux : IServiceEnumerator
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<ServiceEnumeratorLinux> _logger;

    public ServiceEnumeratorLinux(ILogger<ServiceEnumeratorLinux> logger)
    {
        _logger = logger;
    }

    public Task<ServiceInfo[]> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var output = RunTool(
                "systemctl",
                "list-units --type=service --all --no-pager --output=json --plain --no-legend",
                cancellationToken);

            if (string.IsNullOrWhiteSpace(output))
            {
                return Array.Empty<ServiceInfo>();
            }

            try
            {
                using var doc = JsonDocument.Parse(output);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<ServiceInfo>();
                }

                var results = new List<ServiceInfo>();
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var name = element.TryGetProperty("unit", out var u) ? u.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var description = element.TryGetProperty("description", out var d) ? d.GetString() : null;
                    var active = element.TryGetProperty("active", out var a) ? a.GetString() : null;
                    var sub = element.TryGetProperty("sub", out var s) ? s.GetString() : null;

                    results.Add(new ServiceInfo(
                        name: name,
                        displayName: description,
                        description: description,
                        status: MapActive(active, sub),
                        startType: ServiceStartType.Other,
                        canStop: true,
                        canPauseAndContinue: false,
                        accountName: null,
                        processId: null));
                }

                return results
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse systemctl JSON output.");
                return Array.Empty<ServiceInfo>();
            }
        }, cancellationToken);
    }

    private static ServiceStatus MapActive(string? active, string? sub)
    {
        return (active ?? string.Empty).ToLowerInvariant() switch
        {
            "active" => string.Equals(sub, "exited", StringComparison.OrdinalIgnoreCase)
                ? ServiceStatus.Stopped
                : ServiceStatus.Running,
            "inactive" or "dead" or "failed" => ServiceStatus.Stopped,
            "activating" => ServiceStatus.Starting,
            "deactivating" => ServiceStatus.Stopping,
            _ => ServiceStatus.Other,
        };
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
                _logger.LogDebug("{tool} timed out.", fileName);
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
