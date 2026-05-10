using BorderLink.Agent.Interfaces;
using BorderLink.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Services.Windows;

/// <summary>
/// Searches winget and chocolatey for installable packages. Best-effort:
/// if a tool isn't present, that source contributes no results.
/// </summary>
[SupportedOSPlatform("windows")]
public class PackageSearcherWin : IPackageSearcher
{
    private static readonly TimeSpan _toolTimeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<PackageSearcherWin> _logger;

    public PackageSearcherWin(ILogger<PackageSearcherWin> logger)
    {
        _logger = logger;
    }

    public Task<List<SoftwarePackage>> Search(string query, int max, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var results = new List<SoftwarePackage>();

            if (string.IsNullOrWhiteSpace(query) || max <= 0)
            {
                return results;
            }

            try
            {
                results.AddRange(SearchWinget(query, max, cancellationToken));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "winget search failed.");
            }

            try
            {
                if (results.Count < max)
                {
                    results.AddRange(SearchChoco(query, max - results.Count, cancellationToken));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "choco search failed.");
            }

            return results.Take(max).ToList();
        }, cancellationToken);
    }

    private IEnumerable<SoftwarePackage> SearchWinget(string query, int max, CancellationToken cancellationToken)
    {
        var output = RunTool("winget",
            $"search \"{Sanitize(query)}\" --source winget --accept-source-agreements",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(output))
        {
            yield break;
        }

        // winget search output is column-aligned text:
        //   Name   Id   Version   Match   Source
        var lines = output.Split('\n', StringSplitOptions.None);

        var headerIndex = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd('\r');
            if (trimmed.StartsWith("Name", StringComparison.OrdinalIgnoreCase) &&
                trimmed.IndexOf("Id", StringComparison.OrdinalIgnoreCase) > 0 &&
                trimmed.IndexOf("Version", StringComparison.OrdinalIgnoreCase) > 0)
            {
                headerIndex = i;
                break;
            }
        }

        if (headerIndex < 0)
        {
            yield break;
        }

        var header = lines[headerIndex].TrimEnd('\r');
        var idStart = header.IndexOf("Id", StringComparison.OrdinalIgnoreCase);
        var versionStart = header.IndexOf("Version", StringComparison.OrdinalIgnoreCase);
        if (idStart < 0 || versionStart < 0 || versionStart <= idStart)
        {
            yield break;
        }

        // Find optional Match and Source columns to know where Version ends.
        var matchStart = header.IndexOf("Match", StringComparison.OrdinalIgnoreCase);
        var sourceStart = header.IndexOf("Source", StringComparison.OrdinalIgnoreCase);
        var versionEnd = matchStart > versionStart
            ? matchStart
            : (sourceStart > versionStart ? sourceStart : header.Length);

        // Skip header + dashes line.
        var emitted = 0;
        for (var i = headerIndex + 2; i < lines.Length && emitted < max; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Length < idStart)
            {
                continue;
            }

            string name;
            string id;
            string? version = null;
            try
            {
                name = line.Substring(0, idStart).TrimEnd();
                if (line.Length <= versionStart)
                {
                    id = line.Substring(idStart).TrimEnd();
                }
                else
                {
                    id = line.Substring(idStart, versionStart - idStart).TrimEnd();
                    var versionLength = Math.Min(versionEnd, line.Length) - versionStart;
                    if (versionLength > 0)
                    {
                        version = line.Substring(versionStart, versionLength).Trim();
                    }
                    else
                    {
                        version = line.Substring(versionStart).Trim();
                    }
                }
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            yield return new SoftwarePackage(
                id: id,
                name: name,
                version: string.IsNullOrWhiteSpace(version) ? null : version,
                publisher: null,
                source: "winget",
                description: null);

            emitted++;
        }
    }

    private IEnumerable<SoftwarePackage> SearchChoco(string query, int max, CancellationToken cancellationToken)
    {
        var output = RunTool("choco",
            $"search {Sanitize(query)} --limit-output --no-progress",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(output))
        {
            yield break;
        }

        var emitted = 0;
        foreach (var rawLine in output.Split('\n'))
        {
            if (emitted >= max)
            {
                yield break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var line = rawLine.TrimEnd('\r').Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            // --limit-output uses pipe-separated `name|version` rows.
            var parts = line.Split('|', 2);
            if (parts.Length < 1)
            {
                continue;
            }

            var id = parts[0].Trim();
            var version = parts.Length > 1 ? parts[1].Trim() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            yield return new SoftwarePackage(
                id: id,
                name: id,
                version: string.IsNullOrWhiteSpace(version) ? null : version,
                publisher: null,
                source: "choco",
                description: null);

            emitted++;
        }
    }

    private string RunTool(string toolName, string args, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo(toolName, args)
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

            if (!proc.WaitForExit((int)_toolTimeout.TotalMilliseconds))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return string.Empty;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return proc.StandardOutput.ReadToEnd();
        }
        catch (Win32Exception)
        {
            // Tool not on PATH.
            return string.Empty;
        }
        catch (FileNotFoundException)
        {
            return string.Empty;
        }
    }

    private static string Sanitize(string query)
    {
        // Strip quotes and stray control characters so we don't break the
        // shell args. Package names don't legitimately need quotes.
        return new string(query.Where(c => c != '"' && !char.IsControl(c)).ToArray());
    }
}
