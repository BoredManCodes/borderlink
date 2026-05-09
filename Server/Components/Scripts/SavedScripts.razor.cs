using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using BorderLink.Server.Components.Pages;
using BorderLink.Server.Enums;
using BorderLink.Server.Services;
using BorderLink.Shared;
using BorderLink.Shared.Entities;

namespace BorderLink.Server.Components.Scripts;

[Authorize]
public partial class SavedScripts : AuthComponentBase
{
    [CascadingParameter]
    private ScriptsPage ParentPage { get; set; } = null!;

    private SavedScript _selectedScript = new() { Name = string.Empty };
    private string _alertMessage = string.Empty;
    private string _alertOptionsShowClass = string.Empty;
    private string _environmentVarsShowClass = string.Empty;

    [Inject]
    public IDataService DataService { get; set; } = null!;

    [Inject]
    public IToastService ToastService { get; set; } = null!;

    [Inject]
    public IJsInterop JsInterop { get; set; } = null!;

    [Inject]
    public IModalService ModalService { get; set; } = null!;

    [Inject]
    public required ILogger<SavedScripts> Logger { get; set; }

    [Inject]
    public IAuditLogService AuditService { get; set; } = null!;

    private bool CanModifyScript
    {
        get
        {
            if (User is null)
            {
                return false;
            }

            return 
                _selectedScript.Id == Guid.Empty ||
                _selectedScript.CreatorId == User.Id || User.IsAdministrator;
        }
    }

    private bool CanDeleteScript
    {
        get
        {
            if (User is null)
            {
                return false;
            }

            return 
                !string.IsNullOrWhiteSpace(_selectedScript.CreatorId) &&
                (_selectedScript.CreatorId == User.Id || User.IsAdministrator);
        }
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            JsInterop.AutoHeight();
        }
        base.OnAfterRender(firstRender);
    }

    private async Task OnValidSubmit(EditContext context)
    {
        EnsureUserSet();

        if (_selectedScript is null)
        {
            return;
        }

        if (!CanModifyScript)
        {
            ToastService.ShowToast("You can't modify other people's scripts.", classString: "bg-warning");
            return;
        }
        
        await DataService.AddOrUpdateSavedScript(_selectedScript, User.Id);
        await AuditService.LogAsync(
            AuditActions.ScriptSaved,
            User.OrganizationID,
            userName: User.UserName,
            userId: User.Id,
            targetType: "SavedScript",
            targetId: _selectedScript.Id.ToString(),
            targetName: _selectedScript.Name);
        await ParentPage.RefreshScripts();
        ToastService.ShowToast("Script saved.");
        _alertMessage = "Script saved.";
    }

    private void CreateNew()
    {
        _selectedScript = new()
        {
            Name = string.Empty
        };
    }

    private async Task DeleteSelectedScript()
    {
        try
        {
            EnsureUserSet();

            if (!CanDeleteScript)
            {
                ToastService.ShowToast("You can't delete other people's scripts.", classString: "bg-warning");
                return;
            }

            var result = await JsInterop.Confirm($"Are you sure you want to delete the script {_selectedScript.Name}?");
            if (result)
            {
                var deletedId = _selectedScript.Id;
                var deletedName = _selectedScript.Name;
                await DataService.DeleteSavedScript(_selectedScript.Id);
                await AuditService.LogAsync(
                    AuditActions.ScriptDeleted,
                    User.OrganizationID,
                    userName: User.UserName,
                    userId: User.Id,
                    targetType: "SavedScript",
                    targetId: deletedId.ToString(),
                    targetName: deletedName);
                ToastService.ShowToast("Script deleted.");
                _alertMessage = "Script deleted.";
                await ParentPage.RefreshScripts();
                _selectedScript = new()
                {
                    Name = string.Empty
                };
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error while deleting script.");
            ToastService.ShowToast2("Failed to delete script", ToastType.Error);
        }

    }

    private async Task ScriptSelected(ScriptTreeNode viewModel)
    {
        EnsureUserSet();
        if (viewModel.Script is not null)
        {
            var result = await DataService.GetSavedScript(User.Id, viewModel.Script.Id);
            if (result.IsSuccess)
            {
                _selectedScript = result.Value;
                return;
            }
        }

        _selectedScript = new()
        {
            Name = string.Empty
        };
    }

    private void ToggleEnvironmentVarsShown()
    {
        if (string.IsNullOrWhiteSpace(_environmentVarsShowClass))
        {
            _environmentVarsShowClass = "show";
        }
        else
        {
            _environmentVarsShowClass = string.Empty;
        }
    }
    private void ToggleAlertOptionsShown()
    {
        if (string.IsNullOrWhiteSpace(_alertOptionsShowClass))
        {
            _alertOptionsShowClass = "show";
        }
        else
        {
            _alertOptionsShowClass = string.Empty;
        }
    }

    private void ShowAlertErrorHelp()
    {
        ModalService.ShowModal("Script Error Alerts", new[]
        {
            "A script will report as having an error if there is anything written to the error output. " +
            "For CMD, WinPS, and Bash, an exit code of anything other than 0 will also trigger an error. " +
            "(The integrated PowerShell Core shell does not use exit codes.)",

            "For PowerShell Core, any text written Warning or Error output will trigger an alert." +
            "To manually generate an error based on some logic in your script, use " +
            "'Write-Error' or 'Write-Warning' anywhere in your PowerShell Core script. ",

            "For Windows PowerShell, you can manually trigger an alert by using 'Write-Error'.",

            "For Bash and CMD, you can exit with any non-zero number."
        });
    }
}
