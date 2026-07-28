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
!define MUI_CUSTOMFUNCTION_ABORT InstallerUserAbort
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
Page custom DataRootPageCreate DataRootPageLeave
!insertmacro MUI_PAGE_COMPONENTS
Page custom InstallSummaryPageCreate InstallSummaryPageLeave
!insertmacro MUI_PAGE_INSTFILES
Page custom InstallResultPageCreate InstallResultPageLeave
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Var SetupMutex
Var DataRoot
Var DataRootDialog
Var DataRootText
Var DataRootAdvanced
Var OrphanRecoveryChoice
Var OrphanFreshChoice
Var OrphanLegacyDecision
Var OrphanLegacyIntent
Var OrphanLegacySource
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
Var InstallOutcome
Var InstallOutcomeCode
Var InstallHelperExit
Var InstallRollback
Var InstallFirewall
Var InstallResidue
Var DataRootResidue
Var VirtualDisplayOutcome
!define LIGASE_SECTION_DESKTOP_SHORTCUT 1
!define LIGASE_SECTION_VIRTUAL_DISPLAY 2

!include "InstallDirectoryValidation.nsh"

Function ConsumeResolvedOrphanDecision
  ${If} $6 == "confirmedRecover"
    StrCpy $OrphanLegacyDecision "Recover"
  ${ElseIf} $6 == "confirmedCreateFresh"
    StrCpy $OrphanLegacyDecision "CreateFresh"
  ${Else}
    StrCpy $OrphanLegacyDecision ""
  ${EndIf}
FunctionEnd

Function .onInit
  System::Call 'kernel32::SetEnvironmentVariableW(w "LIGASE_INSTALL_VALIDATION_HARNESS", p 0)'
  System::Call 'kernel32::SetEnvironmentVariableW(w "LIGASE_INSTALL_TEST_OPERATOR_LOCAL_APP_DATA", p 0)'
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
  StrCpy $InstallOutcome "pending"
  StrCpy $InstallOutcomeCode "notStarted"
  StrCpy $InstallHelperExit "-1"
  StrCpy $InstallRollback "notRequired"
  StrCpy $InstallFirewall "notChecked"
  StrCpy $InstallResidue "unknown"
  StrCpy $DataRootResidue "unknown"
  StrCpy $VirtualDisplayOutcome "notSelected"
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
  File /oname=Manage-LigaseInstallation.ps1 "${StageDir}\Deployment\Manage-LigaseInstallation.ps1"
  File /oname=ligase-install-manifest.json "${StageDir}\ligase-install-manifest.json"
  ; One native argv parser owns both path options and upgrade fallback.
  ; The result only pre-fills UI state; the required section validates the
  ; original argv and final UI values again before any product write.
  Call ResolveInstallerArguments
  StrCpy $DataRootSelectionMode $DataRootMode
  Call ConsumeResolvedOrphanDecision
  ${If} $OrphanLegacyDecision != ""
    StrCpy $DataRootSelectionMode "orphanLegacyRecovery"
  ${EndIf}
  ClearErrors
  Call RecordInstallerEvidenceInitialized
  IfErrors 0 +2
    Abort
FunctionEnd

Function RecordInstallerEvidenceInitialized
  nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\Manage-LigaseInstallation.ps1" -Action RecordEvidence -InstallDirectory "$INSTDIR" -DataRoot "$DataRoot" -EvidenceManifestPath "$PLUGINSDIR\ligase-install-manifest.json" -EvidencePhase initialized -EvidenceSuccess unknown -EvidenceResultCode notStarted -EvidenceDataRootAction none -EvidenceHelperExit -1 -EvidenceRollback notRequired -EvidenceFirewall notChecked -EvidenceInstallResidue unknown -EvidenceDataRootResidue unknown'
  Pop $0
  Pop $1
  ${StrTrimNewLines} $1 $1
  ${If} $0 != 0
  ${OrIf} $1 != '{"code":"installerEvidenceRecorded","success":true,"phase":"initialized","resultCode":"notStarted"}'
    MessageBox MB_OK|MB_ICONSTOP "无法创建受保护的安装诊断记录。安装尚未开始。"
    Abort
  ${EndIf}
FunctionEnd

Function RecordInstallerEvidenceConfirmed
  ${If} $DataRootMode == "migration"
    StrCpy $2 "migrateToStandard"
  ${ElseIf} $DataRootMode == "orphanLegacyRecovery"
    StrCpy $2 "recoverOrphanLegacyDataRoot"
  ${ElseIf} $DataRootMode == "existing"
    StrCpy $2 "preserveExisting"
  ${Else}
    StrCpy $2 "createFresh"
  ${EndIf}
  nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\Manage-LigaseInstallation.ps1" -Action RecordEvidence -InstallDirectory "$INSTDIR" -DataRoot "$DataRoot" -EvidenceDataRootSource "$DataRootSource" -EvidenceManifestPath "$PLUGINSDIR\ligase-install-manifest.json" -EvidencePhase confirmed -EvidenceSuccess unknown -EvidenceResultCode userConfirmed -EvidenceDataRootAction $2 -EvidenceHelperExit -1 -EvidenceRollback notRequired -EvidenceFirewall notChecked -EvidenceInstallResidue unknown -EvidenceDataRootResidue unknown'
  Pop $0
  Pop $1
  ${StrTrimNewLines} $1 $1
  ${If} $0 != 0
  ${OrIf} $1 != '{"code":"installerEvidenceRecorded","success":true,"phase":"confirmed","resultCode":"userConfirmed"}'
    MessageBox MB_OK|MB_ICONSTOP "无法更新安装诊断记录。尚未写入程序或 Host 数据。"
    Abort
  ${EndIf}
FunctionEnd

Function RecordInstallerEvidenceCancelled
  nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\Manage-LigaseInstallation.ps1" -Action RecordEvidence -InstallDirectory "$INSTDIR" -DataRoot "$DataRoot" -EvidenceDataRootSource "$DataRootSource" -EvidenceManifestPath "$PLUGINSDIR\ligase-install-manifest.json" -EvidencePhase cancelled -EvidenceSuccess false -EvidenceResultCode userCancelled -EvidenceDataRootAction none -EvidenceHelperExit $InstallHelperExit -EvidenceRollback $InstallRollback -EvidenceFirewall $InstallFirewall -EvidenceInstallResidue $InstallResidue -EvidenceDataRootResidue $DataRootResidue'
  Pop $0
  Pop $1
FunctionEnd

Function RecordInstallerEvidenceIntegrating
  ${If} $DataRootMode == "migration"
    StrCpy $2 "migrateToStandard"
  ${ElseIf} $DataRootMode == "orphanLegacyRecovery"
    StrCpy $2 "recoverOrphanLegacyDataRoot"
  ${ElseIf} $DataRootMode == "existing"
    StrCpy $2 "preserveExisting"
  ${Else}
    StrCpy $2 "createFresh"
  ${EndIf}
  nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\Manage-LigaseInstallation.ps1" -Action RecordEvidence -InstallDirectory "$INSTDIR" -DataRoot "$DataRoot" -EvidenceDataRootSource "$DataRootSource" -EvidenceManifestPath "$PLUGINSDIR\ligase-install-manifest.json" -EvidencePhase integrating -EvidenceSuccess unknown -EvidenceResultCode integrationStarted -EvidenceDataRootAction $2 -EvidenceHelperExit -1 -EvidenceRollback notRequired -EvidenceFirewall notChecked -EvidenceInstallResidue absent -EvidenceDataRootResidue unknown'
  Pop $0
  Pop $1
  ${StrTrimNewLines} $1 $1
  ${If} $0 != 0
  ${OrIf} $1 != '{"code":"installerEvidenceRecorded","success":true,"phase":"integrating","resultCode":"integrationStarted"}'
    MessageBox MB_OK|MB_ICONSTOP "无法更新安装诊断记录。尚未写入程序或 Host 数据。"
    Abort
  ${EndIf}
FunctionEnd

Function InstallerUserAbort
  StrCpy $InstallOutcome "cancelled"
  StrCpy $InstallOutcomeCode "userCancelled"
  Call RecordInstallerEvidenceCancelled
FunctionEnd

Function .onInstFailed
  ${If} $InstallOutcome != "failed"
    StrCpy $InstallOutcome "failed"
    StrCpy $InstallOutcomeCode "installerExecutionFailed"
  ${EndIf}
FunctionEnd

Function .onGUIEnd
  ${If} $InstallOutcome == "failed"
  ${OrIf} $InstallOutcome == "cancelled"
    SetErrorLevel 10
  ${EndIf}
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
  ${ElseIf} $DataRootSelectionMode == "orphanLegacyRecovery"
    ${NSD_CreateLabel} 0 0 100% 34u "检测到未绑定的旧 Host 数据。请选择恢复并迁移以保留原身份，或明确创建全新身份。旧目录不会被删除。"
  ${Else}
    ${NSD_CreateLabel} 0 0 100% 28u "主机数据包含 Host 身份、已配对设备、游戏库和设置。安装程序会在 Windows 公共应用数据区自动创建专属目录，仅当前 Host 运行账户可访问。"
  ${EndIf}
  Pop $0
  ${If} $DataRootSelectionMode == "orphanLegacyRecovery"
    ${NSD_CreateRadioButton} 0 32u 100% 12u "恢复并迁移（保留旧 Host 身份）"
    Pop $OrphanRecoveryChoice
    ${NSD_CreateRadioButton} 0 48u 100% 12u "创建全新 Host 身份（不会使用检测到的旧数据）"
    Pop $OrphanFreshChoice
    ${NSD_CreateLabel} 0 66u 100% 30u "旧数据：$DataRootSource$\r$\n标准目录：$DataRoot"
    Pop $0
    ${If} $OrphanLegacyDecision == "Recover"
      ${NSD_Check} $OrphanRecoveryChoice
    ${ElseIf} $OrphanLegacyDecision == "CreateFresh"
      ${NSD_Check} $OrphanFreshChoice
    ${EndIf}
  ${EndIf}
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
  ${ElseIf} $DataRootSelectionMode == "orphanLegacyRecovery"
    EnableWindow $DataRootAdvanced 0
    EnableWindow $DataRootText 0
    ShowWindow $DataRootAdvanced ${SW_HIDE}
    ShowWindow $DataRootText ${SW_HIDE}
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
  ${ElseIf} $DataRootSelectionMode == "orphanLegacyRecovery"
    StrCpy $OrphanLegacySource $DataRootSource
    ${NSD_GetState} $OrphanRecoveryChoice $0
    ${NSD_GetState} $OrphanFreshChoice $1
    ${If} $0 == ${BST_CHECKED}
      StrCpy $OrphanLegacyIntent "Recover"
      StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\" /OrphanLegacyAction=Recover'
    ${ElseIf} $1 == ${BST_CHECKED}
      StrCpy $OrphanLegacyIntent "CreateFresh"
      StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\" /OrphanLegacyAction=CreateFresh'
    ${Else}
      MessageBox MB_OK|MB_ICONSTOP "请选择恢复旧 Host 数据，或明确创建全新 Host 身份。"
      Abort
    ${EndIf}
    Call ResolveInstallerArguments
    Call ConsumeResolvedOrphanDecision
    ${If} $OrphanLegacyDecision != $OrphanLegacyIntent
      MessageBox MB_OK|MB_ICONSTOP "无法确认旧 Host 数据操作。未写入任何数据。"
      Abort
    ${ElseIf} $OrphanLegacyDecision == "Recover"
    ${AndIf} $DataRootMode != "orphanLegacyRecovery"
      MessageBox MB_OK|MB_ICONSTOP "无法确认旧 Host 数据恢复事务。未写入任何数据。"
      Abort
    ${ElseIf} $OrphanLegacyDecision == "CreateFresh"
    ${AndIf} $DataRootMode != "freshDefault"
      MessageBox MB_OK|MB_ICONSTOP "无法确认全新 Host 身份目标。未写入任何数据。"
      Abort
    ${EndIf}
    StrCpy $DataRootSource $OrphanLegacySource
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
  ${If} $DataRootSelectionMode == "orphanLegacyRecovery"
    ${If} $OrphanLegacyDecision == "Recover"
      StrCpy $DataRootMode "orphanLegacyRecovery"
    ${Else}
      StrCpy $DataRootMode "freshDefault"
    ${EndIf}
  ${Else}
    StrCpy $DataRootMode $DataRootSelectionMode
  ${EndIf}
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
  ${ElseIf} $DataRootMode == "orphanLegacyRecovery"
    StrCpy $DataRootActionSummary "恢复未绑定的旧 Host 数据并迁移到标准安全目录"
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
  ${OrIf} $DataRootMode == "orphanLegacyRecovery"
    ${NSD_CreateLabel} 0 24u 100% 100u "程序目录：$INSTDIR$\r$\n旧数据目录：$DataRootSource$\r$\n标准数据目录：$DataRoot$\r$\n数据动作：$DataRootActionSummary$\r$\n创建桌面快捷方式：$DesktopShortcutSummary$\r$\n安装虚拟显示：$VirtualDisplaySummary$\r$\n安全目录：如检测到先前安装留下的精确空目录，将在同一身份下修复其访问控制$\r$\n防火墙：将申请本次安装的一次管理员授权，并在完成前精确读回 Ligase 专属规则"
  ${Else}
    ${NSD_CreateLabel} 0 24u 100% 84u "程序目录：$INSTDIR$\r$\n数据目录：$DataRoot$\r$\n数据动作：$DataRootActionSummary$\r$\n创建桌面快捷方式：$DesktopShortcutSummary$\r$\n安装虚拟显示：$VirtualDisplaySummary$\r$\n安全目录：如检测到先前安装留下的精确空目录，将在同一身份下修复其访问控制$\r$\n防火墙：将申请本次安装的一次管理员授权，并在完成前精确读回 Ligase 专属规则"
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
  ClearErrors
  Call RecordInstallerEvidenceConfirmed
  IfErrors 0 +2
    Abort
FunctionEnd

Function FinalizeInstallTerminal
  ${If} $InstallOutcome == "provisional"
    ${If} $DataRootMode == "migration"
      StrCpy $5 "migrateToStandard"
    ${ElseIf} $DataRootMode == "orphanLegacyRecovery"
      StrCpy $5 "recoverOrphanLegacyDataRoot"
    ${ElseIf} $DataRootMode == "existing"
      StrCpy $5 "preserveExisting"
    ${Else}
      StrCpy $5 "createFresh"
    ${EndIf}
    SectionGetFlags ${LIGASE_SECTION_DESKTOP_SHORTCUT} $2
    IntOp $2 $2 & ${SF_SELECTED}
    ${If} $2 != 0
      StrCpy $3 "-DesktopShortcutSelected"
    ${Else}
      StrCpy $3 ""
    ${EndIf}
    SectionGetFlags ${LIGASE_SECTION_VIRTUAL_DISPLAY} $2
    IntOp $2 $2 & ${SF_SELECTED}
    ${If} $2 != 0
      StrCpy $4 "-VirtualDisplaySelected"
    ${Else}
      StrCpy $4 ""
    ${EndIf}
    nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Deployment\Manage-LigaseInstallation.ps1" -Action FinalizeInstall -InstallDirectory "$INSTDIR" -DataRoot "$DataRoot" -EvidenceDataRootSource "$DataRootSource" -EvidenceDataRootAction $5 -EvidenceHelperExit $InstallHelperExit -EvidenceRollback $InstallRollback -EvidenceFirewall configured -EvidenceInstallResidue nonEmpty -EvidenceDataRootResidue nonEmpty -ConfigureFirewall $3 $4 -VirtualDisplayOutcome $VirtualDisplayOutcome'
    Pop $0
    Pop $1
    ${StrTrimNewLines} $1 $1
    ${If} $0 == 0
    ${AndIf} $1 == '{"code":"installationFinalized","success":true,"dataRootState":"existing","firewallState":"configured"}'
      StrCpy $InstallOutcome "succeeded"
      StrCpy $InstallOutcomeCode "installed"
      StrCpy $IntegrationResult "程序文件、数据绑定、快捷方式和防火墙规则均已精确读回。"
    ${Else}
      StrCpy $InstallOutcome "failed"
      StrCpy $InstallOutcomeCode "installationFinalReadbackFailed"
      StrCpy $IntegrationResult "安装最终读回失败。未将本次操作标记为成功，请使用安装诊断结果进行修复。"
    ${EndIf}
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
  ${If} $InstallOutcome == "failed"
    ${NSD_CreateLabel} 0 24u 100% 108u "$IntegrationResult$\r$\n结果代码：$InstallOutcomeCode$\r$\n程序目录残留：$InstallResidue$\r$\n数据目录残留：$DataRootResidue$\r$\n旧 Host 数据和原 bootstrap 证据未被静默删除。关闭此页后安装程序将以失败状态退出。"
  ${ElseIf} $DataRootMode == "migration"
  ${OrIf} $DataRootMode == "orphanLegacyRecovery"
    ${NSD_CreateLabel} 0 24u 100% 108u "$IntegrationResult$\r$\n程序目录：$INSTDIR$\r$\n标准数据目录：$DataRoot$\r$\n数据动作：$DataRootActionSummary$\r$\n回滚证据：旧数据仍原样保留在 $DataRootSource$\r$\n桌面快捷方式：$DesktopShortcutSummary$\r$\n虚拟显示：$VirtualDisplaySummary$\r$\n防火墙：Ligase 专属规则已精确验证"
  ${Else}
    ${NSD_CreateLabel} 0 24u 100% 96u "$IntegrationResult$\r$\n程序目录：$INSTDIR$\r$\n数据目录：$DataRoot$\r$\n数据动作：$DataRootActionSummary$\r$\n桌面快捷方式：$DesktopShortcutSummary$\r$\n虚拟显示：$VirtualDisplaySummary$\r$\n防火墙：Ligase 专属规则已精确验证"
  ${EndIf}
  Pop $1
  nsDialogs::Show
FunctionEnd

Function InstallResultPageLeave
  ${If} $InstallOutcome == "failed"
    SetErrorLevel 10
    Quit
  ${EndIf}
FunctionEnd

Function un.onInit
  SetRegView 64
  ; All installed shortcuts are machine-scoped. The helper removes only
  ; shortcuts whose target, arguments, and working directory match Ligase.
  SetShellVarContext all
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
    StrCpy $InstallOutcome "failed"
    StrCpy $InstallOutcomeCode "dataRootRequired"
    StrCpy $IntegrationResult "数据目录验证失败，安装未写入成功状态。"
    Goto mainSectionDone
  ${EndIf}
  ${If} $DataRootSelectionMode == "orphanLegacyRecovery"
    ${If} $OrphanLegacyDecision == "Recover"
      StrCpy $OrphanLegacyIntent "Recover"
      StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\" /OrphanLegacyAction=Recover'
    ${ElseIf} $OrphanLegacyDecision == "CreateFresh"
      StrCpy $OrphanLegacyIntent "CreateFresh"
      StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\" /OrphanLegacyAction=CreateFresh'
    ${Else}
      DetailPrint "检测到未绑定的旧 Host 数据，但尚未明确选择恢复或创建全新身份。"
      StrCpy $InstallOutcome "failed"
      StrCpy $InstallOutcomeCode "orphanLegacyActionRequired"
      StrCpy $IntegrationResult "必须明确选择恢复旧 Host 数据或创建全新身份；安装尚未写入任何数据。"
      Goto mainSectionDone
    ${EndIf}
  ${Else}
    StrCpy $OrphanLegacyIntent ""
    StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\"'
  ${EndIf}
  Call ResolveInstallerArguments
  ${If} $DataRootSelectionMode == "orphanLegacyRecovery"
    Call ConsumeResolvedOrphanDecision
    ${If} $OrphanLegacyDecision == ""
    ${OrIf} $OrphanLegacyDecision != $OrphanLegacyIntent
      DetailPrint "旧 Host 数据操作未通过 resolver 最终确认。"
      StrCpy $InstallOutcome "failed"
      StrCpy $InstallOutcomeCode "orphanLegacyActionRequired"
      StrCpy $IntegrationResult "旧 Host 数据操作未通过最终验证；安装尚未写入任何数据。"
      Goto mainSectionDone
    ${EndIf}
  ${EndIf}
  ClearErrors
  Call RecordInstallerEvidenceIntegrating
  IfErrors 0 +2
    Goto mainSectionEvidenceFailed
  SetOutPath "$INSTDIR"
  File /r "${StageDir}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  ; Shortcut selection is known before sections execute. Pass it to the
  ; single ownership-aware integration helper instead of creating links in
  ; the current elevated user's shell folders.
  SetShellVarContext all
  SectionGetFlags ${LIGASE_SECTION_DESKTOP_SHORTCUT} $3
  IntOp $3 $3 & ${SF_SELECTED}
  ${If} $3 != 0
    StrCpy $3 "-DesktopShortcutSelected"
  ${Else}
    StrCpy $3 ""
  ${EndIf}
  ; The virtual-display selection is also transaction identity. Freeze it
  ; before the required section writes the install journal so final readback
  ; compares the same selection that the optional section will consume.
  SectionGetFlags ${LIGASE_SECTION_VIRTUAL_DISPLAY} $2
  IntOp $2 $2 & ${SF_SELECTED}
  ${If} $2 != 0
    StrCpy $4 "-VirtualDisplaySelected"
  ${Else}
    StrCpy $4 ""
  ${EndIf}
  ${If} $DataRootMode == "migration"
    nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Deployment\Manage-LigaseInstallation.ps1" -Action Install -InstallDirectory "$INSTDIR" -DataRoot "$DataRoot" -MigrateDataRoot -ConfigureFirewall $3 $4'
  ${ElseIf} $DataRootMode == "orphanLegacyRecovery"
    nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Deployment\Manage-LigaseInstallation.ps1" -Action Install -InstallDirectory "$INSTDIR" -DataRoot "$DataRoot" -RecoverOrphanDataRoot -RecoveryDataRootSource "$DataRootSource" -ConfigureFirewall $3 $4'
  ${Else}
    nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Deployment\Manage-LigaseInstallation.ps1" -Action Install -InstallDirectory "$INSTDIR" -DataRoot "$DataRoot" -ConfigureFirewall $3 $4'
  ${EndIf}
  Pop $0
  Pop $1
  StrCpy $InstallHelperExit $0
  ${StrTrimNewLines} $1 $1
  ${If} $0 != 0
    DetailPrint "Ligase integration failed with machine outcome."
    DetailPrint "Existing bootstrap, user data, and unknown files were not removed."
    StrCpy $InstallOutcome "failed"
    StrCpy $InstallOutcomeCode "installationIntegrationFailed"
    StrCpy $InstallRollback "unknown"
    StrCpy $InstallFirewall "unknown"
    StrCpy $InstallResidue "nonEmpty"
    StrCpy $DataRootResidue "unknown"
    StrCpy $IntegrationResult "Ligase 集成验证失败：数据绑定、迁移或防火墙读回未通过。"
    Goto mainSectionDone
  ${EndIf}
  ${If} $DataRootMode == "migration"
    StrCpy $2 '{"code":"installed","success":true,"installMode":"packaged","dataRootState":"existing","dataRootAction":"migratedToStandardDataRoot","firewallState":"configured","firewallMachineCode":"configured"}'
  ${ElseIf} $DataRootMode == "orphanLegacyRecovery"
    StrCpy $2 '{"code":"installed","success":true,"installMode":"packaged","dataRootState":"existing","dataRootAction":"recoveredOrphanLegacyDataRoot","firewallState":"configured","firewallMachineCode":"configured"}'
  ${ElseIf} $DataRootMode == "existing"
    StrCpy $2 '{"code":"installed","success":true,"installMode":"packaged","dataRootState":"existing","dataRootAction":"preservedExistingBootstrap","firewallState":"configured","firewallMachineCode":"configured"}'
  ${Else}
    StrCpy $2 '{"code":"installed","success":true,"installMode":"packaged","dataRootState":"fresh","dataRootAction":"createdFreshBootstrap","firewallState":"configured","firewallMachineCode":"configured"}'
  ${EndIf}
  ${If} $1 != $2
    DetailPrint "Ligase integration returned an unknown or contradictory outcome."
    StrCpy $InstallOutcome "failed"
    StrCpy $InstallOutcomeCode "installationOutcomeInvalid"
    StrCpy $InstallRollback "unknown"
    StrCpy $InstallFirewall "unknown"
    StrCpy $InstallResidue "nonEmpty"
    StrCpy $DataRootResidue "unknown"
    StrCpy $IntegrationResult "Ligase 集成返回了无效或相互矛盾的结果。"
    nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Deployment\Manage-LigaseInstallation.ps1" -Action RecordEvidence -InstallDirectory "$INSTDIR" -DataRoot "$DataRoot" -EvidencePhase failed -EvidenceSuccess false -EvidenceResultCode installationOutcomeInvalid -EvidenceDataRootAction none -EvidenceHelperExit 0 -EvidenceRollback unknown -EvidenceFirewall unknown -EvidenceInstallResidue nonEmpty -EvidenceDataRootResidue unknown'
    Pop $0
    Pop $1
    Goto mainSectionDone
  ${EndIf}
  StrCpy $IntegrationResult "程序文件、数据绑定和防火墙规则均已验证。"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "DisplayName" "Ligase Host"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "DisplayIcon" "$INSTDIR\Ligase Host.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "Publisher" "Ligase"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "NoRepair" 1
  StrCpy $InstallOutcome "provisional"
  StrCpy $InstallOutcomeCode "finalReadbackPending"
  StrCpy $InstallFirewall "configured"
  StrCpy $InstallResidue "nonEmpty"
  StrCpy $DataRootResidue "nonEmpty"
  mainSectionDone:
  Goto mainSectionEnd
  mainSectionEvidenceFailed:
  StrCpy $InstallOutcome "failed"
  StrCpy $InstallOutcomeCode "installerEvidenceUnavailable"
  StrCpy $IntegrationResult "无法写入安装诊断结果，安装已在写入产品文件前停止。"
  mainSectionEnd:
SectionEnd

Section "创建桌面快捷方式（可选）" SEC_DESKTOP_SHORTCUT
  ; The selected flag is consumed by the ownership-aware integration helper
  ; in the required section. No shortcut is created from the current-user
  ; shell context here.
SectionEnd

Section /o "Ligase 虚拟显示（可选）" SEC_VDISPLAY
  ${If} $InstallOutcome == "failed"
    Goto virtualDisplayDone
  ${EndIf}
  StrCpy $VirtualDisplaySummary "未安装"
  StrCpy $VirtualDisplayOutcome "declined"
  MessageBox MB_YESNO|MB_ICONEXCLAMATION \
    "SudoVDA 当前使用自签名发布者证书。继续会将该发布者加入本机信任存储，以便 Windows 安装内核驱动。串流物理桌面不需要此组件。是否继续？" \
    /SD IDNO IDNO skipVirtualDisplay
  nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Deployment\Manage-LigaseInstallation.ps1" -Action InstallVirtualDisplay -InstallDirectory "$INSTDIR"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    StrCpy $VirtualDisplaySummary "失败（物理桌面串流仍可用）"
    StrCpy $VirtualDisplayOutcome "failed"
    DetailPrint "虚拟显示未安装；物理桌面串流仍可用。"
  ${Else}
    StrCpy $VirtualDisplaySummary "已安装"
    StrCpy $VirtualDisplayOutcome "installed"
  ${EndIf}
  skipVirtualDisplay:
  virtualDisplayDone:
SectionEnd

Section -Finalize SEC_FINALIZE
  Call FinalizeInstallTerminal
  ${If} $InstallOutcome == "failed"
    DetailPrint "安装失败：$InstallOutcomeCode"
    DetailPrint "诊断结果已持久化；旧 Host 数据与原 bootstrap 未被静默删除。"
    SetErrorLevel 10
    Abort
  ${ElseIf} $InstallOutcome != "succeeded"
    StrCpy $InstallOutcome "failed"
    StrCpy $InstallOutcomeCode "installationTerminalStateInvalid"
    StrCpy $IntegrationResult "安装终态无效，未显示成功。"
    DetailPrint "安装失败：installationTerminalStateInvalid"
    SetErrorLevel 10
    Abort
  ${EndIf}
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
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host"
  RMDir /r "$INSTDIR"
SectionEnd
