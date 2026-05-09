using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using BorderLink.Server.Hubs;
using BorderLink.Server.Models;
using BorderLink.Server.Services;
using BorderLink.Shared;
using BorderLink.Shared.Helpers;
using BorderLink.Shared.Interfaces;

namespace BorderLink.Server.Pages;

[Authorize]
public class QuickConnectModel(
    IDataService _dataService,
    IRemoteControlSessionCache _remoteControlSessionCache,
    IAgentHubSessionCache _agentSessionCache,
    IHubContext<AgentHub, IAgentHubClient> _agentHub,
    IAuditLogService _auditLog,
    ILogger<QuickConnectModel> _logger) : PageModel
{
    public string DeviceId { get; private set; } = string.Empty;
    public string ErrorMessage { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGet(string? deviceId)
    {
        DeviceId = deviceId ?? string.Empty;

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            ErrorMessage = "No device was specified in the URL.";
            return Page();
        }

        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Challenge();
        }

        var userResult = await _dataService.GetUserByName(userName);
        if (!userResult.IsSuccess)
        {
            ErrorMessage = "Your user account could not be loaded.";
            return Page();
        }

        var user = userResult.Value;

        if (!_dataService.DoesUserHaveAccessToDevice(deviceId, user))
        {
            _logger.LogWarning(
                "Quick connect blocked: user {user} does not have access to device {device}.",
                user.UserName,
                deviceId);
            await _auditLog.LogAsync(
                AuditActions.RemoteControlBlocked,
                user.OrganizationID,
                userName: user.UserName,
                userId: user.Id,
                targetType: "Device",
                targetId: deviceId,
                ipAddress: GetClientIp(),
                success: false,
                resultMessage: "Quick connect: user lacks access to device.");
            ErrorMessage = "You don't have access to that device.";
            return Page();
        }

        if (!_agentSessionCache.TryGetByDeviceId(deviceId, out var targetDevice) ||
            !_agentSessionCache.TryGetConnectionId(deviceId, out var serviceConnectionId))
        {
            ErrorMessage = "That device isn't online right now. Try again once the agent is connected.";
            return Page();
        }

        if (targetDevice.OrganizationID != user.OrganizationID)
        {
            _logger.LogWarning(
                "Quick connect blocked: organization mismatch for user {user} and device {device}.",
                user.UserName,
                deviceId);
            ErrorMessage = "You don't have access to that device.";
            return Page();
        }

        var settings = await _dataService.GetSettings();

        var sessionId = Guid.NewGuid();
        var accessKey = RandomGenerator.GenerateAccessKey();

        var session = new RemoteControlSession
        {
            UnattendedSessionId = sessionId,
            UserConnectionId = HttpContext.Connection.Id,
            AgentConnectionId = serviceConnectionId,
            DeviceId = deviceId,
            OrganizationId = user.OrganizationID,
            RequireConsent = settings.EnforceAttendedAccess,
            NotifyUserOnStart = settings.RemoteControlNotifyUser
        };

        _remoteControlSessionCache.AddOrUpdate($"{sessionId}", session);

        var orgResult = await _dataService.GetOrganizationNameByUserName(userName);
        if (!orgResult.IsSuccess)
        {
            ErrorMessage = "Could not resolve your organization name.";
            return Page();
        }

        await _agentHub.Clients.Client(serviceConnectionId).RemoteControl(
            sessionId,
            accessKey,
            HttpContext.Connection.Id,
            user.UserOptions?.DisplayName ?? user.UserName ?? string.Empty,
            orgResult.Value,
            user.OrganizationID);

        var ready = await session.WaitForSessionReady(TimeSpan.FromSeconds(20));
        if (!ready)
        {
            ErrorMessage = "The remote control process didn't start in time. Please try again.";
            return Page();
        }

        await _auditLog.LogAsync(
            AuditActions.RemoteControlQuickConnect,
            user.OrganizationID,
            userName: user.UserName,
            userId: user.Id,
            targetType: "Device",
            targetId: deviceId,
            targetName: targetDevice.DeviceName,
            ipAddress: GetClientIp(),
            details: new { sessionId });

        return Redirect(
            $"/Viewer?mode=Unattended&sessionId={sessionId}&accessKey={accessKey}&viewonly=False");
    }

    private string? GetClientIp()
    {
        var ip = HttpContext.Connection.RemoteIpAddress;
        if (ip is null) return null;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return ip.ToString();
    }
}
