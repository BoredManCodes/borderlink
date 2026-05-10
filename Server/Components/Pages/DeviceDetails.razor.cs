using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using BorderLink.Server.Hubs;
using BorderLink.Server.Models.Messages;
using BorderLink.Server.Services;
using BorderLink.Shared;
using BorderLink.Shared.Entities;
using BorderLink.Shared.Enums;
using BorderLink.Shared.Utilities;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BorderLink.Server.Components.Pages;

public partial class DeviceDetails : AuthComponentBase
{
    private readonly ConcurrentQueue<string> _logLines = new();
    private readonly ConcurrentQueue<ScriptResult> _scriptResults = new();

    private string? _alertMessage;
    private Device? _device;
    private bool _userHasAccess;
    private string? _inputDeviceId;
    private bool _isLoading = true;
    private DeviceGroup[] _deviceGroups = Array.Empty<DeviceGroup>();

    private List<InstalledApp>? _installedApps;
    private DateTimeOffset? _inventoryCapturedAt;
    private bool _isAppsRefreshing;
    private string? _appsRefreshError;
    private string? _appsSearchTerm;
    private InstalledAppSortKey _appsSortKey = InstalledAppSortKey.Name;
    private bool _appsSortDescending;
    private bool _isInstallModalOpen;
    private readonly HashSet<string> _pendingUninstallKeys = new(StringComparer.Ordinal);

    private ServiceInfo[]? _services;
    private bool _isServicesRefreshing;
    private string? _servicesError;
    private string? _servicesSearchTerm;
    private ServiceSortKey _servicesSortKey = ServiceSortKey.Name;
    private bool _servicesSortDescending;
    private readonly HashSet<string> _pendingServiceKeys = new(StringComparer.Ordinal);

    private ProcessInfo[]? _processes;
    private bool _isProcessesRefreshing;
    private string? _processesError;
    private string? _processesSearchTerm;
    private ProcessSortKey _processesSortKey = ProcessSortKey.Name;
    private bool _processesSortDescending;
    private readonly HashSet<int> _pendingKillPids = new();

    private enum InstalledAppSortKey
    {
        Name,
        Version,
        Publisher,
        InstallDate,
        Source,
    }

    private enum ServiceSortKey
    {
        Name,
        DisplayName,
        Status,
        StartType,
        ProcessId,
    }

    private enum ProcessSortKey
    {
        Pid,
        Name,
        UserName,
        WorkingSet,
        Cpu,
    }

    [Parameter]
    public string ActiveTab { get; set; } = string.Empty;

    [Parameter]
    public string DeviceId { get; set; } = string.Empty;

    [Inject]
    private ICircuitConnection CircuitConnection { get; set; } = null!;

    [Inject]
    private IDataService DataService { get; set; } = null!;

    [Inject]
    private IInventoryService InventoryService { get; set; } = null!;


    [Inject]
    private IJsInterop JsInterop { get; set; } = null!;

    [Inject]
    private IModalService ModalService { get; set; } = null!;

    [Inject]
    private NavigationManager NavManager { get; set; } = null!;

    [Inject]
    private IToastService ToastService { get; set; } = null!;


    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        EnsureUserSet();

        if (!string.IsNullOrWhiteSpace(DeviceId))
        {
            var deviceResult = await DataService.GetDevice(DeviceId);
            if (deviceResult.IsSuccess)
            {
                _device = deviceResult.Value;
                _userHasAccess = DataService.DoesUserHaveAccessToDevice(_device.ID, User);
            }
            else
            {
                ToastService.ShowToast2(deviceResult.Reason, Enums.ToastType.Warning);
            }
        }

        _deviceGroups = DataService.GetDeviceGroups(UserName);
        await Register<ReceiveLogsMessage, string>(
            CircuitConnection.ConnectionId,
            HandleReceiveLogsMessage);

        _isLoading = false;
    }

    private async Task HandleReceiveLogsMessage(object subscriber, ReceiveLogsMessage message)
    {
        _logLines.Enqueue(message.LogChunk);
        await InvokeAsync(StateHasChanged);
    }

    private async Task DeleteLogs()
    {
        if (_device is null)
        {
            return;
        }

        var result = await JsInterop.Confirm("Are you sure you want to delete the remote logs?");
        if (result)
        {
            await CircuitConnection.DeleteRemoteLogs(_device.ID);
            ToastService.ShowToast("Delete command sent.");
        }
    }

    private void EditFormKeyDown()
    {
        _alertMessage = string.Empty;
    }

    private void EvaluateDeviceIdInputKeyDown(KeyboardEventArgs args)
    {
        if (args.Key.Equals("Enter", StringComparison.OrdinalIgnoreCase))
        {
            NavManager.NavigateTo($"/device-details/{_inputDeviceId}");
        }
    }

    private void GetRemoteLogs()
    {
        if (_device is null)
        {
            return;
        }

        _logLines.Clear();

        if (_device.IsOnline)
        {
            CircuitConnection.GetRemoteLogs(_device.ID);
        }
    }

    private void GetScriptHistory()
    {
        if (_device is null)
        {
            return;
        }

        EnsureUserSet();

        _scriptResults.Clear();

        if (User.IsAdministrator)
        {
            var results = DataService
                .GetAllScriptResults(User.OrganizationID, _device.ID)
                .OrderByDescending(x => x.TimeStamp);

            foreach (var result in results)
            {
                _scriptResults.Enqueue(result);
            }
        }
        else
        {
            var results = DataService
                .GetAllCommandResultsForUser(User.OrganizationID, UserName, _device.ID)
                .OrderByDescending(x => x.TimeStamp);

            foreach (var result in results)
            {
                _scriptResults.Enqueue(result);
            }
        }
    }

    private string GetTrimmedText(string? source, int stringLength)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "(none)";
        }

        if (source.Length <= stringLength)
        {
            return source;
        }

        return source[0..25] + "...";
    }

    private string GetTrimmedText(string[]? source, int stringLength)
    {
        source ??= Array.Empty<string>();
        return GetTrimmedText(string.Join("", source), stringLength);
    }

    private Task HandleValidSubmit()
    {
        if (_device is null)
        {
            return Task.CompletedTask;
        }

        DataService.UpdateDevice(
            _device.ID,
            _device.Tags,
            _device.Alias,
            _device.DeviceGroupID,
            _device.Notes);

        _alertMessage = "Device details saved.";
        ToastService.ShowToast("Device details saved.");

        return Task.CompletedTask;
    }

    private void NavigateToDeviceId()
    {
        NavManager.NavigateTo($"/device-details/{_inputDeviceId}");
    }

    private void ShowAllDisks()
    {
        if (_device is null)
        {
            return;
        }

        var disksString = JsonSerializer.Serialize(_device.Drives, JsonSerializerHelper.IndentedOptions);
        void modalBody(RenderTreeBuilder builder)
        {
            builder.AddMarkupContent(0, $"<div style='white-space: pre'>{disksString}</div>");
        }
        ModalService.ShowModal($"All Disks for {_device.DeviceName}", modalBody);
    }

    private void LoadInstalledApps()
    {
        // OnActivated is Action (not Func<Task>) — kick the load off as a
        // background task so the tab activation completes immediately.
        _ = LoadInstalledAppsCore();
    }

    private async Task LoadInstalledAppsCore()
    {
        if (_device is null)
        {
            return;
        }

        _appsRefreshError = null;

        // If we already have apps loaded for this view, don't refetch
        // automatically — the user has an explicit Refresh button.
        if (_installedApps is not null)
        {
            return;
        }

        try
        {
            var snapshot = await InventoryService.GetLatestSnapshot(_device.ID);
            if (snapshot is not null)
            {
                _installedApps = snapshot.Apps;
                _inventoryCapturedAt = snapshot.CapturedAt;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception ex)
        {
            _appsRefreshError = "Failed to load cached inventory: " + ex.Message;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RefreshInstalledApps()
    {
        if (_device is null || _isAppsRefreshing)
        {
            return;
        }

        _isAppsRefreshing = true;
        _appsRefreshError = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            var result = await CircuitConnection.RefreshDeviceInventory(_device.ID);
            if (!result.IsSuccess)
            {
                _appsRefreshError = result.Reason;
                ToastService.ShowToast2(result.Reason, Enums.ToastType.Warning);
                return;
            }

            _installedApps = result.Value.Apps;
            _inventoryCapturedAt = result.Value.CapturedAt;
            ToastService.ShowToast("Inventory refreshed.");
        }
        catch (Exception ex)
        {
            _appsRefreshError = ex.Message;
            ToastService.ShowToast2("Failed to refresh inventory.", Enums.ToastType.Error);
        }
        finally
        {
            _isAppsRefreshing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void SortApps(InstalledAppSortKey key)
    {
        if (_appsSortKey == key)
        {
            _appsSortDescending = !_appsSortDescending;
        }
        else
        {
            _appsSortKey = key;
            _appsSortDescending = false;
        }
    }

    private string SortIndicator(InstalledAppSortKey key)
    {
        if (_appsSortKey != key)
        {
            return string.Empty;
        }

        return _appsSortDescending ? "▼" : "▲";
    }

    private IEnumerable<InstalledApp> GetFilteredSortedApps()
    {
        if (_installedApps is null)
        {
            return Array.Empty<InstalledApp>();
        }

        IEnumerable<InstalledApp> query = _installedApps;

        if (!string.IsNullOrWhiteSpace(_appsSearchTerm))
        {
            var term = _appsSearchTerm.Trim();
            query = query.Where(x =>
                (x.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.Version?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.Publisher?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.Source?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        query = _appsSortKey switch
        {
            InstalledAppSortKey.Version => query.OrderBy(x => x.Version ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            InstalledAppSortKey.Publisher => query.OrderBy(x => x.Publisher ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            InstalledAppSortKey.InstallDate => query.OrderBy(x => x.InstallDate ?? DateTime.MinValue),
            InstalledAppSortKey.Source => query.OrderBy(x => x.Source ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase),
        };

        if (_appsSortDescending)
        {
            query = query.Reverse();
        }

        return query;
    }

    private async Task CopyUninstallCommand(InstalledApp app)
    {
        if (string.IsNullOrWhiteSpace(app.UninstallCommand))
        {
            return;
        }

        var copied = await JsInterop.SetClipboardText(app.UninstallCommand);
        ToastService.ShowToast(copied
            ? "Uninstall command copied to clipboard."
            : "Failed to copy to clipboard.");
    }

    private static bool TryMapSourceToAction(InstalledApp app, out SoftwareActionSource source)
    {
        switch ((app.Source ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "winget":
                source = SoftwareActionSource.Winget;
                return true;
            case "choco":
                source = SoftwareActionSource.Choco;
                return true;
            case "dpkg":
            case "rpm":
            case "apt":
                source = SoftwareActionSource.Apt;
                return true;
            case "brew":
                source = SoftwareActionSource.Brew;
                return true;
            case "registry":
                source = SoftwareActionSource.Msi;
                return true;
            default:
                source = SoftwareActionSource.Winget;
                return false;
        }
    }

    private static string AppKey(InstalledApp app) =>
        $"{app.Source}|{app.Name}|{app.Version}";

    private bool CanUninstallApp(InstalledApp app)
    {
        if (!TryMapSourceToAction(app, out _))
        {
            return false;
        }

        // Registry-only entries with no UninstallString aren't actionable.
        if (string.Equals(app.Source, "registry", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(app.UninstallCommand))
        {
            return false;
        }

        return true;
    }

    private string UninstallButtonTitle(InstalledApp app)
    {
        if (string.Equals(app.Source, "registry", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(app.UninstallCommand))
        {
            return "No uninstaller registered.";
        }

        if (!TryMapSourceToAction(app, out _))
        {
            return $"Uninstall not supported for source '{app.Source}'.";
        }

        return $"Uninstall {app.Name} via {app.Source}.";
    }

    private async Task OnUninstallClicked(InstalledApp app)
    {
        if (_device is null || !CanUninstallApp(app))
        {
            return;
        }

        if (!TryMapSourceToAction(app, out var sourceEnum))
        {
            return;
        }

        var packageId = ResolvePackageIdForUninstall(app, sourceEnum);
        if (string.IsNullOrWhiteSpace(packageId))
        {
            ToastService.ShowToast2(
                "No package id available for uninstall.",
                Enums.ToastType.Warning);
            return;
        }

        var displayCommand = sourceEnum switch
        {
            SoftwareActionSource.Winget => $"winget uninstall {packageId}",
            SoftwareActionSource.Choco => $"choco uninstall {packageId}",
            SoftwareActionSource.Apt => $"apt-get remove {packageId}",
            SoftwareActionSource.Brew => $"brew uninstall {packageId}",
            SoftwareActionSource.Msi => $"the registered uninstaller for {packageId}",
            _ => $"uninstall {packageId}",
        };

        var confirmed = await JsInterop.Confirm(
            $"This will run \"{displayCommand}\" on {_device.DeviceName}. " +
            "Output appears under Script History. Continue?");

        if (!confirmed)
        {
            return;
        }

        var key = AppKey(app);
        _pendingUninstallKeys.Add(key);
        try
        {
            var result = await CircuitConnection.RequestSoftwareUninstall(
                _device.ID,
                sourceEnum,
                packageId,
                app.Name);

            if (!result.IsSuccess)
            {
                ToastService.ShowToast2(result.Reason, Enums.ToastType.Error);
                return;
            }

            ToastService.ShowToast("Uninstall queued.");
        }
        finally
        {
            _pendingUninstallKeys.Remove(key);
            await InvokeAsync(StateHasChanged);
        }
    }

    private static string? ResolvePackageIdForUninstall(InstalledApp app, SoftwareActionSource source)
    {
        // For winget/choco/brew/apt the package "id" we want is the
        // human-typeable name (winget uses Id values like Git.Git but the
        // Phase 1 enumerator stores those in Name when sourced from
        // winget; for registry entries we fall back to DisplayName which
        // the MSI uninstall PowerShell template handles).
        return app.Name;
    }

    private void OpenInstallSoftwareModal()
    {
        _isInstallModalOpen = true;
    }

    private Task CloseInstallSoftwareModal()
    {
        _isInstallModalOpen = false;
        return Task.CompletedTask;
    }

    private async Task OnInstallQueued(int scriptRunId)
    {
        // Schedule a follow-up inventory refresh so the new app appears
        // without the user clicking Refresh manually. ScriptResultsController
        // also refreshes on result, but this is a belt-and-braces
        // background nudge while the install runs.
        _isInstallModalOpen = false;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(45));
                if (_device is not null)
                {
                    await CircuitConnection.RefreshDeviceInventory(_device.ID);
                }
            }
            catch
            {
                // Best-effort.
            }
        });
        await InvokeAsync(StateHasChanged);
    }

    private void ShowFullScriptOutput(ScriptResult result)
    {
        void outputModal(RenderTreeBuilder builder)
        {
            var output = string.Join("\r\n", result.StandardOutput ?? Array.Empty<string>());
            var error = string.Join("\r\n", result.ErrorOutput ?? Array.Empty<string>());
            var textareaStyle = "width: 100%; height: 200px; white-space: pre;";

            builder.AddMarkupContent(0, "<h5>Input</h5>");
            builder.AddMarkupContent(1, $"<textarea readonly style=\"{textareaStyle}\">{result.ScriptInput}</textarea>");
            builder.AddMarkupContent(2, "<h5 class=\"mt-3\">Standard Output</h5>");
            builder.AddMarkupContent(3, $"<textarea readonly style=\"{textareaStyle}\">{output}</textarea>");
            builder.AddMarkupContent(4, "<h5 class=\"mt-3\">Error Output</h5>");
            builder.AddMarkupContent(3, $"<textarea readonly style=\"{textareaStyle}\">{error}</textarea>");
        }

        ModalService.ShowModal("Script Input/Output", outputModal);
    }

    private void LoadServices()
    {
        if (_services is null && _device is not null && _device.IsOnline)
        {
            _ = RefreshServices();
        }
    }

    private async Task RefreshServices()
    {
        if (_device is null || _isServicesRefreshing)
        {
            return;
        }

        _isServicesRefreshing = true;
        _servicesError = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            _services = await CircuitConnection.GetDeviceServices(_device.ID);
            if (_services.Length == 0)
            {
                _servicesError = "No services were returned. The device may be offline or unable to enumerate.";
            }
        }
        catch (Exception ex)
        {
            _servicesError = ex.Message;
            ToastService.ShowToast2("Failed to load services.", Enums.ToastType.Error);
        }
        finally
        {
            _isServicesRefreshing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void SortServices(ServiceSortKey key)
    {
        if (_servicesSortKey == key)
        {
            _servicesSortDescending = !_servicesSortDescending;
        }
        else
        {
            _servicesSortKey = key;
            _servicesSortDescending = false;
        }
    }

    private string ServiceSortIndicator(ServiceSortKey key)
    {
        if (_servicesSortKey != key)
        {
            return string.Empty;
        }
        return _servicesSortDescending ? "▼" : "▲";
    }

    private static string ServiceStatusBadge(ServiceStatus status) => status switch
    {
        ServiceStatus.Running => "bg-success",
        ServiceStatus.Stopped => "bg-secondary",
        ServiceStatus.Paused => "bg-warning text-dark",
        ServiceStatus.Starting => "bg-info text-dark",
        ServiceStatus.Stopping => "bg-info text-dark",
        _ => "bg-light text-dark",
    };

    private IEnumerable<ServiceInfo> GetFilteredSortedServices()
    {
        if (_services is null)
        {
            return Array.Empty<ServiceInfo>();
        }

        IEnumerable<ServiceInfo> query = _services;

        if (!string.IsNullOrWhiteSpace(_servicesSearchTerm))
        {
            var term = _servicesSearchTerm.Trim();
            query = query.Where(x =>
                (x.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.DisplayName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        query = _servicesSortKey switch
        {
            ServiceSortKey.DisplayName => query.OrderBy(x => x.DisplayName ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            ServiceSortKey.Status => query.OrderBy(x => x.Status),
            ServiceSortKey.StartType => query.OrderBy(x => x.StartType),
            ServiceSortKey.ProcessId => query.OrderBy(x => x.ProcessId ?? int.MaxValue),
            _ => query.OrderBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase),
        };

        if (_servicesSortDescending)
        {
            query = query.Reverse();
        }

        return query;
    }

    private async Task OnServiceActionClicked(ServiceInfo service, string action)
    {
        if (_device is null || string.IsNullOrWhiteSpace(service.Name))
        {
            return;
        }

        if (action is "stop" or "restart")
        {
            var verb = action == "stop" ? "Stop" : "Restart";
            var confirmed = await JsInterop.Confirm(
                $"{verb} \"{service.DisplayName ?? service.Name}\" on {_device.DeviceName}?");
            if (!confirmed)
            {
                return;
            }
        }

        _pendingServiceKeys.Add(service.Name);
        try
        {
            var success = await CircuitConnection.ControlDeviceService(_device.ID, service.Name, action);
            if (!success)
            {
                ToastService.ShowToast2(
                    $"Failed to {action} service.",
                    Enums.ToastType.Error);
                return;
            }

            ToastService.ShowToast($"Service {action} sent.");
            await RefreshServices();
        }
        finally
        {
            _pendingServiceKeys.Remove(service.Name);
            await InvokeAsync(StateHasChanged);
        }
    }

    private void LoadProcesses()
    {
        if (_processes is null && _device is not null && _device.IsOnline)
        {
            _ = RefreshProcesses();
        }
    }

    private async Task RefreshProcesses()
    {
        if (_device is null || _isProcessesRefreshing)
        {
            return;
        }

        _isProcessesRefreshing = true;
        _processesError = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            _processes = await CircuitConnection.GetDeviceProcesses(_device.ID);
            if (_processes.Length == 0)
            {
                _processesError = "No processes were returned. The device may be offline or unable to enumerate.";
            }
        }
        catch (Exception ex)
        {
            _processesError = ex.Message;
            ToastService.ShowToast2("Failed to load processes.", Enums.ToastType.Error);
        }
        finally
        {
            _isProcessesRefreshing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void SortProcesses(ProcessSortKey key)
    {
        if (_processesSortKey == key)
        {
            _processesSortDescending = !_processesSortDescending;
        }
        else
        {
            _processesSortKey = key;
            _processesSortDescending = false;
        }
    }

    private string ProcessSortIndicator(ProcessSortKey key)
    {
        if (_processesSortKey != key)
        {
            return string.Empty;
        }
        return _processesSortDescending ? "▼" : "▲";
    }

    private IEnumerable<ProcessInfo> GetFilteredSortedProcesses()
    {
        if (_processes is null)
        {
            return Array.Empty<ProcessInfo>();
        }

        IEnumerable<ProcessInfo> query = _processes;

        if (!string.IsNullOrWhiteSpace(_processesSearchTerm))
        {
            var term = _processesSearchTerm.Trim();
            query = query.Where(x =>
                (x.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.UserName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        query = _processesSortKey switch
        {
            ProcessSortKey.Pid => query.OrderBy(x => x.Pid),
            ProcessSortKey.UserName => query.OrderBy(x => x.UserName ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            ProcessSortKey.WorkingSet => query.OrderBy(x => x.WorkingSetBytes),
            ProcessSortKey.Cpu => query.OrderBy(x => x.CpuPercent ?? -1),
            _ => query.OrderBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase),
        };

        if (_processesSortDescending)
        {
            query = query.Reverse();
        }

        return query;
    }

    private static string FormatMb(long bytes)
    {
        var mb = bytes / 1024d / 1024d;
        return mb.ToString("0.0");
    }

    private async Task OnKillProcessClicked(ProcessInfo proc)
    {
        if (_device is null || proc.Pid <= 0)
        {
            return;
        }

        var confirmed = await JsInterop.Confirm(
            $"Kill {proc.Name} (PID {proc.Pid}) on {_device.DeviceName}?");
        if (!confirmed)
        {
            return;
        }

        _pendingKillPids.Add(proc.Pid);
        try
        {
            var success = await CircuitConnection.KillDeviceProcess(_device.ID, proc.Pid);
            if (!success)
            {
                ToastService.ShowToast2("Failed to kill process.", Enums.ToastType.Error);
                return;
            }

            ToastService.ShowToast("Kill signal sent.");
            await RefreshProcesses();
        }
        finally
        {
            _pendingKillPids.Remove(proc.Pid);
            await InvokeAsync(StateHasChanged);
        }
    }
}