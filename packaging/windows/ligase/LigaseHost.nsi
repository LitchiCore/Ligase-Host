Unicode true
RequestExecutionLevel admin
Name "Ligase Host"
InstallDir "$PROGRAMFILES64\Ligase Host"
InstallDirRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "InstallLocation"
OutFile "${OutputFile}"
SetCompressor /SOLID lzma
ShowInstDetails show
ShowUninstDetails show
!ifdef SignerTool
  !finalize '"${SignerTool}" "%1"'
  !uninstfinalize '"${SignerTool}" "%1"'
!endif

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "nsDialogs.nsh"
!include "StrFunc.nsh"
${StrTrimNewLines}
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
Page custom DataRootPageCreate DataRootPageLeave
!insertmacro MUI_PAGE_COMPONENTS
Page custom InstallSummaryPageCreate InstallSummaryPageLeave
!insertmacro MUI_PAGE_INSTFILES
Page custom InstallResultPageCreate
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Var SetupMutex
Var DataRoot
Var DataRootDialog
Var DataRootText
Var DataRootAdvanced
Var DataRootMode
Var DataRootSelectionMode
Var InstallParameters
Var OriginalInstallParameters
Var IntegrationResult
Var ProgramDataRoot

!include "InstallDirectoryValidation.nsh"

Function .onInit
  SetRegView 64
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Global\Ligase.Host.Setup.v1") p .r0 ?e'
  Pop $SetupMutex
  Pop $1
  ${If} $1 = 183
    MessageBox MB_OK|MB_ICONSTOP "Another Ligase install, repair, or uninstall is already running."
    Abort
  ${EndIf}
  SetShellVarContext all
  StrCpy $ProgramDataRoot "$APPDATA"
  SetShellVarContext current
  ; Preserve the exact native command line for duplicate/malformed validation.
  System::Call 'kernel32::GetCommandLineW() w .r0'
  StrCpy $InstallParameters $0
  StrCpy $OriginalInstallParameters $0
  ${GetParameters} $6
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /oname=Resolve-LigaseInstallDirectory.ps1 "${StageDir}\Deployment\Resolve-LigaseInstallDirectory.ps1"
  ; One native argv parser owns both path options and upgrade fallback.
  ; The result only pre-fills UI state; the required section validates the
  ; original argv and final UI values again before any product write.
  Call ResolveInstallerArguments
  StrCpy $DataRootSelectionMode $DataRootMode
FunctionEnd

Function DataRootPageCreate
  nsDialogs::Create 1018
  Pop $DataRootDialog
  ${If} $DataRootDialog == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 28u "Host data contains identity, paired devices, library, and settings. Windows machine application data is used by default."
  Pop $0
  ${NSD_CreateCheckbox} 0 34u 100% 12u "Advanced: use a custom data directory"
  Pop $DataRootAdvanced
  ${NSD_CreateText} 0 52u 100% 13u "$DataRoot"
  Pop $DataRootText
  ${If} $DataRootSelectionMode == "explicit"
    ${NSD_Check} $DataRootAdvanced
    EnableWindow $DataRootText 1
  ${Else}
    EnableWindow $DataRootText 0
  ${EndIf}
  ${NSD_OnClick} $DataRootAdvanced DataRootAdvancedChanged
  nsDialogs::Show
FunctionEnd

Function DataRootAdvancedChanged
  ${NSD_GetState} $DataRootAdvanced $0
  ${If} $0 == ${BST_CHECKED}
    EnableWindow $DataRootText 1
  ${Else}
    EnableWindow $DataRootText 0
  ${EndIf}
FunctionEnd

Function DataRootPageLeave
  ${NSD_GetState} $DataRootAdvanced $0
  ${If} $0 == ${BST_CHECKED}
    ${NSD_GetText} $DataRootText $DataRoot
    StrCpy $DataRootSelectionMode "explicit"
  ${Else}
    ${If} $DataRootSelectionMode == "explicit"
      StrCpy $DataRootSelectionMode "freshDefault"
    ${EndIf}
  ${EndIf}
  ${If} $DataRoot == ""
    MessageBox MB_OK|MB_ICONSTOP "The data directory could not be resolved. Setup will not silently use another location."
    Abort
  ${EndIf}
  StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\"'
  Call ResolveInstallerArguments
  StrCpy $DataRootMode $DataRootSelectionMode
  StrCpy $InstallParameters $OriginalInstallParameters
FunctionEnd

Function InstallSummaryPageCreate
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 18u "Confirm installation"
  Pop $1
  ${NSD_CreateLabel} 0 24u 100% 72u "Program: $INSTDIR$\r$\nData: $DataRoot$\r$\nDesktop shortcut: selected on Components$\r$\nFirewall: Ligase-owned Private + LocalSubnet exact rules$\r$\nVirtual display: installed only when explicitly selected"
  Pop $1
  ${NSD_CreateLabel} 0 104u 100% 28u "Go Back to change a selection. Completion is shown only after integration readback succeeds."
  Pop $1
  nsDialogs::Show
FunctionEnd

Function InstallSummaryPageLeave
  ${If} $DataRoot == ""
    MessageBox MB_OK|MB_ICONSTOP "The data directory has not been confirmed."
    Abort
  ${EndIf}
FunctionEnd

Function InstallResultPageCreate
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 18u "Installation result"
  Pop $1
  ${NSD_CreateLabel} 0 24u 100% 84u "$IntegrationResult$\r$\nProgram: $INSTDIR$\r$\nData: $DataRoot$\r$\nFirewall: Ligase-owned exact rules verified"
  Pop $1
  nsDialogs::Show
FunctionEnd

Function un.onInit
  SetRegView 64
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Global\Ligase.Host.Setup.v1") p .r0 ?e'
  Pop $SetupMutex
  Pop $1
  ${If} $1 = 183
    MessageBox MB_OK|MB_ICONSTOP "Another Ligase install, repair, or uninstall is already running."
    Abort
  ${EndIf}
FunctionEnd

Section "Ligase Host (required)" SEC_MAIN
  SectionIn RO
  ; Validate the original argv first so duplicates and malformed explicit
  ; options cannot be corrected or hidden by the directory page.
  StrCpy $InstallParameters $OriginalInstallParameters
  StrCpy $4 $INSTDIR
  Call ResolveInstallerArguments
  ${If} $5 != "true"
    StrCpy $INSTDIR $4
  ${EndIf}
  ; Then validate the final directory-page value. Both validations happen
  ; before SetOutPath or File can write to the selected installation root.
  ${If} $DataRoot == ""
    MessageBox MB_OK|MB_ICONSTOP "The data directory is empty. No installation data was written."
    SetErrorLevel 10
    Quit
  ${EndIf}
  StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\"'
  Call ResolveInstallerArguments
  SetOutPath "$INSTDIR"
  File /r "${StageDir}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Deployment\Manage-LigaseInstallation.ps1" -Action Install -InstallDirectory "$INSTDIR" -DataRoot "$DataRoot" -ConfigureFirewall'
  Pop $0
  Pop $1
  ${If} $0 != 0
    DetailPrint "Ligase integration failed with machine outcome."
    DetailPrint "Existing bootstrap, user data, and unknown files were not removed."
    MessageBox MB_OK|MB_ICONSTOP "Ligase integration validation failed. Data binding or firewall readback did not pass. Installation is incomplete."
    SetErrorLevel 10
    Quit
  ${EndIf}
  StrCpy $IntegrationResult "Program files, data binding, and firewall rules were verified."
  ; The exact owned desktop shortcut is selection-controlled on every install
  ; and upgrade. Removing it here makes an unchecked upgrade deterministic.
  Delete "$DESKTOP\Ligase Host.lnk"
  CreateDirectory "$SMPROGRAMS\Ligase Host"
  CreateShortcut "$SMPROGRAMS\Ligase Host\Ligase Host.lnk" "$INSTDIR\Ligase Host.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "DisplayName" "Ligase Host"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "DisplayIcon" "$INSTDIR\Ligase Host.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "Publisher" "Ligase"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "NoRepair" 1
SectionEnd

Section "Create desktop shortcut (optional)" SEC_DESKTOP_SHORTCUT
  CreateShortcut "$DESKTOP\Ligase Host.lnk" "$INSTDIR\Ligase Host.exe"
SectionEnd

Section /o "Ligase Virtual Display (optional)" SEC_VDISPLAY
  MessageBox MB_YESNO|MB_ICONEXCLAMATION \
    "SudoVDA currently uses a self-signed publisher certificate. Continuing adds that publisher to local trust stores so Windows can install the kernel driver. Physical desktop streaming does not require it. Continue?" \
    /SD IDNO IDNO skipVirtualDisplay
  nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Deployment\Manage-LigaseInstallation.ps1" -Action InstallVirtualDisplay -InstallDirectory "$INSTDIR"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    DetailPrint "Virtual display was not installed; physical desktop streaming remains available."
  ${Else}
  ${EndIf}
  skipVirtualDisplay:
SectionEnd

Section "Uninstall"
  MessageBox MB_YESNO|MB_ICONQUESTION \
    "Move Ligase personal data out of the active directory into recoverable quarantine? Choosing No preserves it in place." \
    /SD IDNO IDNO preserveData
  StrCpy $3 "Quarantine"
  Goto dataChoiceDone
  preserveData:
  StrCpy $3 "Preserve"
  dataChoiceDone:
  nsExec::ExecToLog 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Deployment\Manage-LigaseInstallation.ps1" -Action Uninstall -InstallDirectory "$INSTDIR" -DataDisposition $3 -ConfigureFirewall'
  IfFileExists "$INSTDIR\Deployment\Drivers\sudovda\.ligase-driver-ownership.json" 0 noDriver
  nsExec::ExecToLog 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Deployment\Manage-LigaseInstallation.ps1" -Action UninstallVirtualDisplay -InstallDirectory "$INSTDIR"'
  noDriver:
  Delete "$SMPROGRAMS\Ligase Host\Ligase Host.lnk"
  RMDir "$SMPROGRAMS\Ligase Host"
  Delete "$DESKTOP\Ligase Host.lnk"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host"
  RMDir /r "$INSTDIR"
SectionEnd
