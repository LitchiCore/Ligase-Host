[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$DesktopDirectory,
  [ValidateRange(5, 60)][int]$TimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"

function Write-Outcome([string]$Code, [bool]$Success) {
  [ordered]@{
    schemaVersion = 1
    code = $Code
    success = $Success
  } | ConvertTo-Json -Compress
}

$desktop = [IO.Path]::GetFullPath($DesktopDirectory)
$sourceExecutable = Join-Path $desktop "Ligase.Host.Desktop.exe"
if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
  Write-Outcome "desktopExecutableMissing" $false
  exit 10
}
if (@(Get-CimInstance Win32_Process | Where-Object {
      $_.Name -eq "Ligase.Host.Desktop.exe" -and
      $_.ExecutablePath -eq $sourceExecutable
    }).Count -ne 0) {
  Write-Outcome "desktopAlreadyRunning" $false
  exit 13
}

$buildRoot = if ([string]::IsNullOrWhiteSpace($env:LIGASE_BUILD_ROOT)) {
  Join-Path ([IO.Path]::GetTempPath()) "LigaseBuild"
} else {
  [IO.Path]::GetFullPath($env:LIGASE_BUILD_ROOT)
}
$gateRoot = Join-Path $buildRoot (
  "temp\desktop-startup-gate-" + [Guid]::NewGuid().ToString("N"))
$workingDirectory = Join-Path $gateRoot "non-install-cwd"
$dataRoot = Join-Path $gateRoot "data"
New-Item -ItemType Directory -Force -Path $workingDirectory, $dataRoot |
  Out-Null
$launchDirectory = Join-Path $gateRoot "payload"
New-Item -ItemType Junction -Path $launchDirectory -Target $desktop |
  Out-Null
$executable = Join-Path $launchDirectory "Ligase.Host.Desktop.exe"

$previousDataRoot = $env:LIGASE_DATA_ROOT
$previousInstanceKey = $env:LIGASE_STARTUP_VALIDATION_INSTANCE_KEY
$process = $null
$outcomeCode = "desktopReadyTimeout"
$outcomeSuccess = $false
$exitCode = 12
try {
  $env:LIGASE_DATA_ROOT = $dataRoot
  $env:LIGASE_STARTUP_VALIDATION_INSTANCE_KEY =
    "Ligase.Host.Desktop.StartupValidation." + [Guid]::NewGuid().ToString("N")
  $process = Start-Process `
    -FilePath $executable `
    -WorkingDirectory $workingDirectory `
    -PassThru
  $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
  do {
    Start-Sleep -Milliseconds 200
    $process.Refresh()
    if ($process.HasExited) {
      $outcomeCode = "desktopExitedBeforeReady"
      $exitCode = 11
      break
    }
  } while (
    [string]::IsNullOrWhiteSpace($process.MainWindowTitle) -and
    [DateTime]::UtcNow -lt $deadline)

  if (-not $process.HasExited) {
    if (-not [string]::IsNullOrWhiteSpace($process.MainWindowTitle) -and
        $process.Responding) {
      $outcomeCode = "desktopStartupReady"
      $outcomeSuccess = $true
      $exitCode = 0
    }
  }
} finally {
  if ($null -ne $process) {
    try {
      if (-not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit(5000)
      }
    } catch {
      # The gate owns only this exact isolated process.
    }
    $process.Dispose()
  }
  foreach ($remaining in @(Get-CimInstance Win32_Process | Where-Object {
      $_.Name -eq "Ligase.Host.Desktop.exe" -and
      ($_.ExecutablePath -eq $executable -or
       $_.ExecutablePath -eq $sourceExecutable)
    })) {
    try {
      Stop-Process -Id $remaining.ProcessId -Force -ErrorAction Stop
      Wait-Process -Id $remaining.ProcessId -Timeout 5 -ErrorAction SilentlyContinue
    } catch {
      # The preflight established that every exact-path process belongs to this gate.
    }
  }
  if ($null -eq $previousDataRoot) {
    Remove-Item Env:LIGASE_DATA_ROOT -ErrorAction SilentlyContinue
  } else {
    $env:LIGASE_DATA_ROOT = $previousDataRoot
  }
  if ($null -eq $previousInstanceKey) {
    Remove-Item Env:LIGASE_STARTUP_VALIDATION_INSTANCE_KEY `
      -ErrorAction SilentlyContinue
  } else {
    $env:LIGASE_STARTUP_VALIDATION_INSTANCE_KEY = $previousInstanceKey
  }
  if (Test-Path -LiteralPath $gateRoot) {
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
      try {
        Remove-Item -LiteralPath $gateRoot -Recurse -Force -ErrorAction Stop
        break
      } catch [IO.IOException] {
        Start-Sleep -Milliseconds 200
      }
    }
  }
}

Write-Outcome $outcomeCode $outcomeSuccess
exit $exitCode
