Unicode true
RequestExecutionLevel user
SilentInstall silent
Name "Ligase Install Directory Validation Harness"
OutFile "${OutputFile}"

!define LIGASE_VALIDATION_HARNESS
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "StrFunc.nsh"
${StrTrimNewLines}

Var InstallParameters
Var HarnessResultFile
Var DataRoot
Var DataRootMode
Var ProgramDataRoot
Var FailureMode
Var EvidenceFile
Var DataRootSource
Var DataRootInitialMode
Var TestOperatorLocalAppData
Var ResolverOrphanDecision

!include "InstallDirectoryValidation.nsh"

Function .onInit
  System::Call 'kernel32::SetEnvironmentVariableW(w "LIGASE_INSTALL_VALIDATION_HARNESS", w "1")'
  SetShellVarContext all
  StrCpy $ProgramDataRoot "$APPDATA"
  SetShellVarContext current
  System::Call 'kernel32::GetCommandLineW() w .r0'
  StrCpy $InstallParameters $0
  ${GetParameters} $6
  StrCpy $0 $6
  ${GetOptions} $0 "/ResultFile=" $HarnessResultFile
  StrCpy $0 $6
  ${GetOptions} $0 "/FailureMode=" $FailureMode
  StrCpy $0 $6
  ${GetOptions} $0 "/EvidenceFile=" $EvidenceFile
  StrCpy $0 $6
  ${GetOptions} $0 "/TestOperatorLocalAppData=" $TestOperatorLocalAppData
  ${If} $TestOperatorLocalAppData != ""
    System::Call 'kernel32::SetEnvironmentVariableW(w "LIGASE_INSTALL_TEST_OPERATOR_LOCAL_APP_DATA", w "$TestOperatorLocalAppData")'
  ${EndIf}
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /oname=Resolve-LigaseInstallDirectory.ps1 "${ResolverScript}"
FunctionEnd

Function .onGUIEnd
  ${If} $FailureMode != ""
    SetErrorLevel 10
  ${ElseIf} $HarnessResultFile != ""
  ${AndIfNot} ${FileExists} "$HarnessResultFile"
    ; Resolver rejection is terminal before the silent section can create its
    ; typed result. Preserve the product contract as a native non-zero exit;
    ; orphan recovery without an explicit action is specifically machine 18.
    SetErrorLevel 18
  ${EndIf}
FunctionEnd

Function .onInstFailed
  SetErrorLevel 10
FunctionEnd

Section
  ${If} $FailureMode != ""
    ${If} $EvidenceFile == ""
      SetErrorLevel 19
      Quit
    ${EndIf}
    StrCpy $1 "integrationFailed"
    StrCpy $2 "completed"
    StrCpy $3 "artifacts"
    ${If} $FailureMode == "helperFailure"
      StrCpy $1 "installationIntegrationFailed"
      StrCpy $3 "artifacts"
    ${ElseIf} $FailureMode == "migrationFailure"
      StrCpy $1 "dataRootMigrationReadbackFailed"
      StrCpy $3 "dataRoot"
    ${ElseIf} $FailureMode == "integrationFailure"
      StrCpy $1 "installationFinalReadbackFailed"
      StrCpy $3 "startMenu"
    ${ElseIf} $FailureMode == "rollbackFailure"
      StrCpy $1 "installationActionFailed"
      StrCpy $2 "failed"
      StrCpy $3 "dataRoot"
    ${ElseIf} $FailureMode == "silentProvisional"
      StrCpy $1 "installationFinalReadbackRequired"
      StrCpy $2 "notRequired"
      StrCpy $3 "none"
    ${Else}
      SetErrorLevel 19
      Quit
    ${EndIf}
    FileOpen $5 "$EvidenceFile" w
    FileWriteUTF16LE $5 '{$\"schemaVersion$\":1,$\"phase$\":$\"failed$\",$\"success$\":false,$\"resultCode$\":$\"$1$\",$\"failedField$\":$\"$3$\",$\"components$\":{$\"artifacts$\":$\"pending$\",$\"bootstrap$\":$\"pending$\",$\"dataRoot$\":$\"pending$\",$\"installTransaction$\":$\"pending$\",$\"arp$\":$\"pending$\",$\"startMenu$\":$\"pending$\",$\"desktop$\":$\"pending$\",$\"firewall$\":$\"pending$\",$\"virtualDisplay$\":$\"pending$\"},$\"helper$\":{$\"exitCode$\":10},$\"transactionHelper$\":{$\"nativeExitCode$\":18,$\"stage$\":$\"finalReadback$\",$\"nativeCategory$\":$\"none$\",$\"nativeCode$\":0,$\"aclMutationOccurred$\":false,$\"aclRollback$\":$\"notRequired$\",$\"recoveryAction$\":$\"none$\"},$\"rollback$\":{$\"state$\":$\"$2$\",$\"shortcut$\":$\"notRequired$\",$\"firewall$\":$\"notRequired$\",$\"transactionCleanup$\":$\"notCreated$\"},$\"firewall$\":{$\"state$\":$\"notChecked$\"},$\"displayedSuccess$\":false,$\"displayedFailure$\":true}'
    FileClose $5
    FileOpen $5 "$HarnessResultFile" w
    FileWriteUTF16LE $5 "failed$\r$\n$1$\r$\n$2"
    FileClose $5
    SetErrorLevel 10
    Abort
  ${EndIf}
  ; The raw argv validation rejects duplicate and malformed options.
  StrCpy $4 $INSTDIR
  Call ResolveInstallerArguments
  StrCpy $ResolverOrphanDecision $6
  StrCpy $DataRootSource $4
  StrCpy $DataRootInitialMode $DataRootMode
  ${If} $5 != "true"
    StrCpy $INSTDIR $4
  ${EndIf}
  ; The selected value validation closes the same boundary as the directory UI.
  ${If} $DataRoot == ""
    StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\"'
  ${ElseIf} $ResolverOrphanDecision == "confirmedCreateFresh"
    StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\" /OrphanLegacyAction=CreateFresh'
  ${ElseIf} $ResolverOrphanDecision == "confirmedRecover"
    StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\" /OrphanLegacyAction=Recover'
  ${Else}
    StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\"'
  ${EndIf}
  Call ResolveInstallerArguments
  ; Controlled UI lifecycle: DataRoot page Next, Back, then Next again. The
  ; selected value is converted to an explicit validated pair each time and
  ; must remain byte-for-byte stable without creating either directory.
  ${If} $ResolverOrphanDecision == "confirmedCreateFresh"
    StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\" /OrphanLegacyAction=CreateFresh'
  ${ElseIf} $ResolverOrphanDecision == "confirmedRecover"
    StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\" /OrphanLegacyAction=Recover'
  ${Else}
    StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\"'
  ${EndIf}
  Call ResolveInstallerArguments
  ${If} $ResolverOrphanDecision == "confirmedCreateFresh"
    StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\" /OrphanLegacyAction=CreateFresh'
  ${ElseIf} $ResolverOrphanDecision == "confirmedRecover"
    StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\" /OrphanLegacyAction=Recover'
  ${Else}
    StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\"'
  ${EndIf}
  Call ResolveInstallerArguments
  ${If} $HarnessResultFile == ""
    SetErrorLevel 19
    Quit
  ${EndIf}
  FileOpen $5 "$HarnessResultFile" w
  FileWriteWord $5 0xFEFF
  FileWriteUTF16LE $5 "$INSTDIR$\r$\n$DataRoot$\r$\n$DataRootInitialMode$\r$\n$DataRootSource"
  FileClose $5
  SetErrorLevel 0
  harnessDone:
SectionEnd
