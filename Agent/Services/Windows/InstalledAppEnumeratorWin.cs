using BorderLink.Agent.Interfaces;
using BorderLink.Shared;
using BorderLink.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
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
/// Reads installed software from the standard Windows uninstall registry
/// keys (HKLM 64-bit and Wow6432Node, plus all loaded HKEY_USERS hives so
/// per-user installs are visible when the agent runs as SYSTEM), and merges
/// in <c>winget list</c> output when winget is available.
/// </summary>
[SupportedOSPlatform("windows")]
public class InstalledAppEnumeratorWin : IInstalledAppEnumerator
{
    private const string UninstallSubKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private const string WowUninstallSubKey =
        @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly string[] _installDateFormats =
    {
        "yyyyMMdd",
        "yyyy-MM-dd",
        "MM/dd/yyyy",
        "M/d/yyyy"
    };

    private readonly ILogger<InstalledAppEnumeratorWin> _logger;

    public InstalledAppEnumeratorWin(ILogger<InstalledAppEnumeratorWin> logger)
    {
        _logger = logger;
    }

    public Task<List<InstalledApp>> GetInstalledApps(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

            ReadFromHive(Registry.LocalMachine, UninstallSubKey, RegistryView.Registry64, apps, cancellationToken);
            ReadFromHive(Registry.LocalMachine, WowUninstallSubKey, RegistryView.Registry32, apps, cancellationToken);

            ReadAllUserHives(apps, cancellationToken);

            MergeWingetEntries(apps, cancellationToken);

            return apps.Values
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .OrderBy(x => x.Name)
                .ToList();
        }, cancellationToken);
    }

    private void ReadAllUserHives(
        Dictionary<string, InstalledApp> apps,
        CancellationToken cancellationToken)
    {
        try
        {
            using var users = Registry.Users;
            var subkeyNames = users.GetSubKeyNames();

            foreach (var sid in subkeyNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(sid))
                {
                    continue;
                }

                if (sid.Equals(".DEFAULT", StringComparison.OrdinalIgnoreCase) ||
                    sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    using var userHive = users.OpenSubKey(sid);
                    if (userHive is null)
                    {
                        continue;
                    }

                    using var uninstall = userHive.OpenSubKey(UninstallSubKey);
                    if (uninstall is null)
                    {
                        continue;
                    }

                    ReadKey(uninstall, RegistryView.Default, apps, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Failed to read uninstall entries for SID {sid}.", sid);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate HKEY_USERS hives.");
        }
    }

    private void ReadFromHive(
        RegistryKey hive,
        string subKey,
        RegistryView view,
        Dictionary<string, InstalledApp> apps,
        CancellationToken cancellationToken)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);

            using var key = baseKey.OpenSubKey(subKey);
            if (key is null)
            {
                return;
            }

            ReadKey(key, view, apps, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read uninstall key {subKey}.", subKey);
        }
    }

    private void ReadKey(
        RegistryKey uninstallKey,
        RegistryView view,
        Dictionary<string, InstalledApp> apps,
        CancellationToken cancellationToken)
    {
        foreach (var subkeyName in uninstallKey.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var sub = uninstallKey.OpenSubKey(subkeyName);
                if (sub is null)
                {
                    continue;
                }

                var name = sub.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                // Skip system updates and Windows components by convention:
                // entries with SystemComponent=1 are not user-facing software.
                var systemComponent = sub.GetValue("SystemComponent") as int?;
                if (systemComponent.HasValue && systemComponent.Value == 1)
                {
                    continue;
                }

                var version = sub.GetValue("DisplayVersion") as string;
                var publisher = sub.GetValue("Publisher") as string;
                var installDateRaw = sub.GetValue("InstallDate") as string;
                var uninstallString = sub.GetValue("UninstallString") as string;
                var quietUninstall = sub.GetValue("QuietUninstallString") as string;
                var arch = view == RegistryView.Registry32 ? "x86" : null;

                var installDate = ParseInstallDate(installDateRaw);

                var key = $"{name}|{version}";
                var app = new InstalledApp(
                    name: name,
                    version: version,
                    publisher: publisher,
                    installDate: installDate,
                    source: "registry",
                    uninstallCommand: !string.IsNullOrWhiteSpace(quietUninstall)
                        ? quietUninstall
                        : uninstallString,
                    architecture: arch);

                apps[key] = app;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Failed to read uninstall subkey {sub}.", subkeyName);
            }
        }
    }

    private static DateTime? ParseInstallDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTime.TryParseExact(
                raw,
                _installDateFormats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsedExact))
        {
            return parsedExact;
        }

        if (DateTime.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private void MergeWingetEntries(
        Dictionary<string, InstalledApp> apps,
        CancellationToken cancellationToken)
    {
        try
        {
            var output = RunWinget(cancellationToken);
            if (string.IsNullOrWhiteSpace(output))
            {
                return;
            }

            // winget list output is fixed-width columns:
            //   Name   Id   Version   Available   Source
            // We parse leniently — the header row tells us where each column starts.
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var headerIndex = -1;
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("Name", StringComparison.OrdinalIgnoreCase) &&
                    lines[i].IndexOf("Id", StringComparison.OrdinalIgnoreCase) > 0 &&
                    lines[i].IndexOf("Version", StringComparison.OrdinalIgnoreCase) > 0)
                {
                    headerIndex = i;
                    break;
                }
            }

            if (headerIndex < 0)
            {
                return;
            }

            var header = lines[headerIndex];
            var nameStart = 0;
            var idStart = header.IndexOf("Id", StringComparison.OrdinalIgnoreCase);
            var versionStart = header.IndexOf("Version", StringComparison.OrdinalIgnoreCase);

            if (idStart < 0 || versionStart < 0 || versionStart <= idStart)
            {
                return;
            }

            for (var i = headerIndex + 2; i < lines.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = lines[i].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("---", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.Length < versionStart)
                {
                    continue;
                }

                try
                {
                    var name = line.Substring(nameStart, idStart - nameStart).TrimEnd();
                    var versionPart = line.Length > versionStart
                        ? line.Substring(versionStart).TrimStart()
                        : string.Empty;

                    // versionPart may itself be space-padded into Available/Source columns;
                    // take the first whitespace-delimited token as the version.
                    var version = versionPart.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault();

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var key = $"{name}|{version}";

                    if (apps.TryGetValue(key, out var existing))
                    {
                        existing.Source = "winget";
                    }
                    else
                    {
                        apps[key] = new InstalledApp(
                            name: name,
                            version: version,
                            publisher: null,
                            installDate: null,
                            source: "winget",
                            uninstallCommand: null,
                            architecture: null);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Failed to parse winget line: {line}", line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "winget enumeration failed.");
        }
    }

    private string RunWinget(CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo("winget", "list --accept-source-agreements")
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

            // Hard cap on time. If winget is slow, return what we have rather than blocking.
            if (!proc.WaitForExit(TimeSpan.FromSeconds(20)))
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
    }
}
