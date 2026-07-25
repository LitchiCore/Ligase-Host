[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string] $MakeNsis,
  [Parameter(Mandatory)]
  [string] $OutputRoot
)

$ErrorActionPreference = "Stop"

$root = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $root -Force | Out-Null
$harness = Join-Path $root "LigaseInstallDirectoryHarness.exe"
$harnessDiagnostic = Join-Path $root "harness-runtime.diagnostic"
$resolver = Join-Path $PSScriptRoot "Resolve-LigaseInstallDirectory.ps1"
& $MakeNsis `
  "/DOutputFile=$harness" `
  "/DResolverScript=$resolver" `
  "/DHarnessDiagnosticPath=$harnessDiagnostic" `
  (Join-Path $PSScriptRoot "LigaseInstallDirectoryHarness.nsi") | Out-Null
if ($LASTEXITCODE -ne 0) { throw "harnessCompileFailed" }

function Invoke-Harness(
  [Parameter(Mandatory)][string] $Name,
  [Parameter(Mandatory)][string[]] $Arguments,
  [Parameter(Mandatory)][bool] $ShouldSucceed,
  [string] $Expected = ""
) {
  $result = Join-Path $root "$Name.result"
  if (Test-Path -LiteralPath $result) {
    Remove-Item -LiteralPath $result -Force
  }
  if (Test-Path -LiteralPath $harnessDiagnostic) {
    Remove-Item -LiteralPath $harnessDiagnostic -Force
  }
  $nativeArguments = @($Arguments) + "/ResultFile=$result"
  & $harness @nativeArguments
  $deadline = [DateTime]::UtcNow.AddSeconds(5)
  do {
    Start-Sleep -Milliseconds 50
    $diagnosticLines = if (Test-Path -LiteralPath $harnessDiagnostic) {
      @(Get-Content -LiteralPath $harnessDiagnostic)
    } else {
      @()
    }
    $enoughDiagnostics = if ($ShouldSucceed) {
      $diagnosticLines.Count -ge 2
    } else {
      $diagnosticLines.Count -ge 1
    }
  } until (
    ([DateTime]::UtcNow -ge $deadline) -or
    ($enoughDiagnostics -and (
      -not $ShouldSucceed -or
      (Test-Path -LiteralPath $result -PathType Leaf))))
  $resolverCodes = @($diagnosticLines | ForEach-Object {
    $parts = $_ -split '\|', 3
    if ($parts.Count -ge 2 -and $parts[1] -match '^[0-9]+$') {
      [int]$parts[1]
    }
  })
  $exitCode = if ($resolverCodes.Count -gt 0) {
    $resolverCodes[-1]
  } else {
    255
  }
  $exists = Test-Path -LiteralPath $result -PathType Leaf
  if ($ShouldSucceed) {
    if ($resolverCodes.Count -ne 2 -or
        $resolverCodes.Where({ $_ -ne 0 }).Count -ne 0 -or
        -not $exists) {
      $detail = if (Test-Path -LiteralPath $harnessDiagnostic) {
        [IO.File]::ReadAllText($harnessDiagnostic)
      } else {
        "noDiagnostic"
      }
      throw "harnessPositiveFailed:${Name}:$detail"
    }
    $actual = [IO.File]::ReadAllText($result)
    if (-not $actual.Equals($Expected, [StringComparison]::OrdinalIgnoreCase)) {
      throw "harnessResultMismatch:$Name"
    }
  } elseif ($resolverCodes.Count -lt 1 -or
      $resolverCodes[0] -eq 0 -or
      $exists) {
    throw "harnessNegativeAccepted:$Name"
  }
  [ordered]@{
    name = $Name
    exitCode = $exitCode
    resultCreated = $exists
  }
}

$results = @(
  Invoke-Harness `
    -Name "d-path-with-spaces" `
    -Arguments @('/InstallDirectory=D:\Program Files\Ligase Host') `
    -ShouldSucceed $true `
    -Expected 'D:\Program Files\Ligase Host'
  Invoke-Harness `
    -Name "duplicate" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/InstallDirectory=E:\Ligase Host') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "relative" `
    -Arguments @('/InstallDirectory=relative\Ligase Host') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "drive-root" `
    -Arguments @('/InstallDirectory=D:\') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "unc" `
    -Arguments @('/InstallDirectory=\\server\share\Ligase Host') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "device" `
    -Arguments @('/InstallDirectory=\\?\D:\Ligase Host') `
    -ShouldSucceed $false
)

[ordered]@{
  code = "installDirectoryRuntimeHarnessPassed"
  cases = $results
} | ConvertTo-Json -Depth 4 -Compress
