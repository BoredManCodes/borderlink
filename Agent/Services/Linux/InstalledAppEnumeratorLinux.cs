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
/// Probes for dpkg, rpm, flatpak and snap and aggregates whichever package
/// managers are present. Each tool is invoked with a hard timeout; absent
/// tools are silently skipped.
/// </summary>
public class InstalledAppEnumeratorLinux : IInstalledAppEnumerator
{
    private static readonly TimeSpan _perToolTimeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<InstalledAppEnumeratorLinux> _logger;

    public InstalledAppEnumeratorLinux(ILogger<InstalledAppEnumeratorLinux> logger)
    {
        _logger = logger;
    }

    public Task<List<InstalledApp>> GetInstalledApps(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var apps = new List<InstalledApp>();

            apps.AddRange(EnumerateDpkg(cancellationToken));
            apps.AddRange(EnumerateRpm(cancellationToken));
            apps.AddRange(EnumerateFlatpak(cancellationToken));
            apps.AddRange(EnumerateSnap(cancellationToken));

            return apps
                .GroupBy(x => $"{x.Source}|{x.Name}|{x.Version}", StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    private IEnumerable<InstalledApp> EnumerateDpkg(CancellationToken cancellationToken)
    {
        var output = RunTool(
            "dpkg-query",
            "-W -f=${Package}\\t${Version}\\t${Maintainer}\\n",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(output))
        {
            yield break;
        }

        foreach (var line in output.Split('\n'))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var parts = trimmed.Split('\t');
            if (parts.Length < 1 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            yield return new InstalledApp(
                name: parts[0],
                version: parts.Length > 1 ? NullIfEmpty(parts[1]) : null,
                publisher: parts.Length > 2 ? NullIfEmpty(parts[2]) : null,
                installDate: null,
                source: "dpkg",
                uninstallCommand: $"apt-get remove -y {parts[0]}",
                architecture: null);
        }
    }

    private IEnumerable<InstalledApp> EnumerateRpm(CancellationToken cancellationToken)
    {
        var output = RunTool(
            "rpm",
            "-qa --qf %{NAME}\\t%{VERSION}\\t%{VENDOR}\\n",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(output))
        {
            yield break;
        }

        foreach (var line in output.Split('\n'))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var parts = trimmed.Split('\t');
            if (parts.Length < 1 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            yield return new InstalledApp(
                name: parts[0],
                version: parts.Length > 1 ? NullIfEmpty(parts[1]) : null,
                publisher: parts.Length > 2 ? NullIfEmpty(parts[2]) : null,
                installDate: null,
                source: "rpm",
                uninstallCommand: $"rpm -e {parts[0]}",
                architecture: null);
        }
    }

    private IEnumerable<InstalledApp> EnumerateFlatpak(CancellationToken cancellationToken)
    {
        var output = RunTool("flatpak", "list --app --columns=name,version,application", cancellationToken);
        if (string.IsNullOrWhiteSpace(output))
        {
            yield break;
        }

        foreach (var line in output.Split('\n'))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            // flatpak emits tab-separated columns when piped.
            var parts = trimmed.Split('\t');
            if (parts.Length < 1 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            var name = parts[0];
            var version = parts.Length > 1 ? NullIfEmpty(parts[1]) : null;
            var appId = parts.Length > 2 ? NullIfEmpty(parts[2]) : null;
            var uninstall = appId is null ? null : $"flatpak uninstall -y {appId}";

            yield return new InstalledApp(
                name: name,
                version: version,
                publisher: null,
                installDate: null,
                source: "flatpak",
                uninstallCommand: uninstall,
                architecture: null);
        }
    }

    private IEnumerable<InstalledApp> EnumerateSnap(CancellationToken cancellationToken)
    {
        var output = RunTool("snap", "list", cancellationToken);
        if (string.IsNullOrWhiteSpace(output))
        {
            yield break;
        }

        var lines = output.Split('\n');
        // First non-blank line is the header: Name  Version  Rev  Tracking  Publisher  Notes
        var startIndex = 0;
        for (; startIndex < lines.Length; startIndex++)
        {
            var l = lines[startIndex].TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(l) &&
                l.StartsWith("Name", StringComparison.OrdinalIgnoreCase))
            {
                startIndex++;
                break;
            }
        }

        for (var i = startIndex; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trimmed = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1)
            {
                continue;
            }

            var name = parts[0];
            var version = parts.Length > 1 ? parts[1] : null;
            var publisher = parts.Length > 4 ? parts[4] : null;

            yield return new InstalledApp(
                name: name,
                version: version,
                publisher: publisher,
                installDate: null,
                source: "snap",
                uninstallCommand: $"snap remove {name}",
                architecture: null);
        }
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
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

            if (!proc.WaitForExit(_perToolTimeout))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                _logger.LogDebug("{tool} timed out after {seconds}s.", fileName, _perToolTimeout.TotalSeconds);
                return string.Empty;
            }

            return proc.StandardOutput.ReadToEnd();
        }
        catch (Win32Exception)
        {
            // Tool not installed.
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
