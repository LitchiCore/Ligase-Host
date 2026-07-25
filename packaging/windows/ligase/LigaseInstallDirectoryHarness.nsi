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
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /oname=Resolve-LigaseInstallDirectory.ps1 "${ResolverScript}"
FunctionEnd

Section
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
SectionEnd
