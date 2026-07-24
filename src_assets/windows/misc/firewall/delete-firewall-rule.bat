@echo off
for %%I in ("%~dp0\..") do set "ROOT_DIR=%%~fI"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Manage-LigaseFirewall.ps1" -Action Remove -Manifest "%~dp0ligase-firewall-v1.json" -Program "%ROOT_DIR%\sunshine.exe" -BasePort 48989
exit /b %ERRORLEVEL%
