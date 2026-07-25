Unicode true
RequestExecutionLevel admin
Name "Ligase Host"
InstallDir "$PROGRAMFILES64\Ligase Host"
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
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Var SetupMutex
Var DataRoot

Function .onInit
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Global\Ligase.Host.Setup.v1") p .r0 ?e'
  Pop $SetupMutex
  Pop $1
  ${If} $1 = 183
    MessageBox MB_OK|MB_ICONSTOP "Another Ligase install, repair, or uninstall is already running."
    Abort
  ${EndIf}
  ${GetParameters} $0
  ${GetOptions} $0 "/DataRoot=" $DataRoot
FunctionEnd

Function un.onInit
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
  SetOutPath "$INSTDIR"
  File /r "${StageDir}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Deployment\Manage-LigaseInstallation.ps1" -Action Install -InstallDirectory "$INSTDIR" -DataRoot "$DataRoot" -ConfigureFirewall'
  Pop $0
  Pop $1
  ${If} $0 != 0
    DetailPrint "Ligase integration failed with machine outcome."
    DetailPrint "Existing bootstrap, user data, and unknown files were not removed."
    Abort
  ${EndIf}
  CreateDirectory "$SMPROGRAMS\Ligase Host"
  CreateShortcut "$SMPROGRAMS\Ligase Host\Ligase Host.lnk" "$INSTDIR\Desktop\Ligase.Host.Desktop.exe"
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
  RMDir /r "$INSTDIR"
SectionEnd
