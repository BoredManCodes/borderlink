# BorderLink Agent — Windows NSIS installer

Builds a single `BorderLink-Agent-Setup-<arch>.exe` per architecture that
installs the unattended agent service. Functionally equivalent to
`Server/wwwroot/Content/Install-BorderLink.ps1` but ships as a real Windows
installer with an Add/Remove Programs entry, an uninstaller, and proper
unattended/silent flags suitable for GPO, Intune, and PSA deployment.

## How a customer gets it

1. They sign in to BorderLink.
2. From **Downloads** they click **Windows x64 Installer (.exe)**.
3. The server streams the prebuilt installer with the org id and server URL
   **encoded into the filename** (`__<base64url-json>.exe` suffix).
4. They double-click the .exe. The installer reads its own filename, decodes
   the suffix, and skips the configuration page entirely. No prompts.

If the file is renamed before running, the installer falls back first to CLI
args (`/SERVERURL=…` etc.), then to a UI prompt — so all three paths still
work.

## Configuration sources (priority order)

1. **CLI args** on the installer — `/SERVERURL=`, `/ORGID=`, `/ALIAS=`,
   `/GROUP=`. Always win.
2. **Filename suffix** — `BorderLink-Agent-Setup-<arch>__<token>.exe` where
   `<token>` is base64url-encoded JSON of `{ "u":"server", "o":"org", "a":"alias", "g":"group" }`.
3. **UI prompt** — only if 1+2 didn't fill server URL and org id, and the run
   isn't silent.

## Prerequisites

- .NET 8 SDK (for `dotnet publish`)
- [NSIS 3.x](https://nsis.sourceforge.io/Download) — `makensis.exe` on PATH or
  installed to `C:\Program Files (x86)\NSIS\`
- Windows PowerShell 5.1 (or PowerShell 7) for the build script

## Build

From the repo root:

```powershell
powershell -f Agent\Installer\Windows\Build-Installer.ps1 -Arch both
```

Outputs land in `Agent\Installer\Windows\out\` and are also copied to
`Server\wwwroot\Content\` so the hosted endpoint picks them up without a
server rebuild. Pass `-NoCopyToContent` to skip the copy.

If you've already published the agent (e.g. via `Utilities\Publish.ps1`), pass
`-SkipPublish` to skip the rebuild.

To sign the installer pass `-CertificatePath` and `-CertificatePassword`; the
script reuses `Utilities\signtool.exe`.

## Hosted endpoint

After a build, `Server/wwwroot/Content/BorderLink-Agent-Setup-<arch>.exe`
exists on disk. The server exposes:

| Route | Auth | Notes |
|---|---|---|
| `GET /api/clientdownloads/agent/{platformId}` | Cookie auth (Identity) | Uses requesting user's organization. |
| `GET /api/clientdownloads/agent/{platformId}/{organizationId}` | Anonymous | For deploy-script style links. |

`{platformId}` is `WindowsAgentInstaller-x64` or `WindowsAgentInstaller-x86`.

Optional query parameters: `?alias=Reception%20PC&group=Reception` — these are
embedded in the filename suffix and pre-fill the corresponding fields at
install time, so the device shows up in the right group with the right name on
first boot.

The .exe bytes on disk are unchanged between requests; only the
`Content-Disposition` filename varies. That keeps the endpoint cheap (no
per-request rebuild) and means the file is also safe to serve from a CDN.

## Install (silent / unattended)

If the filename was preserved, just run it:

```powershell
BorderLink-Agent-Setup-x64__eyJ1IjoiaHR0cHM...fQ.exe /S
```

Otherwise, supply args explicitly:

```powershell
BorderLink-Agent-Setup-x64.exe /S `
    /SERVERURL=https://remote.example.com `
    /ORGID=00000000-0000-0000-0000-000000000000 `
    /ALIAS="Reception PC" `
    /GROUP="Reception"
```

Flags:

| Flag                  | Meaning                                                        |
|-----------------------|----------------------------------------------------------------|
| `/S`                  | Silent install (no UI). Server URL + Org ID must be resolvable.|
| `/SERVERURL=<url>`    | Server base URL. Overrides filename + UI.                      |
| `/ORGID=<guid>`       | Organization ID. Overrides filename + UI.                      |
| `/ALIAS="<name>"`     | Optional device alias (registers via `POST /api/devices`).     |
| `/GROUP="<name>"`     | Optional device group name.                                    |
| `/D=<path>`           | Override install directory. **Must be the LAST argument.**     |
| `/KEEPCONFIG`         | On uninstall, preserve `ConnectionInfo.json`.                  |

Exit codes: `0` on success; `2` when a silent install can't resolve server URL
or org id from any source. Standard NSIS errors otherwise.

## What it does

1. Stops and removes any existing `BorderLink_Service`, kills agent + desktop
   processes if running.
2. Wipes the install dir contents but **preserves `ConnectionInfo.json`** so
   the device's `DeviceID` survives a reinstall.
3. Extracts the published agent payload (from
   `Agent\bin\publish\win-<arch>`).
4. Writes `ConnectionInfo.json` (new `DeviceID` on first install, updated
   `Host` / `OrganizationID` on every install).
5. Adds an inbound firewall rule named `BorderLink Desktop Unattended` for
   `BorderLink_Desktop.exe`.
6. Creates the `BorderLink_Service` Windows service (auto-start, restart on
   failure: 5/5/5 seconds).
7. If `/ALIAS` or `/GROUP` was supplied, POSTs `DeviceSetupOptions` to
   `<server>/api/devices` so the device shows up with the right metadata.
8. Starts the service.
9. Registers an Add/Remove Programs entry pointing at the bundled uninstaller.

## Uninstall

Either run from Settings → Apps → BorderLink Agent → Uninstall, or silently:

```powershell
"C:\Program Files\BorderLink\Uninstall.exe" /S
```

Add `/KEEPCONFIG` to keep `ConnectionInfo.json` (handy if you're tearing down
just to reinstall).
