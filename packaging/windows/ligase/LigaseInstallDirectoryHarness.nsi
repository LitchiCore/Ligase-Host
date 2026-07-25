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

!include "InstallDirectoryValidation.nsh"

Function .onInit
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
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /oname=Resolve-LigaseInstallDirectory.ps1 "${ResolverScript}"
FunctionEnd

Function .onGUIEnd
  ${If} $FailureMode != ""
    SetErrorLevel 10
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
    ${If} $FailureMode == "helperFailure"
      StrCpy $1 "installationIntegrationFailed"
    ${ElseIf} $FailureMode == "migrationFailure"
      StrCpy $1 "dataRootMigrationReadbackFailed"
    ${ElseIf} $FailureMode == "integrationFailure"
      StrCpy $1 "installationFinalReadbackFailed"
    ${ElseIf} $FailureMode == "rollbackFailure"
      StrCpy $1 "installationActionFailed"
      StrCpy $2 "failed"
    ${ElseIf} $FailureMode == "silentProvisional"
      StrCpy $1 "installationFinalReadbackRequired"
      StrCpy $2 "notRequired"
    ${Else}
      SetErrorLevel 19
      Quit
    ${EndIf}
    FileOpen $5 "$EvidenceFile" w
    FileWriteUTF16LE $5 '{$\"schemaVersion$\":1,$\"phase$\":$\"failed$\",$\"success$\":false,$\"resultCode$\":$\"$1$\",$\"helper$\":{$\"exitCode$\":10},$\"rollback$\":{$\"state$\":$\"$2$\"},$\"firewall$\":{$\"state$\":$\"failed$\"},$\"displayedSuccess$\":false,$\"displayedFailure$\":true}'
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
  ${If} $5 != "true"
    StrCpy $INSTDIR $4
  ${EndIf}
  ; The selected value validation closes the same boundary as the directory UI.
  ${If} $DataRoot == ""
    StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\"'
  ${Else}
    StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\"'
  ${EndIf}
  Call ResolveInstallerArguments
  ; Controlled UI lifecycle: DataRoot page Next, Back, then Next again. The
  ; selected value is converted to an explicit validated pair each time and
  ; must remain byte-for-byte stable without creating either directory.
  StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\"'
  Call ResolveInstallerArguments
  StrCpy $InstallParameters '$\"ligase-installer.exe$\" $\"/InstallDirectory=$INSTDIR$\" $\"/DataRoot=$DataRoot$\"'
  Call ResolveInstallerArguments
  ${If} $HarnessResultFile == ""
    SetErrorLevel 19
    Quit
  ${EndIf}
  FileOpen $5 "$HarnessResultFile" w
  FileWriteWord $5 0xFEFF
  FileWriteUTF16LE $5 "$INSTDIR$\r$\n$DataRoot"
  FileClose $5
  SetErrorLevel 0
  harnessDone:
SectionEnd
