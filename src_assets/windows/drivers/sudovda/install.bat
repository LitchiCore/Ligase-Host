@echo off
setlocal
pushd %~dp0
if errorlevel 1 exit /b 10

set "CERTUTIL=certutil"
where certutil >nul 2>&1 || set "CERTUTIL=%SystemRoot%\System32\certutil.exe"
set "NEFCON=%~dp0nefconc.exe"
set "ACTION=%LIGASE_VDISPLAY_ACTION%"

if not exist "%NEFCON%" (
  echo LIGASE_VDISPLAY_V1^|stage=toolValidation^|nativeExit=2
  popd
  exit /b 20
)

if "%ACTION%"=="removeOne" goto remove_one_device
if "%ACTION%"=="removeInstance" goto remove_instance
if not "%ACTION%"=="install" (
  echo LIGASE_VDISPLAY_V1^|stage=toolValidation^|nativeExit=87
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

goto create_device

:remove_one_device
"%NEFCON%" --remove-device-node --hardware-id root\sudomaker\sudovda --class-guid "4D36E968-E325-11CE-BFC1-08002BE10318" >nul 2>&1
set "REMOVE_EXIT=%ERRORLEVEL%"
if not "%REMOVE_EXIT%"=="0" (
  echo LIGASE_VDISPLAY_V1^|stage=deviceRemove^|nativeExit=%REMOVE_EXIT%^|removeExit=%REMOVE_EXIT%^|removeCount=0
  popd
  exit /b 25
)
echo LIGASE_VDISPLAY_V1^|stage=deviceRemove^|nativeExit=0^|removeExit=0^|removeCount=1
popd
exit /b 0

:remove_instance
if not exist "%LIGASE_VDISPLAY_PNPUTIL%" (
  echo LIGASE_VDISPLAY_V1^|stage=deviceRemoveFallback^|nativeExit=2^|removeExit=2^|removeCount=0
  popd
  exit /b 26
)
"%LIGASE_VDISPLAY_PNPUTIL%" /remove-device "%LIGASE_VDISPLAY_INSTANCE_ID%" >nul 2>&1
set "REMOVE_EXIT=%ERRORLEVEL%"
if not "%REMOVE_EXIT%"=="0" (
  echo LIGASE_VDISPLAY_V1^|stage=deviceRemoveFallback^|nativeExit=%REMOVE_EXIT%^|removeExit=%REMOVE_EXIT%^|removeCount=0
  popd
  exit /b 26
)
echo LIGASE_VDISPLAY_V1^|stage=deviceRemoveFallback^|nativeExit=0^|removeExit=0^|removeCount=1
popd
exit /b 0

:create_device
"%NEFCON%" --create-device-node --class-name Display --class-guid "4D36E968-E325-11CE-BFC1-08002BE10318" --hardware-id root\sudomaker\sudovda >nul 2>&1
set "STEP_EXIT=%ERRORLEVEL%"
if not "%STEP_EXIT%"=="0" (
  echo LIGASE_VDISPLAY_V1^|stage=deviceCreate^|nativeExit=%STEP_EXIT%^|removeExit=0^|removeCount=0
  popd
  exit /b 23
)

"%NEFCON%" --install-driver --inf-path "SudoVDA.inf" >nul 2>&1
set "STEP_EXIT=%ERRORLEVEL%"
if not "%STEP_EXIT%"=="0" (
  echo LIGASE_VDISPLAY_V1^|stage=driverPackageInstall^|nativeExit=%STEP_EXIT%^|removeExit=0^|removeCount=0
  popd
  exit /b 24
)

echo LIGASE_VDISPLAY_V1^|stage=completed^|nativeExit=0^|removeExit=0^|removeCount=0

popd
exit /b 0
