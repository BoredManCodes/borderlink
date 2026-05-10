using BorderLink.Server.Data;
using BorderLink.Shared.Constants;
using BorderLink.Shared.Entities;
using BorderLink.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace BorderLink.Server.Services;

/// <summary>
/// Ensures the nine well-known SavedScript rows that back the
/// software-action feature (winget/choco/apt/brew + msi uninstall) exist
/// at server startup. The rows are templates: each <c>Content</c>
/// contains a <c>{0}</c> placeholder that the SavedScripts API
/// substitutes with the linked <see cref="SoftwareActionRun.PackageId"/>
/// at fetch time. Seeding is idempotent — rows are only inserted when
/// missing; existing rows are not overwritten so customers can tweak the
/// templates if they want to.
/// </summary>
public class SoftwareActionScriptSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SoftwareActionScriptSeeder> _logger;

    public SoftwareActionScriptSeeder(
        IServiceScopeFactory scopeFactory,
        ILogger<SoftwareActionScriptSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SeedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to seed software-action saved scripts. The Apps tab " +
                "install/uninstall buttons will be unavailable until the " +
                "server is restarted with at least one organization and one " +
                "server-admin user provisioned.");
        }
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IAppDbFactory>();
        await using var db = dbFactory.GetContext();

        // Anchor seeded scripts to the default org if it exists, else the
        // first org we can find. Without any organization there's nowhere
        // to put them — defer until the next startup.
        var organization =
            await db.Organizations
                .AsNoTracking()
                .Where(x => x.IsDefaultOrganization)
                .FirstOrDefaultAsync(cancellationToken)
            ?? await db.Organizations.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        if (organization is null)
        {
            _logger.LogInformation(
                "No organization exists yet; skipping software-action script seeding.");
            return;
        }

        // SavedScript.CreatorId is non-nullable; pick a server-admin in the
        // default org if possible, else any user in the org. Without a user
        // we cannot satisfy the FK — defer.
        var creator =
            await db.Users
                .AsNoTracking()
                .Where(x => x.OrganizationID == organization.ID && x.IsServerAdmin)
                .FirstOrDefaultAsync(cancellationToken)
            ?? await db.Users
                .AsNoTracking()
                .Where(x => x.OrganizationID == organization.ID)
                .FirstOrDefaultAsync(cancellationToken)
            ?? await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

        if (creator is null)
        {
            _logger.LogInformation(
                "No user exists yet; skipping software-action script seeding.");
            return;
        }

        var seedRows = BuildSeedRows(organization.ID, creator.Id);
        var existingIds = await db.SavedScripts
            .AsNoTracking()
            .Where(x => seedRows.Select(r => r.Id).Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var toInsert = seedRows.Where(x => !existingIds.Contains(x.Id)).ToList();
        if (toInsert.Count == 0)
        {
            _logger.LogDebug("All software-action scripts already exist; nothing to seed.");
            return;
        }

        db.SavedScripts.AddRange(toInsert);
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Seeded {count} software-action saved scripts.",
            toInsert.Count);
    }

    private static List<SavedScript> BuildSeedRows(string organizationId, string creatorId)
    {
        // Each Content uses a {0} placeholder that the SavedScripts API
        // substitutes with the package id at fetch time. Literal `{` and
        // `}` characters in PowerShell here-strings must be doubled to
        // satisfy string.Format.
        const string msiUninstallContent =
            "$id='{0}'; " +
            "$key=Get-ChildItem 'HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall',"
            + "'HKLM:\\Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall' "
            + "-ErrorAction SilentlyContinue | "
            + "Where-Object {{ $_.PSChildName -eq $id -or (Get-ItemProperty $_.PSPath).DisplayName -eq $id }} | "
            + "Select-Object -First 1; "
            + "if (-not $key) {{ Write-Error \"No uninstaller registered for $id.\"; exit 1 }}; "
            + "$u=(Get-ItemProperty $key.PSPath).UninstallString; "
            + "if ($u -match '^MsiExec') {{ $u = $u + ' /qn /norestart' }}; "
            + "Write-Host \"Running: $u\"; "
            + "cmd /c \"$u\"";

        return new List<SavedScript>
        {
            Make(SoftwareActionScriptIds.WingetUninstall,
                "Software action: winget uninstall",
                ScriptingShell.WinPS,
                "winget uninstall --id {0} --silent --accept-source-agreements"),
            Make(SoftwareActionScriptIds.WingetInstall,
                "Software action: winget install",
                ScriptingShell.WinPS,
                "winget install --id {0} --silent --accept-source-agreements --accept-package-agreements"),
            Make(SoftwareActionScriptIds.ChocoUninstall,
                "Software action: choco uninstall",
                ScriptingShell.WinPS,
                "choco uninstall {0} -y --no-progress"),
            Make(SoftwareActionScriptIds.ChocoInstall,
                "Software action: choco install",
                ScriptingShell.WinPS,
                "choco install {0} -y --no-progress"),
            Make(SoftwareActionScriptIds.AptUninstall,
                "Software action: apt uninstall",
                ScriptingShell.Bash,
                "sudo -n DEBIAN_FRONTEND=noninteractive apt-get remove -y {0}"),
            Make(SoftwareActionScriptIds.AptInstall,
                "Software action: apt install",
                ScriptingShell.Bash,
                "sudo -n DEBIAN_FRONTEND=noninteractive apt-get install -y {0}"),
            Make(SoftwareActionScriptIds.BrewUninstall,
                "Software action: brew uninstall",
                ScriptingShell.Bash,
                "brew uninstall --force {0}"),
            Make(SoftwareActionScriptIds.BrewInstall,
                "Software action: brew install",
                ScriptingShell.Bash,
                "brew install {0}"),
            Make(SoftwareActionScriptIds.MsiUninstall,
                "Software action: msi uninstall",
                ScriptingShell.WinPS,
                msiUninstallContent),
        };

        SavedScript Make(Guid id, string name, ScriptingShell shell, string content) => new()
        {
            Id = id,
            Name = name,
            Content = content,
            Shell = shell,
            IsPublic = true,
            IsQuickScript = false,
            CreatorId = creatorId,
            OrganizationID = organizationId,
            FolderPath = "BorderLink/SoftwareActions",
        };
    }
}
