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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Services.Linux;

/// <summary>
/// apt-based patch manager for Debian/Ubuntu agents. Non-apt distros
/// (rpm/dnf/zypper) get a no-op stub — patching for those is deferred to a
/// later phase rather than badly half-implemented now.
/// </summary>
public class PatchManagerLinux : IPatchManager
{
    private static readonly TimeSpan _listTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _installTimeout = TimeSpan.FromMinutes(15);

    private static readonly Regex _upgradableLine = new(
        @"^(?<pkg>[^/\s]+)/\S+\s+(?<ver>\S+)\s+(?<arch>\S+)\s+\[upgradable from:\s+(?<oldver>[^\]]+)\]",
        RegexOptions.Compiled);

    private readonly ILogger<PatchManagerLinux> _logger;

    public PatchManagerLinux(ILogger<PatchManagerLinux> logger)
    {
        _logger = logger;
    }

    public Task<PatchUpdate[]> GetPendingUpdatesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (!IsAptAvailable())
            {
                return Array.Empty<PatchUpdate>();
            }

            var output = RunTool("apt", "list --upgradable", _listTimeout, cancellationToken);
            if (string.IsNullOrWhiteSpace(output))
            {
                return Array.Empty<PatchUpdate>();
            }

            var list = new List<PatchUpdate>();
            foreach (var line in output.Split('\n'))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var trimmed = line.TrimEnd('\r').Trim();
                if (string.IsNullOrWhiteSpace(trimmed) ||
                    trimmed.StartsWith("Listing", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var match = _upgradableLine.Match(trimmed);
                if (!match.Success)
                {
                    continue;
                }

                var pkg = match.Groups["pkg"].Value;
                var newVer = match.Groups["ver"].Value;
                var oldVer = match.Groups["oldver"].Value;

                list.Add(new PatchUpdate(
                    id: pkg,
                    title: $"{pkg} {newVer}",
                    kbNumber: null,
                    description: $"Upgradable from {oldVer} to {newVer}",
                    severity: PatchSeverity.Unknown,
                    sizeBytes: 0,
                    rebootRequired: false,
                    publishedAt: null,
                    isDownloaded: false,
                    isInstalled: false));
            }

            return list.ToArray();
        }, cancellationToken);
    }

    public async Task<bool> InstallUpdateAsync(
        string updateId,
        IProgress<PatchInstallProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(updateId) || !IsAptAvailable())
        {
            return false;
        }

        progress.Report(new PatchInstallProgress(string.Empty, updateId, PatchInstallPhase.Downloading, 0, "Starting apt-get."));

        try
        {
            var psi = new ProcessStartInfo("apt-get",
                $"-y -o Dpkg::Options::=--force-confnew install --only-upgrade {EscapeArg(updateId)}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.Environment["DEBIAN_FRONTEND"] = "noninteractive";

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                progress.Report(new PatchInstallProgress(string.Empty, updateId, PatchInstallPhase.Failed, 0, "Failed to start apt-get."));
                return false;
            }

            proc.OutputDataReceived += (_, args) =>
            {
                if (string.IsNullOrEmpty(args.Data)) return;

                if (args.Data.StartsWith("Get:", StringComparison.Ordinal))
                {
                    progress.Report(new PatchInstallProgress(string.Empty, updateId, PatchInstallPhase.Downloading, 50, args.Data));
                }
                else if (args.Data.StartsWith("Setting up", StringComparison.Ordinal) ||
                         args.Data.StartsWith("Unpacking", StringComparison.Ordinal))
                {
                    progress.Report(new PatchInstallProgress(string.Empty, updateId, PatchInstallPhase.Installing, 75, args.Data));
                }
            };
            proc.BeginOutputReadLine();

            var exitTask = proc.WaitForExitAsync(cancellationToken);
            var timeoutTask = Task.Delay(_installTimeout, cancellationToken);
            var completed = await Task.WhenAny(exitTask, timeoutTask);
            if (completed == timeoutTask && !proc.HasExited)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                progress.Report(new PatchInstallProgress(string.Empty, updateId, PatchInstallPhase.Failed, 0, "apt-get timed out."));
                return false;
            }

            await exitTask;

            if (proc.ExitCode == 0)
            {
                progress.Report(new PatchInstallProgress(string.Empty, updateId, PatchInstallPhase.Completed, 100, "apt-get install complete."));
                return true;
            }

            var stderr = await proc.StandardError.ReadToEndAsync();
            progress.Report(new PatchInstallProgress(
                string.Empty,
                updateId,
                PatchInstallPhase.Failed,
                100,
                $"apt-get exited {proc.ExitCode}. {Truncate(stderr, 256)}"));
            return false;
        }
        catch (OperationCanceledException)
        {
            progress.Report(new PatchInstallProgress(string.Empty, updateId, PatchInstallPhase.Failed, 0, "Cancelled."));
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while installing update {updateId}.", updateId);
            progress.Report(new PatchInstallProgress(string.Empty, updateId, PatchInstallPhase.Failed, 0, ex.Message));
            return false;
        }
    }

    public Task<PendingRebootInfo> GetPendingRebootAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            const string flagPath = "/var/run/reboot-required";
            const string pkgsPath = "/var/run/reboot-required.pkgs";

            try
            {
                if (!File.Exists(flagPath))
                {
                    return new PendingRebootInfo(false, Array.Empty<string>());
                }

                var reasons = new List<string> { "reboot-required" };
                if (File.Exists(pkgsPath))
                {
                    foreach (var line in File.ReadAllLines(pkgsPath))
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            reasons.Add(trimmed);
                        }
                    }
                }

                return new PendingRebootInfo(true, reasons.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to probe Linux pending reboot.");
                return new PendingRebootInfo(false, Array.Empty<string>());
            }
        }, cancellationToken);
    }

    private static bool IsAptAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("/bin/sh", "-c \"command -v apt\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            proc.WaitForExit(5000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private string RunTool(string fileName, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
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

            if (!proc.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
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

    private static string EscapeArg(string raw)
    {
        // Package names are restricted to [a-z0-9.+-]; reject anything else
        // to keep this from becoming an injection vector.
        foreach (var c in raw)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '+' && c != '-' && c != '_' && c != ':')
            {
                return string.Empty;
            }
        }
        return raw;
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }
        return value[..max];
    }
}
