Unicode true
RequestExecutionLevel user
SilentInstall silent
Name "Ligase Install Directory Validation Harness"
OutFile "${OutputFile}"

!define LIGASE_VALIDATION_HARNESS
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "StrFunc.nsh"
${StrStr}

Var InstallParameters
Var HarnessResultFile
Var InstallDirectoryOptionPresent

!include "InstallDirectoryValidation.nsh"

Function .onInit
  System::Call 'kernel32::GetCommandLineW() w .r0'
  StrCpy $InstallParameters $0
  ${StrStr} $InstallDirectoryOptionPresent $InstallParameters "/InstallDirectory"
  ${GetParameters} $6
  StrCpy $0 $6
  ${GetOptions} $0 "/ResultFile=" $HarnessResultFile
  StrCpy $0 $6
  ${GetOptions} $0 "/InstallDirectory=" $INSTDIR
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /oname=Resolve-LigaseInstallDirectory.ps1 "${ResolverScript}"
FunctionEnd

Section
  ; The raw argv validation rejects duplicate and malformed options.
  StrCpy $4 $INSTDIR
  Call ResolveInstallDirectory
  ${If} $InstallDirectoryOptionPresent == ""
    StrCpy $INSTDIR $4
  ${EndIf}
  ; The selected value validation closes the same boundary as the directory UI.
  StrCpy $InstallParameters '$\"/InstallDirectory=$INSTDIR$\"'
  Call ResolveInstallDirectory
  ${If} $HarnessResultFile == ""
    SetErrorLevel 19
    Quit
  ${EndIf}
  FileOpen $5 "$HarnessResultFile" w
  FileWrite $5 "$INSTDIR"
  FileClose $5
  SetErrorLevel 0
SectionEnd
