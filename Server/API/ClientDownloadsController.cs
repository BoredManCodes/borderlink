using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BorderLink.Server.Auth;
using BorderLink.Server.Extensions;
using BorderLink.Server.Services;
using BorderLink.Shared;
using BorderLink.Shared.Extensions;
using BorderLink.Shared.Models;
using BorderLink.Shared.Services;
using System.Text;
using System.Text.Json;
using FileIO = System.IO.File;

namespace BorderLink.Server.API;

[Route("api/[controller]")]
[ApiController]
public class ClientDownloadsController : ControllerBase
{
    private readonly IDataService _dataService;
    private readonly IEmbeddedServerDataProvider _embeddedDataSearcher;
    private readonly IAuditLogService _auditLog;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly IWebHostEnvironment _hostEnv;
    private readonly ILogger<ClientDownloadsController> _logger;

    public ClientDownloadsController(
        IWebHostEnvironment hostEnv,
        IEmbeddedServerDataProvider embeddedDataSearcher,
        IDataService dataService,
        IAuditLogService auditLog,
        ILogger<ClientDownloadsController> logger)
    {
        _hostEnv = hostEnv;
        _embeddedDataSearcher = embeddedDataSearcher;
        _dataService = dataService;
        _auditLog = auditLog;
        _logger = logger;
    }

    [HttpGet("desktop/{platformID}")]
    public async Task<IActionResult> GetDesktop(string platformID)
    {
        switch (platformID)
        {
            case "WindowsDesktop-x64":
                {
                    var filePath = Path.Combine("Content", "Win-x64", "BorderLink_Desktop.exe");
                    return await GetDesktopFile(filePath);
                }
            case "WindowsDesktop-x86":
                {
                    var filePath = Path.Combine("Content", "Win-x86", "BorderLink_Desktop.exe");
                    return await GetDesktopFile(filePath);
                }
            case "UbuntuDesktop":
                {
                    var filePath = Path.Combine("Content", "Linux-x64", "BorderLink_Desktop");
                    return await GetDesktopFile(filePath);
                }
            case "MacOS-x64":
                {
                    var filePath = Path.Combine("Content", "MacOS-x64", "BorderLink_Desktop");
                    return await GetDesktopFile(filePath);
                }
            case "MacOS-arm64":
                {
                    var filePath = Path.Combine("Content", "MacOS-arm64", "BorderLink_Desktop");
                    return await GetDesktopFile(filePath);
                }
            default:
                return NotFound();
        }
    }


    [HttpGet("desktop/{platformId}/{organizationId}")]
    public async Task<IActionResult> GetDesktop(string platformId, string organizationId)
    {
        switch (platformId)
        {
            case "WindowsDesktop-x64":
                {
                    var filePath = Path.Combine("Content", "Win-x64", "BorderLink_Desktop.exe");
                    return await GetDesktopFile(filePath, organizationId);
                }
            case "WindowsDesktop-x86":
                {
                    var filePath = Path.Combine("Content", "Win-x86", "BorderLink_Desktop.exe");
                    return await GetDesktopFile(filePath, organizationId);
                }
            case "UbuntuDesktop":
                {
                    var filePath = Path.Combine("Content", "Linux-x64", "BorderLink_Desktop");
                    return await GetDesktopFile(filePath, organizationId);
                }
            case "MacOS-x64":
                {
                    var filePath = Path.Combine("Content", "MacOS-x64", "BorderLink_Desktop");
                    return await GetDesktopFile(filePath);
                }
            case "MacOS-arm64":
                {
                    var filePath = Path.Combine("Content", "MacOS-arm64", "BorderLink_Desktop");
                    return await GetDesktopFile(filePath);
                }
            default:
                return NotFound();
        }
    }

    [ServiceFilter(typeof(ApiAuthorizationFilter))]
    [HttpGet("{platformID}")]
    public async Task<IActionResult> GetInstaller(string platformID)
    {
        if (!Request.Headers.TryGetOrganizationId(out var orgId))
        {
            return Unauthorized();
        }
        return await GetInstallFile(orgId, platformID);
    }

    /// <summary>
    /// Returns the prebuilt NSIS agent installer with the org id and server
    /// URL encoded into the filename. The installer reads its own filename
    /// at startup, decodes the suffix, and pre-fills its config — so the
    /// recipient can just double-click and go.
    /// </summary>
    [Authorize]
    [HttpGet("agent/{platformId}")]
    public async Task<IActionResult> GetAgentInstaller(
        string platformId,
        [FromQuery] string? alias = null,
        [FromQuery] string? group = null)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Unauthorized();
        }

        var userResult = await _dataService.GetUserByName(userName);
        if (!userResult.IsSuccess)
        {
            return Unauthorized();
        }

        return await GetAgentInstallerFile(platformId, userResult.Value.OrganizationID, alias, group);
    }

    [HttpGet("agent/{platformId}/{organizationId}")]
    public async Task<IActionResult> GetAgentInstaller(
        string platformId,
        string organizationId,
        [FromQuery] string? alias = null,
        [FromQuery] string? group = null)
    {
        return await GetAgentInstallerFile(platformId, organizationId, alias, group);
    }

    private async Task<IActionResult> GetAgentInstallerFile(
        string platformId,
        string organizationId,
        string? alias,
        string? group)
    {
        await LogRequest(nameof(GetAgentInstallerFile));

        var arch = platformId switch
        {
            "WindowsAgentInstaller-x64" => "x64",
            "WindowsAgentInstaller-x86" => "x86",
            _ => null
        };

        if (arch is null)
        {
            return BadRequest("Unknown agent installer platform.");
        }

        var relativePath = Path.Combine("Content", $"BorderLink-Agent-Setup-{arch}.exe");
        var fullPath = Path.Combine(_hostEnv.WebRootPath, relativePath);
        if (!FileIO.Exists(fullPath))
        {
            _logger.LogWarning(
                "Agent installer not found at {path}. Build it with Agent\\Installer\\Windows\\Build-Installer.ps1 and copy to wwwroot/Content.",
                fullPath);
            return NotFound("Agent installer hasn't been built and published. See Agent\\Installer\\Windows\\README.md.");
        }

        var settings = await _dataService.GetSettings();
        var effectiveScheme = settings.ForceClientHttps ? "https" : Request.Scheme;
        var serverUrl = $"{effectiveScheme}://{Request.Host}";

        var fileName = BuildAgentInstallerFileName(arch, serverUrl, organizationId, alias, group);

        await _auditLog.LogAsync(
            AuditActions.AgentInstallerDownload,
            organizationId,
            userName: User.Identity?.Name,
            ipAddress: Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            details: new { arch, alias, group });

        return File(relativePath, "application/octet-stream", fileName);
    }

    private static string BuildAgentInstallerFileName(
        string arch,
        string serverUrl,
        string organizationId,
        string? alias,
        string? group)
    {
        // Schema is intentionally short ({u,o,a,g}) so the produced filename
        // stays well under the Windows MAX_PATH limit. Keep this in sync with
        // ParseFilenameConfig in Agent/Installer/Windows/BorderLink-Agent.nsi.
        var payload = new Dictionary<string, string> { ["u"] = serverUrl, ["o"] = organizationId };
        if (!string.IsNullOrWhiteSpace(alias)) payload["a"] = alias;
        if (!string.IsNullOrWhiteSpace(group)) payload["g"] = group;

        var json = JsonSerializer.Serialize(payload);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return $"BorderLink-Agent-Setup-{arch}__{token}.exe";
    }

    [HttpGet("{platformId}/{organizationId}")]
    public async Task<IActionResult> GetInstaller(string platformId, string organizationId)
    {
        return await GetInstallFile(organizationId, platformId);
    }

    private async Task<IActionResult> GetBashInstaller(string fileName, string organizationId)
    {
        var fileContents = new List<string>();
        fileContents.AddRange(await FileIO.ReadAllLinesAsync(Path.Combine(_hostEnv.WebRootPath, "Content", fileName)));

        var hostIndex = fileContents.IndexOf("HostName=");
        var orgIndex = fileContents.IndexOf("Organization=");

        var settings = await _dataService.GetSettings();
        var effectiveScheme = settings.ForceClientHttps ? "https" : Request.Scheme;

        fileContents[hostIndex] = $"HostName=\"{effectiveScheme}://{Request.Host}\"";
        fileContents[orgIndex] = $"Organization=\"{organizationId}\"";
        var fileBytes = Encoding.UTF8.GetBytes(string.Join("\n", fileContents));
        return File(fileBytes, "application/octet-stream", fileName);
    }

    private async Task<IActionResult> GetDesktopFile(string relativeFilePath, string? organizationId = null)
    {
        await LogRequest(nameof(GetDesktopFile));
        var defaultOrg = await _dataService.GetDefaultOrganization();

        // The default org will be used if unspecified, so might as well save the
        // space in the file name.
        if (defaultOrg.IsSuccess && 
            defaultOrg.Value.ID.Equals(organizationId, StringComparison.OrdinalIgnoreCase))
        {
            organizationId = null;
        }

        var settings = await _dataService.GetSettings();
        var effectiveScheme = settings.ForceClientHttps ? "https" : Request.Scheme;
        var serverUrl = $"{effectiveScheme}://{Request.Host}";

        var embeddedData = new EmbeddedServerData(new Uri(serverUrl), organizationId);
        var fileName = _embeddedDataSearcher.GetEncodedFileName(relativeFilePath, embeddedData);
        return File(relativeFilePath, "application/octet-stream", fileName);
    }

    private async Task<IActionResult> GetInstallFile(string organizationId, string platformID)
    {
        var settings = await _dataService.GetSettings();
        await LogRequest(nameof(GetInstallFile));

        await _auditLog.LogAsync(
            AuditActions.AgentInstallerDownload,
            organizationId,
            userName: User.Identity?.Name,
            ipAddress: Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            details: new { platform = platformID });

        if (!await _fileLock.WaitAsync(TimeSpan.FromSeconds(15)))
        {
            return StatusCode(StatusCodes.Status408RequestTimeout);
        }

        try
        {
            switch (platformID)
            {
                case "WindowsInstaller":
                    {
                        var effectiveScheme = settings.ForceClientHttps ? "https" : Request.Scheme;

                        var filePath = Path.Combine(_hostEnv.WebRootPath, "Content", "Install-BorderLink.ps1");
                        if (!FileIO.Exists(filePath))
                        {
                            return NotFound();
                        }
                        
                        var fileContents = await FileIO.ReadAllLinesAsync(filePath);
                        var hostIndex = fileContents.IndexWhere(x => 
                            x.Contains("[string]$HostName = $null", StringComparison.OrdinalIgnoreCase));
                        var orgIndex = fileContents.IndexWhere(x => 
                            x.Contains("[string]$Organization = $null", StringComparison.OrdinalIgnoreCase));

                        if (hostIndex < 0 || orgIndex < 0)
                        {
                            return NotFound();
                        }

                        fileContents[hostIndex] = $"[string]$HostName = \"{effectiveScheme}://{Request.Host}\"";
                        fileContents[orgIndex] = $"[string]$Organization = \"{organizationId}\"";
                        var fileBytes = Encoding.UTF8.GetBytes(string.Join("\n", fileContents));

                        return File(fileBytes, "application/octet-stream", "Install-BorderLink.ps1");
                    }
                case "ManjaroInstaller-x64":
                    {
                        var fileName = "Install-Manjaro-x64.sh";

                        return await GetBashInstaller(fileName, organizationId);
                    }
                case "UbuntuInstaller-x64":
                    {
                        var fileName = "Install-Ubuntu-x64.sh";

                        return await GetBashInstaller(fileName, organizationId);
                    }
                case "MacOSInstaller-x64":
                    {
                        var fileName = "Install-MacOS-x64.sh";

                        return await GetBashInstaller(fileName, organizationId);
                    }
                case "MacOSInstaller-arm64":
                    {
                        var fileName = "Install-MacOS-arm64.sh";

                        return await GetBashInstaller(fileName, organizationId);
                    }
                default:
                    return BadRequest();
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task LogRequest(string methodName)
    {
        var settings = await _dataService.GetSettings();
        if (settings.UseHttpLogging)
        {
            var ip = Request.HttpContext.Connection.RemoteIpAddress;
            if (ip?.IsIPv4MappedToIPv6 == true)
            {
                ip = ip.MapToIPv4();
            }

            var effectiveScheme = settings.ForceClientHttps ? "https" : Request.Scheme;

            _logger.LogInformation(
                "Started client download via {methodName}.  Effective Scheme: {scheme}.  Effective Host: {host}.  Remote IP: {ip}.",
                methodName,
                effectiveScheme,
                Request.Host,
                $"{ip}");
        }
    }
}
