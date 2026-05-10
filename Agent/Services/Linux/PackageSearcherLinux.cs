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
/// Searches apt-cache for installable packages on Debian/Ubuntu hosts.
/// Best-effort: if apt isn't available, returns an empty list.
/// </summary>
public class PackageSearcherLinux : IPackageSearcher
{
    private static readonly TimeSpan _perToolTimeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<PackageSearcherLinux> _logger;

    public PackageSearcherLinux(ILogger<PackageSearcherLinux> logger)
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
                results.AddRange(SearchApt(query, max, cancellationToken));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "apt-cache search failed.");
            }

            return results.Take(max).ToList();
        }, cancellationToken);
    }

    private IEnumerable<SoftwarePackage> SearchApt(string query, int max, CancellationToken cancellationToken)
    {
        var sanitized = Sanitize(query);
        var output = RunTool("apt-cache", $"search \"{sanitized}\"", cancellationToken);
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

            // apt-cache search outputs:  package-name - description
            var parts = line.Split(new[] { " - " }, 2, StringSplitOptions.None);
            if (parts.Length < 1)
            {
                continue;
            }

            var id = parts[0].Trim();
            var description = parts.Length > 1 ? parts[1].Trim() : null;

            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            string? version = null;
            // Best-effort version lookup for the top result(s).
            if (emitted < 10)
            {
                version = TryGetAptVersion(id, cancellationToken);
            }

            yield return new SoftwarePackage(
                id: id,
                name: id,
                version: version,
                publisher: null,
                source: "apt",
                description: description);

            emitted++;
        }
    }

    private string? TryGetAptVersion(string pkg, CancellationToken cancellationToken)
    {
        try
        {
            var output = RunTool("apt-cache", $"show {Sanitize(pkg)}", cancellationToken);
            if (string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring("Version:".Length).Trim();
                }
            }
        }
        catch
        {
            // Ignore — version is best-effort.
        }
        return null;
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

            if (!proc.WaitForExit((int)_perToolTimeout.TotalMilliseconds))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return string.Empty;
            }

            cancellationToken.ThrowIfCancellationRequested();
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
    }

    private static string Sanitize(string query)
    {
        return new string(query.Where(c =>
                c != '"' && c != '`' && c != '$' && c != ';' && !char.IsControl(c))
            .ToArray());
    }
}
