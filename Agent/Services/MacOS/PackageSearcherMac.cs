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

namespace BorderLink.Agent.Services.MacOS;

/// <summary>
/// Searches Homebrew for installable formulae on macOS hosts.
/// Best-effort: if brew isn't installed, returns an empty list.
/// </summary>
public class PackageSearcherMac : IPackageSearcher
{
    private static readonly TimeSpan _perToolTimeout = TimeSpan.FromSeconds(25);

    private readonly ILogger<PackageSearcherMac> _logger;

    public PackageSearcherMac(ILogger<PackageSearcherMac> logger)
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
                results.AddRange(SearchBrew(query, max, cancellationToken));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "brew search failed.");
            }

            return results.Take(max).ToList();
        }, cancellationToken);
    }

    private IEnumerable<SoftwarePackage> SearchBrew(string query, int max, CancellationToken cancellationToken)
    {
        var output = RunTool("brew", $"search {Sanitize(query)}", cancellationToken);
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

            // Skip section headers like "==> Formulae" / "==> Casks".
            if (line.StartsWith("==>", StringComparison.Ordinal))
            {
                continue;
            }

            // brew search wraps multiple names per line on some installs;
            // split on whitespace.
            foreach (var token in line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (emitted >= max)
                {
                    yield break;
                }

                var id = token.Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                string? version = null;
                string? description = null;

                if (emitted < 10)
                {
                    (version, description) = TryGetBrewInfo(id, cancellationToken);
                }

                yield return new SoftwarePackage(
                    id: id,
                    name: id,
                    version: version,
                    publisher: null,
                    source: "brew",
                    description: description);

                emitted++;
            }
        }
    }

    private (string? version, string? description) TryGetBrewInfo(string formula, CancellationToken cancellationToken)
    {
        try
        {
            var json = RunTool("brew", $"info --json {Sanitize(formula)}", cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return (null, null);
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                return (null, null);
            }

            var entry = doc.RootElement[0];
            string? version = null;
            string? description = null;

            if (entry.TryGetProperty("versions", out var versions) &&
                versions.TryGetProperty("stable", out var stable) &&
                stable.ValueKind == JsonValueKind.String)
            {
                version = stable.GetString();
            }

            if (entry.TryGetProperty("desc", out var desc) && desc.ValueKind == JsonValueKind.String)
            {
                description = desc.GetString();
            }

            return (version, description);
        }
        catch
        {
            return (null, null);
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
