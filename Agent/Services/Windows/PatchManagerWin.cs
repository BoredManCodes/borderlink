using BorderLink.Agent.Interfaces;
using BorderLink.Shared;
using BorderLink.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Services.Windows;

/// <summary>
/// Talks to <c>Microsoft.Update.Session</c> via PowerShell. Driving the COM
/// API directly from C# would require the WUApiLib interop assembly; the
/// PowerShell shape is the pragmatic path and matches how the rest of the
/// agent shells out for OS-specific work.
/// </summary>
[SupportedOSPlatform("windows")]
public class PatchManagerWin : IPatchManager
{
    private const string CbsRebootKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
    private const string WuRebootRequiredKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";
    private const string SessionManagerKey =
        @"SYSTEM\CurrentControlSet\Control\Session Manager";

    private readonly ILogger<PatchManagerWin> _logger;

    public PatchManagerWin(ILogger<PatchManagerWin> logger)
    {
        _logger = logger;
    }

    public async Task<PatchUpdate[]> GetPendingUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var ps = PowerShell.Create();
            ps.AddScript(@"
$session = New-Object -ComObject Microsoft.Update.Session
$searcher = $session.CreateUpdateSearcher()
$result = $searcher.Search(""IsInstalled=0 and Type='Software'"")
$result.Updates | ForEach-Object {
    [PSCustomObject]@{
        Id              = $_.Identity.UpdateID
        Title           = $_.Title
        KbNumber        = ($_.KBArticleIDs | Select-Object -First 1)
        Description     = $_.Description
        Severity        = $_.MsrcSeverity
        SizeBytes       = $_.MaxDownloadSize
        RebootRequired  = $_.RebootRequired
        PublishedAt     = $_.LastDeploymentChangeTime
        IsDownloaded    = $_.IsDownloaded
    }
} | ConvertTo-Json -Compress -Depth 4
");

            var invokeTask = ps.InvokeAsync();
            using var registration = cancellationToken.Register(() =>
            {
                try { ps.Stop(); } catch { /* best-effort */ }
            });

            var results = await invokeTask;

            if (ps.HadErrors)
            {
                foreach (var error in ps.Streams.Error)
                {
                    _logger.LogWarning("Update search error: {error}", error.ToString());
                }
            }

            var json = string.Join(string.Empty, results.Select(x => x.BaseObject?.ToString() ?? string.Empty));
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<PatchUpdate>();
            }

            return ParsePendingUpdates(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while querying Microsoft.Update.Session for pending updates.");
            return Array.Empty<PatchUpdate>();
        }
    }

    public async Task<bool> InstallUpdateAsync(
        string updateId,
        IProgress<PatchInstallProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(updateId))
        {
            return false;
        }

        try
        {
            progress.Report(new PatchInstallProgress(string.Empty, updateId, PatchInstallPhase.Downloading, 0, "Searching for update."));

            using var ps = PowerShell.Create();
            ps.AddScript($@"
$session = New-Object -ComObject Microsoft.Update.Session
$searcher = $session.CreateUpdateSearcher()
$result = $searcher.Search(""IsInstalled=0 and Type='Software' and UpdateID='{EscapeForPwsh(updateId)}'"")
if ($result.Updates.Count -eq 0) {{
    Write-Output 'NOT_FOUND'
    return
}}

$updates = New-Object -ComObject Microsoft.Update.UpdateColl
foreach ($u in $result.Updates) {{ [void]$updates.Add($u) }}

$downloader = $session.CreateUpdateDownloader()
$downloader.Updates = $updates
$dlResult = $downloader.Download()

$installer = $session.CreateUpdateInstaller()
$installer.Updates = $updates
$installResult = $installer.Install()

[PSCustomObject]@{{
    DownloadResult     = $dlResult.ResultCode
    InstallResult      = $installResult.ResultCode
    RebootRequired     = $installResult.RebootRequired
    HResult            = $installResult.HResult
}} | ConvertTo-Json -Compress
");

            progress.Report(new PatchInstallProgress(string.Empty, updateId, PatchInstallPhase.Downloading, 100, "Downloading."));

            var invokeTask = ps.InvokeAsync();
            using var registration = cancellationToken.Register(() =>
            {
                try { ps.Stop(); } catch { /* best-effort */ }
            });

            progress.Report(new PatchInstallProgress(string.Empty, updateId, PatchInstallPhase.Installing, 0, "Installing."));

            var results = await invokeTask;

            var output = string.Join(string.Empty, results.Select(x => x.BaseObject?.ToString() ?? string.Empty));
            if (output.Contains("NOT_FOUND", StringComparison.Ordinal))
            {
                progress.Report(new PatchInstallProgress(string.Empty, updateId, PatchInstallPhase.Failed, 0, "Update not found."));
                return false;
            }

            // ResultCode 2 (orcSucceeded) per WUApi spec — anything else is partial/failed.
            var success = output.Contains("\"InstallResult\":2", StringComparison.Ordinal);
            progress.Report(new PatchInstallProgress(
                string.Empty,
                updateId,
                success ? PatchInstallPhase.Completed : PatchInstallPhase.Failed,
                100,
                success ? "Install completed." : $"Install failed. Output: {Truncate(output, 256)}"));
            return success;
        }
        catch (OperationCanceledException)
        {
            progress.Report(new PatchInstallProgress(string.Empty, updateId, PatchInstallPhase.Failed, 0, "Install cancelled."));
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
            var reasons = new List<string>(3);

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(CbsRebootKey);
                if (key is not null)
                {
                    reasons.Add("CBS");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to probe CBS RebootPending.");
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(WuRebootRequiredKey);
                if (key is not null)
                {
                    reasons.Add("WindowsUpdate");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to probe WindowsUpdate RebootRequired.");
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(SessionManagerKey);
                var pfro = key?.GetValue("PendingFileRenameOperations");
                if (pfro is string[] s && s.Any(x => !string.IsNullOrEmpty(x)))
                {
                    reasons.Add("PendingFileRenameOperations");
                }
                else if (pfro is string singleString && !string.IsNullOrEmpty(singleString))
                {
                    reasons.Add("PendingFileRenameOperations");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to probe PendingFileRenameOperations.");
            }

            return new PendingRebootInfo(reasons.Count > 0, reasons.ToArray());
        }, cancellationToken);
    }

    private PatchUpdate[] ParsePendingUpdates(string json)
    {
        try
        {
            // ConvertTo-Json emits a single object when there's only one
            // result and an array otherwise — accept either shape.
            var trimmed = json.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                trimmed = "[" + trimmed + "]";
            }

            using var doc = JsonDocument.Parse(trimmed);
            var list = new List<PatchUpdate>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                list.Add(new PatchUpdate(
                    id: GetString(element, "Id") ?? string.Empty,
                    title: GetString(element, "Title") ?? string.Empty,
                    kbNumber: NormaliseKb(GetString(element, "KbNumber")),
                    description: GetString(element, "Description"),
                    severity: ParseSeverity(GetString(element, "Severity")),
                    sizeBytes: GetLong(element, "SizeBytes"),
                    rebootRequired: GetBool(element, "RebootRequired"),
                    publishedAt: GetDateTime(element, "PublishedAt"),
                    isDownloaded: GetBool(element, "IsDownloaded"),
                    isInstalled: false));
            }
            return list.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse pending-update JSON: {json}", Truncate(json, 256));
            return Array.Empty<PatchUpdate>();
        }
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : prop.ToString();
    }

    private static long GetLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
        {
            return 0;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.TryGetInt64(out var l) ? l : (long)prop.GetDouble(),
            JsonValueKind.String when long.TryParse(prop.GetString(), out var ls) => ls,
            _ => 0,
        };
    }

    private static bool GetBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
        {
            return false;
        }
        return prop.ValueKind == JsonValueKind.True ||
            (prop.ValueKind == JsonValueKind.String &&
             bool.TryParse(prop.GetString(), out var b) && b);
    }

    private static DateTime? GetDateTime(JsonElement element, string name)
    {
        var s = GetString(element, name);
        return DateTime.TryParse(s, out var dt) ? dt : (DateTime?)null;
    }

    private static string? NormaliseKb(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        return raw.StartsWith("KB", StringComparison.OrdinalIgnoreCase) ? raw : "KB" + raw;
    }

    private static PatchSeverity ParseSeverity(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return PatchSeverity.Unknown;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "critical" => PatchSeverity.Critical,
            "important" => PatchSeverity.Important,
            "moderate" => PatchSeverity.Moderate,
            "low" => PatchSeverity.Low,
            _ => PatchSeverity.Unknown,
        };
    }

    private static string EscapeForPwsh(string value)
    {
        return value.Replace("'", "''");
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
