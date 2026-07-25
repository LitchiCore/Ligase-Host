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
!include "Sections.nsh"
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
Var DataRootSource
Var DataRootSelectionMode
Var InstallParameters
Var OriginalInstallParameters
Var IntegrationResult
Var ProgramDataRoot
Var DesktopShortcutSummary
Var VirtualDisplaySummary
Var DataRootActionSummary
Var SavedInstallDirectory
Var SavedDataRoot
Var SavedDataRootSource
!define LIGASE_SECTION_DESKTOP_SHORTCUT 1
!define LIGASE_SECTION_VIRTUAL_DISPLAY 2

!include "InstallDirectoryValidation.nsh"

Function .onInit
  SetRegView 64
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Global\Ligase.Host.Setup.v1") p .r0 ?e'
  Pop $SetupMutex
  Pop $1
  ${If} $1 = 183
    MessageBox MB_OK|MB_ICONSTOP "另一个 Ligase 安装、修复或卸载任务正在运行。"
    Abort
  ${EndIf}
  StrCpy $IntegrationResult ""
  StrCpy $DesktopShortcutSummary "否"
  StrCpy $VirtualDisplaySummary "否"
  StrCpy $DataRootActionSummary "新建"
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
  ${If} $DataRootSelectionMode == "migration"
    ${NSD_CreateLabel} 0 0 100% 38u "检测到旧版 Host 数据目录的权限不符合当前安全规范。安装程序将复制并逐项验证数据，再切换到 Windows 标准安全目录；旧目录会原样保留，供失败回滚与后续确认。"
  ${ElseIf} $DataRootSelectionMode == "existing"
    ${NSD_CreateLabel} 0 0 100% 28u "将保留现有 Host 数据目录、身份和配对，不更改其访问权限。"
  ${Else}
    ${NSD_CreateLabel} 0 0 100% 28u "主机数据包含 Host 身份、已配对设备、游戏库和设置。安装程序会在 Windows 公共应用数据区自动创建专属目录，仅当前 Host 运行账户可访问。"
  ${EndIf}
  Pop $0
  ${NSD_CreateCheckbox} 0 42u 100% 12u "高级：使用其他本机目录（将应用相同的受限权限）"
  Pop $DataRootAdvanced
  ${NSD_CreateText} 0 60u 100% 13u "$DataRoot"
  Pop $DataRootText
  ${If} $DataRootSelectionMode == "existing"
    EnableWindow $DataRootAdvanced 0
    EnableWindow $DataRootText 0
  ${ElseIf} $DataRootSelectionMode == "migration"
    EnableWindow $DataRootAdvanced 0
    EnableWindow $DataRootText 0
  ${ElseIf} $DataRootSelectionMode == "explicit"
    ${NSD_Check} $DataRootAdvanced
    EnableWindow $DataRootText 1
  ${Else}
    EnableWindow $DataRootText 0
  ${EndIf}
  ${NSD_OnClick} $DataRootAdvanced DataRootAdvancedChanged
  nsDialogs::Show
FunctionEnd

Function DataRootAdvancedChanged
  ${If} $DataRootSelectionMode == "existing"
    EnableWindow $DataRootAdvanced 0
    EnableWindow $DataRootText 0
    Return
  ${ElseIf} $DataRootSelectionMode == "migration"
    EnableWindow $DataRootAdvanced 0
    EnableWindow $DataRootText 0
    Return
  ${EndIf}
  ${NSD_GetState} $DataRootAdvanced $0
  ${If} $0 == ${BST_CHECKED}
    EnableWindow $DataRootText 1
  ${Else}
    EnableWindow $DataRootText 0
  ${EndIf}
FunctionEnd

Function DataRootPageLeave
  ${If} $DataRootSelectionMode == "existing"
    StrCpy $DataRootMode "existing"
    Return
  ${ElseIf} $DataRootSelectionMode == "migration"
    StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\"'
    Call ResolveInstallerArguments
    ${If} $DataRootMode != "migration"
      MessageBox MB_OK|MB_ICONSTOP "无法确认安全迁移目标。旧数据与现有 Host 身份均未更改。"
      Abort
    ${EndIf}
    StrCpy $InstallParameters $OriginalInstallParameters
    Return
  ${EndIf}
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
    MessageBox MB_OK|MB_ICONSTOP "无法确定数据目录。安装程序不会静默改用其他位置。"
    Abort
  ${EndIf}
  StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\"'
  Call ResolveInstallerArguments
  StrCpy $DataRootMode $DataRootSelectionMode
  StrCpy $InstallParameters $OriginalInstallParameters
FunctionEnd

Function InstallSummaryPageCreate
  SectionGetFlags ${LIGASE_SECTION_DESKTOP_SHORTCUT} $2
  IntOp $2 $2 & ${SF_SELECTED}
  ${If} $2 != 0
    StrCpy $DesktopShortcutSummary "是"
  ${Else}
    StrCpy $DesktopShortcutSummary "否"
  ${EndIf}
  SectionGetFlags ${LIGASE_SECTION_VIRTUAL_DISPLAY} $3
  IntOp $3 $3 & ${SF_SELECTED}
  ${If} $3 != 0
    StrCpy $VirtualDisplaySummary "是"
  ${Else}
    StrCpy $VirtualDisplaySummary "否"
  ${EndIf}
  ${If} $DataRootMode == "migration"
    StrCpy $DataRootActionSummary "迁移到标准安全目录（旧目录原样保留）"
  ${ElseIf} $DataRootMode == "existing"
    StrCpy $DataRootActionSummary "保留现有目录和身份"
  ${Else}
    StrCpy $DataRootActionSummary "新建并应用受限权限"
  ${EndIf}
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 18u "确认安装"
  Pop $1
  ${If} $DataRootMode == "migration"
    ${NSD_CreateLabel} 0 24u 100% 100u "程序目录：$INSTDIR$\r$\n旧数据目录：$DataRootSource$\r$\n标准数据目录：$DataRoot$\r$\n数据动作：$DataRootActionSummary$\r$\n创建桌面快捷方式：$DesktopShortcutSummary$\r$\n安装虚拟显示：$VirtualDisplaySummary$\r$\n防火墙：将申请本次安装的一次管理员授权，并在完成前精确读回 Ligase 专属规则"
  ${Else}
    ${NSD_CreateLabel} 0 24u 100% 84u "程序目录：$INSTDIR$\r$\n数据目录：$DataRoot$\r$\n数据动作：$DataRootActionSummary$\r$\n创建桌面快捷方式：$DesktopShortcutSummary$\r$\n安装虚拟显示：$VirtualDisplaySummary$\r$\n防火墙：将申请本次安装的一次管理员授权，并在完成前精确读回 Ligase 专属规则"
  ${EndIf}
  Pop $1
  ${NSD_CreateLabel} 0 116u 100% 24u "如需修改请返回。只有数据绑定与防火墙精确读回成功后才会显示完成。"
  Pop $1
  nsDialogs::Show
FunctionEnd

Function InstallSummaryPageLeave
  ${If} $DataRoot == ""
    MessageBox MB_OK|MB_ICONSTOP "尚未确认数据目录。"
    Abort
  ${EndIf}
FunctionEnd

Function InstallResultPageCreate
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 18u "安装结果"
  Pop $1
  ${If} $DataRootMode == "migration"
    ${NSD_CreateLabel} 0 24u 100% 108u "$IntegrationResult$\r$\n程序目录：$INSTDIR$\r$\n标准数据目录：$DataRoot$\r$\n数据动作：$DataRootActionSummary$\r$\n回滚证据：旧数据仍原样保留在 $DataRootSource$\r$\n桌面快捷方式：$DesktopShortcutSummary$\r$\n虚拟显示：$VirtualDisplaySummary$\r$\n防火墙：Ligase 专属规则已精确验证"
  ${Else}
    ${NSD_CreateLabel} 0 24u 100% 96u "$IntegrationResult$\r$\n程序目录：$INSTDIR$\r$\n数据目录：$DataRoot$\r$\n数据动作：$DataRootActionSummary$\r$\n桌面快捷方式：$DesktopShortcutSummary$\r$\n虚拟显示：$VirtualDisplaySummary$\r$\n防火墙：Ligase 专属规则已精确验证"
  ${EndIf}
  Pop $1
  nsDialogs::Show
FunctionEnd

Function un.onInit
  SetRegView 64
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Global\Ligase.Host.Setup.v1") p .r0 ?e'
  Pop $SetupMutex
  Pop $1
  ${If} $1 = 183
    MessageBox MB_OK|MB_ICONSTOP "另一个 Ligase 安装、修复或卸载任务正在运行。"
    Abort
  ${EndIf}
FunctionEnd

Section "Ligase Host（必需）" SEC_MAIN
  SectionIn RO
  ; Validate the original argv first so duplicates and malformed explicit
  ; options cannot be corrected or hidden by the directory page.
  StrCpy $InstallParameters $OriginalInstallParameters
  StrCpy $SavedInstallDirectory $INSTDIR
  StrCpy $SavedDataRoot $DataRoot
  StrCpy $SavedDataRootSource $DataRootSource
  Call ResolveInstallerArguments
  ${If} $5 != "true"
    StrCpy $INSTDIR $SavedInstallDirectory
  ${EndIf}
  ; The first call validates raw native argv only. Keep the Data Root proposal
  ; that the user reviewed; otherwise a fresh/migration UUID could be regenerated
  ; after the confirmation page.
  StrCpy $DataRoot $SavedDataRoot
  StrCpy $DataRootSource $SavedDataRootSource
  StrCpy $DataRootMode $DataRootSelectionMode
  ; Then validate the final directory-page value. Both validations happen
  ; before SetOutPath or File can write to the selected installation root.
  ${If} $DataRoot == ""
    MessageBox MB_OK|MB_ICONSTOP "数据目录为空，未写入安装数据。"
    SetErrorLevel 10
    Quit
  ${EndIf}
  StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\"'
  Call ResolveInstallerArguments
  SetOutPath "$INSTDIR"
  File /r "${StageDir}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  ${If} $DataRootMode == "migration"
    nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Deployment\Manage-LigaseInstallation.ps1" -Action Install -InstallDirectory "$INSTDIR" -DataRoot "$DataRoot" -MigrateDataRoot -ConfigureFirewall'
  ${Else}
    nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Deployment\Manage-LigaseInstallation.ps1" -Action Install -InstallDirectory "$INSTDIR" -DataRoot "$DataRoot" -ConfigureFirewall'
  ${EndIf}
  Pop $0
  Pop $1
  ${StrTrimNewLines} $1 $1
  ${If} $0 != 0
    DetailPrint "Ligase integration failed with machine outcome."
    DetailPrint "Existing bootstrap, user data, and unknown files were not removed."
    MessageBox MB_OK|MB_ICONSTOP "Ligase 集成验证失败：数据绑定或防火墙读回未通过。安装未完成。"
    SetErrorLevel 10
    Quit
  ${EndIf}
  ${If} $DataRootMode == "migration"
    StrCpy $2 '{"code":"installed","success":true,"installMode":"packaged","dataRootState":"existing","dataRootAction":"migratedToStandardDataRoot","firewallState":"configured","firewallMachineCode":"configured"}'
  ${ElseIf} $DataRootMode == "existing"
    StrCpy $2 '{"code":"installed","success":true,"installMode":"packaged","dataRootState":"existing","dataRootAction":"preservedExistingBootstrap","firewallState":"configured","firewallMachineCode":"configured"}'
  ${Else}
    StrCpy $2 '{"code":"installed","success":true,"installMode":"packaged","dataRootState":"fresh","dataRootAction":"createdFreshBootstrap","firewallState":"configured","firewallMachineCode":"configured"}'
  ${EndIf}
  ${If} $1 != $2
    DetailPrint "Ligase integration returned an unknown or contradictory outcome."
    MessageBox MB_OK|MB_ICONSTOP "Ligase 集成结果无效或相互矛盾。安装未完成。"
    SetErrorLevel 10
    Quit
  ${EndIf}
  StrCpy $IntegrationResult "程序文件、数据绑定和防火墙规则均已验证。"
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

Section "创建桌面快捷方式（可选）" SEC_DESKTOP_SHORTCUT
  CreateShortcut "$DESKTOP\Ligase Host.lnk" "$INSTDIR\Ligase Host.exe"
SectionEnd

Section /o "Ligase 虚拟显示（可选）" SEC_VDISPLAY
  StrCpy $VirtualDisplaySummary "未安装"
  MessageBox MB_YESNO|MB_ICONEXCLAMATION \
    "SudoVDA 当前使用自签名发布者证书。继续会将该发布者加入本机信任存储，以便 Windows 安装内核驱动。串流物理桌面不需要此组件。是否继续？" \
    /SD IDNO IDNO skipVirtualDisplay
  nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Deployment\Manage-LigaseInstallation.ps1" -Action InstallVirtualDisplay -InstallDirectory "$INSTDIR"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    StrCpy $VirtualDisplaySummary "失败（物理桌面串流仍可用）"
    DetailPrint "虚拟显示未安装；物理桌面串流仍可用。"
  ${Else}
    StrCpy $VirtualDisplaySummary "已安装"
  ${EndIf}
  skipVirtualDisplay:
SectionEnd

Section "卸载"
  MessageBox MB_YESNO|MB_ICONQUESTION \
    "是否将 Ligase 个人数据移入可恢复的隔离目录？选择“否”会将数据保留在原位置。" \
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
