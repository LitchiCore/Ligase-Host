@echo off
setlocal
pushd %~dp0
if errorlevel 1 exit /b 10

set "CERTUTIL=certutil"
where certutil >nul 2>&1 || set "CERTUTIL=%SystemRoot%\System32\certutil.exe"
set "NEFCON=%~dp0nefconc.exe"

if not exist "%NEFCON%" (
  echo LIGASE_VDISPLAY_V1^|stage=toolValidation^|nativeExit=2
  popd
  exit /b 20
)

"%CERTUTIL%" -addstore -f root "sudovda.cer" >nul 2>&1
set "STEP_EXIT=%ERRORLEVEL%"
if not "%STEP_EXIT%"=="0" (
  echo LIGASE_VDISPLAY_V1^|stage=certificateRoot^|nativeExit=%STEP_EXIT%
  popd
  exit /b 21
)

"%CERTUTIL%" -addstore -f TrustedPublisher "sudovda.cer" >nul 2>&1
set "STEP_EXIT=%ERRORLEVEL%"
if not "%STEP_EXIT%"=="0" (
  echo LIGASE_VDISPLAY_V1^|stage=certificatePublisher^|nativeExit=%STEP_EXIT%
  popd
  exit /b 22
)

set "REMOVE_COUNT=0"
:remove_existing_device
if "%REMOVE_COUNT%"=="16" (
  echo LIGASE_VDISPLAY_V1^|stage=deviceRemove^|nativeExit=16^|removeExit=0^|removeCount=%REMOVE_COUNT%
  popd
  exit /b 25
)
"%NEFCON%" --remove-device-node --hardware-id root\sudomaker\sudovda --class-guid "4D36E968-E325-11CE-BFC1-08002BE10318" >nul 2>&1
set "REMOVE_EXIT=%ERRORLEVEL%"
if not "%REMOVE_EXIT%"=="0" goto removed_all_devices
set /a REMOVE_COUNT+=1
goto remove_existing_device

:removed_all_devices

"%NEFCON%" --create-device-node --class-name Display --class-guid "4D36E968-E325-11CE-BFC1-08002BE10318" --hardware-id root\sudomaker\sudovda >nul 2>&1
set "STEP_EXIT=%ERRORLEVEL%"
if not "%STEP_EXIT%"=="0" (
  echo LIGASE_VDISPLAY_V1^|stage=deviceCreate^|nativeExit=%STEP_EXIT%^|removeExit=%REMOVE_EXIT%^|removeCount=%REMOVE_COUNT%
  popd
  exit /b 23
)

"%NEFCON%" --install-driver --inf-path "SudoVDA.inf" >nul 2>&1
set "STEP_EXIT=%ERRORLEVEL%"
if not "%STEP_EXIT%"=="0" (
  echo LIGASE_VDISPLAY_V1^|stage=driverPackageInstall^|nativeExit=%STEP_EXIT%^|removeExit=%REMOVE_EXIT%^|removeCount=%REMOVE_COUNT%
  popd
  exit /b 24
)

echo LIGASE_VDISPLAY_V1^|stage=completed^|nativeExit=0^|removeExit=%REMOVE_EXIT%^|removeCount=%REMOVE_COUNT%

popd
exit /b 0
