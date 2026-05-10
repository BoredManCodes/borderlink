namespace BorderLink.Shared;

/// <summary>
/// Canonical action strings written to <see cref="Entities.AuditLogEntry.Action"/>.
/// Treat these as a stable, append-only vocabulary — renaming a value invalidates
/// historic log queries.
/// </summary>
public static class AuditActions
{
    // Authentication
    public const string LoginSuccess                 = "Login.Success";
    public const string LoginFailure                 = "Login.Failure";
    public const string LoginLockedOut               = "Login.LockedOut";
    public const string LoginTwoFactorRequired       = "Login.TwoFactorRequired";
    public const string LoginTwoFactorSuccess        = "Login.TwoFactorSuccess";
    public const string LoginTwoFactorFailure        = "Login.TwoFactorFailure";
    public const string LoginRecoveryCodeSuccess     = "Login.RecoveryCodeSuccess";
    public const string LoginRecoveryCodeFailure     = "Login.RecoveryCodeFailure";
    public const string Logout                       = "Login.Logout";

    // Account self-service
    public const string AccountPasswordChanged       = "Account.PasswordChanged";
    public const string AccountPasswordReset         = "Account.PasswordReset";
    public const string AccountTwoFactorEnabled      = "Account.TwoFactorEnabled";
    public const string AccountTwoFactorDisabled     = "Account.TwoFactorDisabled";
    public const string AccountTwoFactorReset        = "Account.TwoFactorReset";
    public const string AccountEmailChanged          = "Account.EmailChanged";
    public const string AccountPersonalDataExported  = "Account.PersonalDataExported";
    public const string AccountDeleted               = "Account.Deleted";

    // Remote control
    public const string RemoteControlStart           = "RemoteControl.Start";
    public const string RemoteControlStartApi        = "RemoteControl.StartApi";
    public const string RemoteControlQuickConnect    = "RemoteControl.QuickConnect";
    public const string RemoteControlBlocked         = "RemoteControl.Blocked";

    // Scripts
    public const string ScriptRun                    = "Script.Run";
    public const string ScriptCommandExecute         = "Script.CommandExecute";
    public const string ScriptSaved                  = "Script.Saved";
    public const string ScriptDeleted                = "Script.Deleted";
    public const string ScriptScheduleCreated        = "Script.ScheduleCreated";
    public const string ScriptScheduleDeleted        = "Script.ScheduleDeleted";

    // Device lifecycle
    public const string DeviceUninstall              = "Device.Uninstall";
    public const string DeviceReinstall              = "Device.Reinstall";
    public const string DeviceRemove                 = "Device.Remove";
    public const string DeviceWake                   = "Device.Wake";
    public const string DeviceTagsUpdate             = "Device.TagsUpdate";
    public const string DeviceMetadataUpdate         = "Device.MetadataUpdate";
    public const string DeviceLogsViewed             = "Device.LogsViewed";
    public const string DeviceLogsDeleted            = "Device.LogsDeleted";

    // Device groups
    public const string DeviceGroupCreated           = "DeviceGroup.Created";
    public const string DeviceGroupUpdated           = "DeviceGroup.Updated";
    public const string DeviceGroupRemoved           = "DeviceGroup.Removed";

    // Files / chat
    public const string FileTransferToAgent          = "File.TransferToAgent";
    public const string FileSharedUpload             = "File.SharedUpload";
    public const string FileSharedDownload           = "File.SharedDownload";
    public const string ChatMessageSent              = "Chat.MessageSent";

    // Organization / users
    public const string OrganizationRenamed          = "Organization.Renamed";
    public const string UserInvited                  = "User.Invited";
    public const string UserInviteRevoked            = "User.InviteRevoked";
    public const string UserRoleChanged              = "User.RoleChanged";
    public const string UserRemoved                  = "User.Removed";
    public const string UserAdminToggled             = "User.AdminToggled";

    // Configuration
    public const string BrandingUpdated              = "Branding.Updated";
    public const string ServerConfigUpdated          = "ServerConfig.Updated";
    public const string SettingsUpdated              = "Settings.Updated";

    // API keys
    public const string ApiKeyCreated                = "ApiKey.Created";
    public const string ApiKeyDeleted                = "ApiKey.Deleted";

    // Alerts
    public const string AlertCreated                 = "Alert.Created";
    public const string AlertDismissed               = "Alert.Dismissed";

    // Downloads
    public const string AgentInstallerDownload       = "Installer.Download";
    public const string CustomBinaryUpload           = "Installer.CustomBinaryUpload";

    // Inventory
    public const string InventoryRefresh             = "Inventory.Refresh";

    // Software actions
    public const string SoftwareInstallRequested     = "Software.InstallRequested";
    public const string SoftwareUninstallRequested   = "Software.UninstallRequested";
    public const string SoftwareSearchPerformed      = "Software.SearchPerformed";

    // Services & processes
    public const string ServicesViewed               = "Services.Viewed";
    public const string ServiceStart                 = "Service.Start";
    public const string ServiceStop                  = "Service.Stop";
    public const string ServiceRestart               = "Service.Restart";
    public const string ProcessKill                  = "Process.Kill";

    // Monitoring
    public const string MonitorRuleCreated           = "MonitorRule.Created";
    public const string MonitorRuleUpdated           = "MonitorRule.Updated";
    public const string MonitorRuleDeleted           = "MonitorRule.Deleted";
    public const string MonitorAlertFired            = "MonitorRule.Fired";
}
