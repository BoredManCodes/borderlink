using BorderLink.Shared.Enums;

namespace BorderLink.Shared.Interfaces;

public interface IAgentHubClient
{
    Task ChangeWindowsSession(
        string viewerConnectionId,
        string sessionId,
        string accessKey,
        string userConnectionId,
        string requesterName,
        string orgName,
        string orgId,
        int targetSessionId);

    Task SendChatMessage(
        string senderName, 
        string message, 
        string orgName, 
        string orgId, 
        bool disconnected, 
        string senderConnectionId);

    Task InvokeCtrlAltDel();

    Task DeleteLogs();

    Task ExecuteCommand(
        ScriptingShell shell, 
        string command, 
        string authToken, 
        string senderUsername, 
        string senderConnectionId);

    Task ExecuteCommandFromApi(ScriptingShell shell,
            string authToken,
            string requestID,
            string command,
            string senderUsername);

    Task<List<InstalledApp>> GetInstalledApps();

    /// <summary>
    /// Search the device's package managers (winget/choco/apt/brew) for
    /// candidate packages matching <paramref name="query"/>. Returns at
    /// most <paramref name="max"/> results. Empty list if no package
    /// manager is available.
    /// </summary>
    Task<SoftwarePackage[]> SearchAvailablePackages(string query, int max);

    /// <summary>
    /// Returns a snapshot of services / daemons on the device. Empty array
    /// when the per-OS enumerator can't query its source.
    /// </summary>
    Task<ServiceInfo[]> GetServices();

    /// <summary>
    /// Start, stop or restart a service on the device. <paramref name="action"/>
    /// must be one of "start", "stop", "restart"; anything else fails closed.
    /// </summary>
    Task<bool> ControlService(string name, string action);

    /// <summary>
    /// Returns a snapshot of running processes on the device.
    /// </summary>
    Task<ProcessInfo[]> GetProcesses();

    /// <summary>
    /// Terminate the process with the given PID. Returns <c>true</c> if the
    /// process is no longer running afterwards.
    /// </summary>
    Task<bool> KillProcess(int pid);

    /// <summary>
    /// Returns the OS pending-update list. Long-running on Windows
    /// (Microsoft.Update.Session search can take 30+ seconds), so callers
    /// should pad their invoke timeout accordingly.
    /// </summary>
    Task<PatchUpdate[]> GetPendingUpdates();

    /// <summary>
    /// Probe the device for pending-reboot signals (CBS, WindowsUpdate,
    /// PendingFileRenameOperations on Windows; <c>/var/run/reboot-required</c>
    /// on Linux). Cheap — safe to call from monitor evaluation.
    /// </summary>
    Task<PendingRebootInfo> GetPendingReboot();

    /// <summary>
    /// Begin installing the given update. Returns <c>true</c> when the
    /// install was queued — completion is reported separately via
    /// <c>ReportPatchInstallProgress</c>.
    /// </summary>
    Task<bool> InstallUpdate(string updateId);

    Task GetLogs(string senderConnectionId);

    Task GetPowerShellCompletions(
        string inputText, 
        int currentIndex, 
        CompletionIntent intent, 
        bool? forward, 
        string senderConnectionId);

    Task ReinstallAgent();

    Task UninstallAgent();

    Task RemoteControl(
        Guid sessionId, 
        string accessKey, 
        string userConnectionId, 
        string requesterName, 
        string orgName, 
        string orgId);

    Task RestartScreenCaster(
        string[] viewerIds, 
        string sessionId, 
        string accessKey, 
        string userConnectionId, 
        string requesterName, 
        string orgName, 
        string orgId);

    Task RunScript(
        Guid savedScriptId, 
        int scriptRunId, 
        string initiator, 
        ScriptInputType scriptInputType, 
        string authToken);

    Task TransferFileFromBrowserToAgent(
        string transferId, 
        string[] fileIds, 
        string requesterId, 
        string expiringToken);

    Task TriggerHeartbeat();

    Task WakeDevice(string macAddress);
}
