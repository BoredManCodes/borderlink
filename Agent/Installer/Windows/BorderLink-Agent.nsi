; BorderLink Agent installer
; Build with: makensis /DARCH=x64 /DAGENT_DIR=path\to\publish\win-x64 BorderLink-Agent.nsi
;
; Configuration sources, in priority order:
;   1. CLI args   (/SERVERURL=, /ORGID=, /ALIAS=, /GROUP=)
;   2. Filename   BorderLink-Agent-Setup-<arch>__<base64url-json>.exe
;                 where the JSON has the form { "u":"server","o":"org","a":"alias","g":"group" }
;   3. UI prompt  (only when running interactively and 1+2 didn't fill ServerUrl/OrgId)
;
; The hosted /api/clientdownloads/agent/... endpoint sets the filename suffix
; so a logged-in user can download a "just works" installer with no questions
; asked.
;
; CLI args supported by the produced installer:
;   /S                  Silent install (no UI)
;   /SERVERURL=URL      Server base URL (overrides filename + UI)
;   /ORGID=GUID         Organization ID (overrides filename + UI)
;   /ALIAS="Name"       Optional device alias (registers via /api/devices)
;   /GROUP="Name"       Optional device group name
;   /KEEPCONFIG         On uninstall, keep ConnectionInfo.json
;   /D=path             Install directory (NSIS built-in; must be LAST argument)

Unicode true
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WinVer.nsh"
!include "x64.nsh"
!include "nsDialogs.nsh"

;-----------------------------------------------------------------------------
; Build-time defines (override on the makensis command line)
;-----------------------------------------------------------------------------
!ifndef ARCH
  !define ARCH "x64"
!endif

!ifndef AGENT_DIR
  !define AGENT_DIR "..\..\bin\publish\win-${ARCH}"
!endif

!ifndef VERSION
  !define VERSION "1.0.0.0"
!endif

!ifndef PRODUCT_NAME
  !define PRODUCT_NAME "BorderLink Agent"
!endif

!ifndef PUBLISHER
  !define PUBLISHER "Border Tech Solutions"
!endif

!ifndef DEFAULT_SERVER_URL
  !define DEFAULT_SERVER_URL ""
!endif

!ifndef OUT_FILE
  !define OUT_FILE "BorderLink-Agent-Setup-${ARCH}.exe"
!endif

!define SERVICE_NAME      "BorderLink_Service"
!define SERVICE_DISPLAY   "BorderLink Service"
!define SERVICE_DESC      "Background service that maintains a connection to the BorderLink server. Used for remote support and maintenance by this computer's administrators."
!define AGENT_EXE         "BorderLink_Agent.exe"
!define DESKTOP_EXE       "Desktop\BorderLink_Desktop.exe"
!define FIREWALL_RULE     "BorderLink Desktop Unattended"
!define UNINST_KEY        "Software\Microsoft\Windows\CurrentVersion\Uninstall\BorderLink"

Name             "${PRODUCT_NAME}"
OutFile          "${OUT_FILE}"
InstallDir       "$PROGRAMFILES\BorderLink"
InstallDirRegKey HKLM "Software\BorderLink" "InstallDir"
RequestExecutionLevel admin
ShowInstDetails  show
ShowUnInstDetails show

VIProductVersion "${VERSION}"
VIAddVersionKey  "ProductName"     "${PRODUCT_NAME}"
VIAddVersionKey  "CompanyName"     "${PUBLISHER}"
VIAddVersionKey  "FileDescription" "${PRODUCT_NAME} Installer"
VIAddVersionKey  "FileVersion"     "${VERSION}"
VIAddVersionKey  "ProductVersion"  "${VERSION}"
VIAddVersionKey  "LegalCopyright"  "(c) ${PUBLISHER}"

;-----------------------------------------------------------------------------
; Variables (filled from CLI args or the configuration page)
;-----------------------------------------------------------------------------
Var ServerUrl
Var OrgId
Var Alias
Var GroupName
Var KeepConfig

; Custom page controls
Var Dlg
Var LblServer
Var TxtServer
Var LblOrg
Var TxtOrg
Var LblAlias
Var TxtAlias
Var LblGroup
Var TxtGroup
Var LblHint

;-----------------------------------------------------------------------------
; UI
;-----------------------------------------------------------------------------
!define MUI_ABORTWARNING

!insertmacro MUI_PAGE_WELCOME
Page custom ConfigPage ConfigPageLeave
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

;-----------------------------------------------------------------------------
; Read CLI args once at startup
;-----------------------------------------------------------------------------
Function .onInit
  ${IfNot} ${AtLeastWin7}
    MessageBox MB_ICONSTOP "Windows 7 or later is required."
    Abort
  ${EndIf}

  StrCpy $ServerUrl "${DEFAULT_SERVER_URL}"
  StrCpy $OrgId ""
  StrCpy $Alias ""
  StrCpy $GroupName ""
  StrCpy $KeepConfig "0"

  ; --- 1. Read CLI args ---
  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/SERVERURL=" $R1
  ${IfNot} ${Errors}
    StrCpy $ServerUrl $R1
  ${EndIf}
  ClearErrors
  ${GetOptions} $R0 "/ORGID=" $R1
  ${IfNot} ${Errors}
    StrCpy $OrgId $R1
  ${EndIf}
  ClearErrors
  ${GetOptions} $R0 "/ALIAS=" $R1
  ${IfNot} ${Errors}
    StrCpy $Alias $R1
  ${EndIf}
  ClearErrors
  ${GetOptions} $R0 "/GROUP=" $R1
  ${IfNot} ${Errors}
    StrCpy $GroupName $R1
  ${EndIf}
  ClearErrors
  ${GetOptions} $R0 "/KEEPCONFIG" $R1
  ${IfNot} ${Errors}
    StrCpy $KeepConfig "1"
  ${EndIf}
  ClearErrors

  ; --- 2. Fill missing values from the installer filename ---
  ${If} $ServerUrl == ""
  ${OrIf} $OrgId == ""
    Call ParseFilenameConfig
  ${EndIf}

  ${If} ${RunningX64}
    SetRegView 64
  ${EndIf}

  ; In silent mode the server URL and org id must be provided up front.
  ${If} ${Silent}
    ${If} $ServerUrl == ""
    ${OrIf} $OrgId == ""
      SetErrorLevel 2
      Abort
    ${EndIf}
  ${EndIf}
FunctionEnd

;-----------------------------------------------------------------------------
; Custom page: prompt for ServerUrl + OrganizationId (skip if both supplied)
;-----------------------------------------------------------------------------
Function ConfigPage
  ${If} $ServerUrl != ""
  ${AndIf} $OrgId != ""
    Abort   ; already supplied via CLI; skip the page
  ${EndIf}

  !insertmacro MUI_HEADER_TEXT "Server Configuration" "Enter the BorderLink server connection details for this device."
  nsDialogs::Create 1018
  Pop $Dlg
  ${If} $Dlg == error
    Abort
  ${EndIf}

  ${NSD_CreateLabel} 0 0 100% 12u "Server URL (e.g. https://remote.borderlink.example):"
  Pop $LblServer
  ${NSD_CreateText} 0 14u 100% 12u "$ServerUrl"
  Pop $TxtServer

  ${NSD_CreateLabel} 0 32u 100% 12u "Organization ID:"
  Pop $LblOrg
  ${NSD_CreateText} 0 46u 100% 12u "$OrgId"
  Pop $TxtOrg

  ${NSD_CreateLabel} 0 64u 100% 12u "Device alias (optional):"
  Pop $LblAlias
  ${NSD_CreateText} 0 78u 100% 12u "$Alias"
  Pop $TxtAlias

  ${NSD_CreateLabel} 0 96u 100% 12u "Device group (optional):"
  Pop $LblGroup
  ${NSD_CreateText} 0 110u 100% 12u "$GroupName"
  Pop $TxtGroup

  ${NSD_CreateLabel} 0 132u 100% 24u "These values come from your BorderLink server's organization settings. They can be changed later by editing $\"$INSTDIR\ConnectionInfo.json$\" and restarting the service."
  Pop $LblHint

  nsDialogs::Show
FunctionEnd

Function ConfigPageLeave
  ${NSD_GetText} $TxtServer $ServerUrl
  ${NSD_GetText} $TxtOrg    $OrgId
  ${NSD_GetText} $TxtAlias  $Alias
  ${NSD_GetText} $TxtGroup  $GroupName

  ${If} $ServerUrl == ""
  ${OrIf} $OrgId == ""
    MessageBox MB_ICONEXCLAMATION "Server URL and Organization ID are required."
    Abort
  ${EndIf}
FunctionEnd

;-----------------------------------------------------------------------------
; Install
;-----------------------------------------------------------------------------
Section "BorderLink Agent" SecAgent
  SectionIn RO
  SetOutPath "$INSTDIR"

  DetailPrint "Stopping any existing BorderLink service..."
  Call StopAgent

  DetailPrint "Copying agent files (${ARCH})..."
  ; Wipe existing agent files but keep ConnectionInfo.json so DeviceID survives reinstalls.
  Push "$INSTDIR"
  Call CleanInstallDirKeepConfig

  ; Bundled agent payload — the build script publishes the agent to AGENT_DIR
  ; (default ..\..\bin\publish\win-<ARCH>) before invoking makensis.
  File /r "${AGENT_DIR}\*.*"

  DetailPrint "Writing ConnectionInfo.json..."
  Call WriteConnectionInfo

  DetailPrint "Configuring firewall..."
  nsExec::ExecToLog 'netsh advfirewall firewall delete rule name="${FIREWALL_RULE}"'
  Pop $0
  nsExec::ExecToLog 'netsh advfirewall firewall add rule name="${FIREWALL_RULE}" dir=in action=allow program="$INSTDIR\${DESKTOP_EXE}" enable=yes profile=any'
  Pop $0

  DetailPrint "Registering Windows service..."
  nsExec::ExecToLog 'sc.exe create "${SERVICE_NAME}" binPath= "\"$INSTDIR\${AGENT_EXE}\"" start= auto DisplayName= "${SERVICE_DISPLAY}"'
  Pop $0
  nsExec::ExecToLog 'sc.exe description "${SERVICE_NAME}" "${SERVICE_DESC}"'
  Pop $0
  nsExec::ExecToLog 'sc.exe failure "${SERVICE_NAME}" reset= 5 actions= restart/5000/restart/5000/restart/5000'
  Pop $0

  ${If} $Alias != ""
  ${OrIf} $GroupName != ""
    DetailPrint "Registering device alias/group with the server..."
    Call RegisterDeviceAliasGroup
  ${EndIf}

  DetailPrint "Starting service..."
  nsExec::ExecToLog 'sc.exe start "${SERVICE_NAME}"'
  Pop $0

  ; Add/Remove Programs entry
  WriteRegStr   HKLM "${UNINST_KEY}" "DisplayName"     "${PRODUCT_NAME}"
  WriteRegStr   HKLM "${UNINST_KEY}" "DisplayVersion"  "${VERSION}"
  WriteRegStr   HKLM "${UNINST_KEY}" "Publisher"       "${PUBLISHER}"
  WriteRegStr   HKLM "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr   HKLM "${UNINST_KEY}" "DisplayIcon"     "$INSTDIR\${AGENT_EXE}"
  WriteRegStr   HKLM "${UNINST_KEY}" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
  WriteRegStr   HKLM "${UNINST_KEY}" "QuietUninstallString" "$\"$INSTDIR\Uninstall.exe$\" /S"
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoRepair" 1

  WriteRegStr HKLM "Software\BorderLink" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "Software\BorderLink" "Version"    "${VERSION}"

  WriteUninstaller "$INSTDIR\Uninstall.exe"
SectionEnd

;-----------------------------------------------------------------------------
; Helpers
;-----------------------------------------------------------------------------

; Reads "$EXEFILE" and, if it has the suffix __<base64url-json>.exe, decodes
; the JSON and pre-fills $ServerUrl / $OrgId / $Alias / $GroupName for any
; field that wasn't already supplied via CLI. Silently no-ops on a plain
; filename or a malformed token — install will then fall back to the UI / CLI.
Function ParseFilenameConfig
  StrCpy $R1 "$PLUGINSDIR\decode-config.ps1"
  StrCpy $R2 "$PLUGINSDIR\config.ini"

  FileOpen  $R3 "$R1" w
  FileWrite $R3 "param([string]$$Filename,[string]$$OutPath)$\r$\n"
  FileWrite $R3 "$$ErrorActionPreference='SilentlyContinue'$\r$\n"
  FileWrite $R3 "$$idx = $$Filename.LastIndexOf('__')$\r$\n"
  FileWrite $R3 "if ($$idx -lt 0) { exit 0 }$\r$\n"
  FileWrite $R3 "$$end = $$Filename.LastIndexOf('.exe')$\r$\n"
  FileWrite $R3 "if ($$end -lt $$idx) { $$end = $$Filename.Length }$\r$\n"
  FileWrite $R3 "$$tok = $$Filename.Substring($$idx + 2, $$end - $$idx - 2)$\r$\n"
  FileWrite $R3 "if ([string]::IsNullOrWhiteSpace($$tok)) { exit 0 }$\r$\n"
  FileWrite $R3 "$$tok = $$tok.Replace('-','+').Replace('_','/')$\r$\n"
  FileWrite $R3 "while (($$tok.Length % 4) -ne 0) { $$tok += '=' }$\r$\n"
  FileWrite $R3 "try { $$json = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($$tok)) } catch { exit 0 }$\r$\n"
  FileWrite $R3 "try { $$obj = $$json | ConvertFrom-Json } catch { exit 0 }$\r$\n"
  FileWrite $R3 "$$lines = @('[cfg]')$\r$\n"
  FileWrite $R3 "if ($$obj.u) { $$lines += 'ServerUrl=' + $$obj.u }$\r$\n"
  FileWrite $R3 "if ($$obj.o) { $$lines += 'OrgId='     + $$obj.o }$\r$\n"
  FileWrite $R3 "if ($$obj.a) { $$lines += 'Alias='     + $$obj.a }$\r$\n"
  FileWrite $R3 "if ($$obj.g) { $$lines += 'Group='     + $$obj.g }$\r$\n"
  FileWrite $R3 "Set-Content -Path $$OutPath -Value $$lines -Encoding UTF8$\r$\n"
  FileClose $R3

  nsExec::ExecToLog 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$R1" -Filename "$EXEFILE" -OutPath "$R2"'
  Pop $0

  ${IfNot} ${FileExists} "$R2"
    Return
  ${EndIf}

  ${If} $ServerUrl == ""
    ReadINIStr $R4 "$R2" "cfg" "ServerUrl"
    ${IfNot} ${Errors}
      StrCpy $ServerUrl $R4
    ${EndIf}
    ClearErrors
  ${EndIf}

  ${If} $OrgId == ""
    ReadINIStr $R4 "$R2" "cfg" "OrgId"
    ${IfNot} ${Errors}
      StrCpy $OrgId $R4
    ${EndIf}
    ClearErrors
  ${EndIf}

  ${If} $Alias == ""
    ReadINIStr $R4 "$R2" "cfg" "Alias"
    ${IfNot} ${Errors}
      StrCpy $Alias $R4
    ${EndIf}
    ClearErrors
  ${EndIf}

  ${If} $GroupName == ""
    ReadINIStr $R4 "$R2" "cfg" "Group"
    ${IfNot} ${Errors}
      StrCpy $GroupName $R4
    ${EndIf}
    ClearErrors
  ${EndIf}
FunctionEnd

Function StopAgent
  nsExec::ExecToLog 'sc.exe stop "${SERVICE_NAME}"'
  Pop $0
  Sleep 1500
  nsExec::ExecToLog 'sc.exe delete "${SERVICE_NAME}"'
  Pop $0
  nsExec::ExecToLog 'taskkill /F /IM BorderLink_Agent.exe /T'
  Pop $0
  nsExec::ExecToLog 'taskkill /F /IM BorderLink_Desktop.exe /T'
  Pop $0
FunctionEnd

; Wipes $INSTDIR contents but preserves ConnectionInfo.json so DeviceID survives reinstall.
Function CleanInstallDirKeepConfig
  Pop $R0   ; install dir
  CreateDirectory "$R0"
  ${If} ${FileExists} "$R0\ConnectionInfo.json"
    CopyFiles /SILENT "$R0\ConnectionInfo.json" "$PLUGINSDIR\ConnectionInfo.json"
  ${EndIf}
  RMDir /r "$R0\Desktop"
  RMDir /r "$R0\runtimes"
  RMDir /r "$R0\cs"
  RMDir /r "$R0\de"
  RMDir /r "$R0\es"
  RMDir /r "$R0\fr"
  RMDir /r "$R0\it"
  RMDir /r "$R0\ja"
  RMDir /r "$R0\ko"
  RMDir /r "$R0\pl"
  RMDir /r "$R0\pt-BR"
  RMDir /r "$R0\ru"
  RMDir /r "$R0\tr"
  RMDir /r "$R0\zh-Hans"
  RMDir /r "$R0\zh-Hant"
  Delete "$R0\*.exe"
  Delete "$R0\*.dll"
  Delete "$R0\*.json"
  Delete "$R0\*.pdb"
  Delete "$R0\*.xml"
  Delete "$R0\etag.txt"
  ${If} ${FileExists} "$PLUGINSDIR\ConnectionInfo.json"
    CopyFiles /SILENT "$PLUGINSDIR\ConnectionInfo.json" "$R0\ConnectionInfo.json"
  ${EndIf}
FunctionEnd

; Reads (or generates) ConnectionInfo.json next to the agent. We rely on
; PowerShell to keep parity with Install-BorderLink.ps1 — same JSON schema,
; same DeviceID-preservation behaviour on reinstall.
Function WriteConnectionInfo
  StrCpy $R0 "$INSTDIR\ConnectionInfo.json"
  StrCpy $R1 "$PLUGINSDIR\write-connection-info.ps1"

  FileOpen  $R2 "$R1" w
  FileWrite $R2 "param([string]$$Path,[string]$$Host,[string]$$Org)$\r$\n"
  FileWrite $R2 "$$ErrorActionPreference='Stop'$\r$\n"
  FileWrite $R2 "$$existing = $$null$\r$\n"
  FileWrite $R2 "if (Test-Path $$Path) {$\r$\n"
  FileWrite $R2 "  try { $$existing = Get-Content -Raw $$Path | ConvertFrom-Json } catch { $$existing = $$null }$\r$\n"
  FileWrite $R2 "}$\r$\n"
  FileWrite $R2 "$$deviceId = if ($$existing -and $$existing.DeviceID) { $$existing.DeviceID } else { [guid]::NewGuid().ToString() }$\r$\n"
  FileWrite $R2 "$$obj = [ordered]@{ DeviceID = $$deviceId; Host = $$Host.TrimEnd('/'); OrganizationID = $$Org; ServerVerificationToken = '' }$\r$\n"
  FileWrite $R2 "$$json = $$obj | ConvertTo-Json$\r$\n"
  FileWrite $R2 "Set-Content -Path $$Path -Value $$json -Encoding UTF8 -Force$\r$\n"
  FileClose $R2

  nsExec::ExecToLog 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$R1" -Path "$R0" -Host "$ServerUrl" -Org "$OrgId"'
  Pop $0
  ${If} $0 != 0
    DetailPrint "WARNING: Failed to write ConnectionInfo.json (exit $0)"
  ${EndIf}
FunctionEnd

; POST to /api/devices to set alias / group at first install.
Function RegisterDeviceAliasGroup
  StrCpy $R1 "$PLUGINSDIR\register-device.ps1"

  FileOpen  $R2 "$R1" w
  FileWrite $R2 "param([string]$$Path,[string]$$Host,[string]$$Org,[string]$$Alias,[string]$$GroupName)$\r$\n"
  FileWrite $R2 "$$ErrorActionPreference='Stop'$\r$\n"
  FileWrite $R2 "[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls13$\r$\n"
  FileWrite $R2 "$$ci = Get-Content -Raw $$Path | ConvertFrom-Json$\r$\n"
  FileWrite $R2 "$$body = @{ DeviceAlias = $$Alias; DeviceGroupName = $$GroupName; OrganizationID = $$Org; DeviceID = $$ci.DeviceID } | ConvertTo-Json$\r$\n"
  FileWrite $R2 "Invoke-RestMethod -Method Post -ContentType 'application/json' -Uri ($$Host.TrimEnd('/') + '/api/devices') -Body $$body | Out-Null$\r$\n"
  FileClose $R2

  nsExec::ExecToLog 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$R1" -Path "$INSTDIR\ConnectionInfo.json" -Host "$ServerUrl" -Org "$OrgId" -Alias "$Alias" -GroupName "$GroupName"'
  Pop $0
  ${If} $0 != 0
    DetailPrint "WARNING: Device alias/group registration failed (exit $0). The device will still appear once it connects."
  ${EndIf}
FunctionEnd

;-----------------------------------------------------------------------------
; Uninstall
;-----------------------------------------------------------------------------
Function un.onInit
  StrCpy $KeepConfig "0"
  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/KEEPCONFIG" $R1
  ${IfNot} ${Errors}
    StrCpy $KeepConfig "1"
  ${EndIf}
  ClearErrors
FunctionEnd

Section "Uninstall"
  DetailPrint "Stopping BorderLink service..."
  nsExec::ExecToLog 'sc.exe stop "${SERVICE_NAME}"'
  Pop $0
  Sleep 1500
  nsExec::ExecToLog 'sc.exe delete "${SERVICE_NAME}"'
  Pop $0
  nsExec::ExecToLog 'taskkill /F /IM BorderLink_Agent.exe /T'
  Pop $0
  nsExec::ExecToLog 'taskkill /F /IM BorderLink_Desktop.exe /T'
  Pop $0

  DetailPrint "Removing firewall rule..."
  nsExec::ExecToLog 'netsh advfirewall firewall delete rule name="${FIREWALL_RULE}"'
  Pop $0

  DetailPrint "Removing files..."
  ${If} $KeepConfig == "1"
  ${AndIf} ${FileExists} "$INSTDIR\ConnectionInfo.json"
    CopyFiles /SILENT "$INSTDIR\ConnectionInfo.json" "$PLUGINSDIR\ConnectionInfo.json"
  ${EndIf}

  RMDir /r "$INSTDIR"

  ${If} $KeepConfig == "1"
  ${AndIf} ${FileExists} "$PLUGINSDIR\ConnectionInfo.json"
    CreateDirectory "$INSTDIR"
    CopyFiles /SILENT "$PLUGINSDIR\ConnectionInfo.json" "$INSTDIR\ConnectionInfo.json"
  ${EndIf}

  DeleteRegKey HKLM "${UNINST_KEY}"
  DeleteRegKey HKLM "Software\BorderLink"
SectionEnd
