using BorderLink.Agent.Interfaces;
using BorderLink.Shared;
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
using System.Xml;

namespace BorderLink.Agent.Services.MacOS;

/// <summary>
/// Enumerates macOS applications via <c>system_profiler SPApplicationsDataType</c>
/// (preferring JSON output when available, falling back to plist XML), and
/// optionally merges <c>brew list --versions</c> output.
/// </summary>
public class InstalledAppEnumeratorMac : IInstalledAppEnumerator
{
    private static readonly TimeSpan _systemProfilerTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan _brewTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<InstalledAppEnumeratorMac> _logger;

    public InstalledAppEnumeratorMac(ILogger<InstalledAppEnumeratorMac> logger)
    {
        _logger = logger;
    }

    public Task<List<InstalledApp>> GetInstalledApps(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in EnumerateSystemProfiler(cancellationToken))
            {
                var key = $"{entry.Name}|{entry.Version}";
                apps[key] = entry;
            }

            foreach (var entry in EnumerateBrew(cancellationToken))
            {
                var key = $"{entry.Name}|{entry.Version}";
                if (!apps.ContainsKey(key))
                {
                    apps[key] = entry;
                }
            }

            return apps.Values
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    private IEnumerable<InstalledApp> EnumerateSystemProfiler(CancellationToken cancellationToken)
    {
        // Try JSON first — supported on Big Sur (11) and later.
        var json = RunTool(
            "system_profiler",
            "SPApplicationsDataType -json",
            _systemProfilerTimeout,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(json))
        {
            foreach (var entry in ParseSystemProfilerJson(json))
            {
                yield return entry;
            }

            yield break;
        }

        var xml = RunTool(
            "system_profiler",
            "SPApplicationsDataType -xml",
            _systemProfilerTimeout,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(xml))
        {
            yield break;
        }

        foreach (var entry in ParseSystemProfilerPlist(xml))
        {
            yield return entry;
        }
    }

    private IEnumerable<InstalledApp> ParseSystemProfilerJson(string json)
    {
        List<InstalledApp> results = new();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("SPApplicationsDataType", out var apps))
            {
                return results;
            }

            foreach (var app in apps.EnumerateArray())
            {
                var name = app.TryGetProperty("_name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var version = app.TryGetProperty("version", out var v) ? v.GetString() : null;
                var path = app.TryGetProperty("path", out var p) ? p.GetString() : null;
                var arch = app.TryGetProperty("arch_kind", out var a) ? a.GetString() : null;
                var publisher = app.TryGetProperty("info", out var i) ? i.GetString() : null;

                DateTime? installDate = null;
                if (app.TryGetProperty("lastModified", out var lm) &&
                    lm.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(lm.GetString(), out var parsed))
                {
                    installDate = parsed;
                }

                results.Add(new InstalledApp(
                    name: name!,
                    version: version,
                    publisher: publisher,
                    installDate: installDate,
                    source: "system_profiler",
                    uninstallCommand: !string.IsNullOrWhiteSpace(path)
                        ? $"rm -rf \"{path}\""
                        : null,
                    architecture: arch));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse system_profiler JSON output.");
        }

        return results;
    }

    private IEnumerable<InstalledApp> ParseSystemProfilerPlist(string xml)
    {
        List<InstalledApp> results = new();

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            // The plist for SPApplicationsDataType is:
            // <plist><array><dict><key>_items</key><array><dict>...</dict></array>...
            var dicts = doc.SelectNodes("//array/dict/array/dict");
            if (dicts is null)
            {
                return results;
            }

            foreach (XmlNode dict in dicts)
            {
                var entry = ParsePlistDict(dict);
                if (entry is not null)
                {
                    results.Add(entry);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse system_profiler XML output.");
        }

        return results;
    }

    private static InstalledApp? ParsePlistDict(XmlNode dict)
    {
        // dict children alternate <key>...</key> followed by a value element.
        string? name = null;
        string? version = null;
        string? path = null;
        string? arch = null;
        string? info = null;
        DateTime? lastModified = null;

        XmlNode? keyNode = null;
        foreach (XmlNode child in dict.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (child.LocalName.Equals("key", StringComparison.Ordinal))
            {
                keyNode = child;
                continue;
            }

            if (keyNode is null)
            {
                continue;
            }

            var keyName = keyNode.InnerText;
            var value = child.InnerText;

            switch (keyName)
            {
                case "_name":
                    name = value;
                    break;
                case "version":
                    version = value;
                    break;
                case "path":
                    path = value;
                    break;
                case "arch_kind":
                    arch = value;
                    break;
                case "info":
                    info = value;
                    break;
                case "lastModified":
                    if (DateTime.TryParse(value, out var parsed))
                    {
                        lastModified = parsed;
                    }
                    break;
            }

            keyNode = null;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new InstalledApp(
            name: name,
            version: version,
            publisher: info,
            installDate: lastModified,
            source: "system_profiler",
            uninstallCommand: !string.IsNullOrWhiteSpace(path) ? $"rm -rf \"{path}\"" : null,
            architecture: arch);
    }

    private IEnumerable<InstalledApp> EnumerateBrew(CancellationToken cancellationToken)
    {
        var output = RunTool("brew", "list --versions", _brewTimeout, cancellationToken);
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

            // brew list --versions emits lines like: "wget 1.21.4"
            var parts = trimmed.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1)
            {
                continue;
            }

            yield return new InstalledApp(
                name: parts[0],
                version: parts.Length > 1 ? parts[1] : null,
                publisher: null,
                installDate: null,
                source: "brew",
                uninstallCommand: $"brew uninstall {parts[0]}",
                architecture: null);
        }
    }

    private string RunTool(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
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

            if (!proc.WaitForExit(timeout))
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
