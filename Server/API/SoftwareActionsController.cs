using BorderLink.Server.Hubs;
using BorderLink.Server.Services;
using BorderLink.Shared;
using BorderLink.Shared.Entities;
using BorderLink.Shared.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace BorderLink.Server.API;

[ApiController]
[Authorize]
[Route("api/software-actions")]
public class SoftwareActionsController : ControllerBase
{
    private static readonly TimeSpan _searchTimeout = TimeSpan.FromSeconds(60);

    private readonly IHubContext<AgentHub> _agentHubContext;
    private readonly IAgentHubSessionCache _agentSessionCache;
    private readonly IDataService _dataService;
    private readonly IAuditLogService _auditLog;
    private readonly ILogger<SoftwareActionsController> _logger;

    public SoftwareActionsController(
        IHubContext<AgentHub> agentHubContext,
        IAgentHubSessionCache agentSessionCache,
        IDataService dataService,
        IAuditLogService auditLog,
        ILogger<SoftwareActionsController> logger)
    {
        _agentHubContext = agentHubContext;
        _agentSessionCache = agentSessionCache;
        _dataService = dataService;
        _auditLog = auditLog;
        _logger = logger;
    }

    /// <summary>
    /// Asks the connected agent to search its package managers
    /// (winget/choco/apt/brew) for installable packages matching the
    /// query. Used by the install picker on the Apps tab.
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<SoftwarePackage[]>> Search(
        [FromQuery] string deviceId,
        [FromQuery] string q,
        [FromQuery] int max = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest("deviceId is required.");
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return Array.Empty<SoftwarePackage>();
        }

        var userResult = await _dataService.GetUserByName($"{User.Identity?.Name}");
        if (!userResult.IsSuccess)
        {
            return Unauthorized();
        }

        var user = userResult.Value;
        if (!_dataService.DoesUserHaveAccessToDevice(deviceId, user))
        {
            _logger.LogWarning(
                "Software search attempted by unauthorized user. Device: {deviceId}. User: {userName}.",
                deviceId, user.UserName);
            await _auditLog.LogAsync(
                AuditActions.SoftwareSearchPerformed,
                user.OrganizationID,
                userName: user.UserName,
                userId: user.Id,
                targetType: "Device",
                targetId: deviceId,
                success: false,
                resultMessage: "User lacks access to device.");
            return Unauthorized();
        }

        if (!_agentSessionCache.TryGetConnectionId(deviceId, out var connectionId) ||
            string.IsNullOrWhiteSpace(connectionId))
        {
            return BadRequest("Device is not currently online.");
        }

        var cappedMax = Math.Clamp(max, 1, 200);

        SoftwarePackage[] results;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_searchTimeout);
            results = await _agentHubContext.Clients
                .Client(connectionId)
                .InvokeAsync<SoftwarePackage[]>(
                    nameof(IAgentHubClient.SearchAvailablePackages),
                    q,
                    cappedMax,
                    cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Software search timed out for device {deviceId}, query '{query}'.",
                deviceId, q);
            return StatusCode(StatusCodes.Status504GatewayTimeout, "The agent did not respond in time.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Software search failed for device {deviceId}, query '{query}'.",
                deviceId, q);
            return StatusCode(StatusCodes.Status502BadGateway, "The agent failed to respond.");
        }

        await _auditLog.LogAsync(
            AuditActions.SoftwareSearchPerformed,
            user.OrganizationID,
            userName: user.UserName,
            userId: user.Id,
            targetType: "Device",
            targetId: deviceId,
            details: new { query = q, max = cappedMax, resultCount = results?.Length ?? 0 });

        return results ?? Array.Empty<SoftwarePackage>();
    }
}
