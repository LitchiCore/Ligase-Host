Function ResolveInstallDirectory
  StrCpy $9 $InstallParameters
  System::Call 'kernel32::SetEnvironmentVariableW(w "LIGASE_INSTALL_RAW_PARAMETERS", w r9)'
  ReadRegStr $2 HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host" "InstallLocation"
  StrCpy $8 $2
  System::Call 'kernel32::SetEnvironmentVariableW(w "LIGASE_INSTALL_REGISTERED_LOCATION", w r8)'
  StrCpy $7 "$PROGRAMFILES64\Ligase Host"
  System::Call 'kernel32::SetEnvironmentVariableW(w "LIGASE_INSTALL_DEFAULT_LOCATION", w r7)'
  ; NSIS extracts a native plugin named System.dll into $PLUGINSDIR. Running
  ; Add-Type from that directory shadows the .NET reference assembly.
  SetOutPath "$TEMP"
  nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\Resolve-LigaseInstallDirectory.ps1"'
  Pop $0
  Pop $1
  !ifdef LIGASE_VALIDATION_HARNESS
    FileOpen $3 "${HarnessDiagnosticPath}" a
    FileSeek $3 0 END
    FileWrite $3 "$InstallParameters|$0|$1$\r$\n"
    FileClose $3
  !endif
  System::Call 'kernel32::SetEnvironmentVariableW(w "LIGASE_INSTALL_RAW_PARAMETERS", p 0)'
  System::Call 'kernel32::SetEnvironmentVariableW(w "LIGASE_INSTALL_REGISTERED_LOCATION", p 0)'
  System::Call 'kernel32::SetEnvironmentVariableW(w "LIGASE_INSTALL_DEFAULT_LOCATION", p 0)'
  ${If} $0 != 0
    !ifdef LIGASE_VALIDATION_HARNESS
      SetErrorLevel $0
      Quit
    !else
      MessageBox MB_OK|MB_ICONSTOP "The installation directory is invalid. Choose an absolute local folder below a drive root."
      Abort
    !endif
  ${EndIf}
  StrCpy $INSTDIR $1
FunctionEnd
