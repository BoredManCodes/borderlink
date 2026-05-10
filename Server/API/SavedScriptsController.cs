using Microsoft.AspNetCore.Mvc;
using BorderLink.Server.Auth;
using BorderLink.Server.Services;
using BorderLink.Shared.Constants;
using BorderLink.Shared.Entities;

namespace BorderLink.Server.API;

[Route("api/[controller]")]
[ApiController]
public class SavedScriptsController : ControllerBase
{
    private readonly IDataService _dataService;
    private readonly ILogger<SavedScriptsController> _logger;

    public SavedScriptsController(
        IDataService dataService,
        ILogger<SavedScriptsController> logger)
    {
        _dataService = dataService;
        _logger = logger;
    }

    [ServiceFilter(typeof(ExpiringTokenFilter))]
    [HttpGet("{scriptId}")]
    public async Task<ActionResult<SavedScript>> GetScript(Guid scriptId, [FromQuery(Name = "runId")] int? runId = null)
    {
        var result = await _dataService.GetSavedScript(scriptId);
        if (!result.IsSuccess)
        {
            return NotFound();
        }

        var savedScript = result.Value;

        // Software-action saved scripts are parameterised one-liners
        // shared across the org. We must look up the linked
        // SoftwareActionRun by ScriptRunId and substitute the package id
        // before handing the script content to the agent. Without a
        // runId we cannot resolve the action — refuse to return a
        // template that would otherwise execute as `winget uninstall
        // --id  --silent` and remove unrelated software.
        if (SoftwareActionScriptIds.All.Contains(savedScript.Id))
        {
            if (runId is null)
            {
                _logger.LogWarning(
                    "Software-action script {scriptId} requested without runId; refusing to return template content.",
                    savedScript.Id);
                return NotFound();
            }

            var actionRun = await _dataService.GetSoftwareActionRunByScriptRunId(runId.Value);
            if (actionRun is null)
            {
                _logger.LogWarning(
                    "Software-action script {scriptId} requested but no SoftwareActionRun exists for ScriptRunId {runId}.",
                    savedScript.Id,
                    runId);
                return NotFound();
            }

            if (string.IsNullOrEmpty(savedScript.Content))
            {
                return NotFound();
            }

            try
            {
                savedScript.Content = string.Format(savedScript.Content, actionRun.PackageId);
            }
            catch (FormatException ex)
            {
                _logger.LogError(
                    ex,
                    "Software-action script {scriptId} has malformed format template.",
                    savedScript.Id);
                return NotFound();
            }
        }

        return savedScript;
    }
}
