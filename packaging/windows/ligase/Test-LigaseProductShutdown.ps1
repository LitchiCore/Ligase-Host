param(
  [Parameter(Mandatory = $true)][string]$DotNetPath,
  [Parameter(Mandatory = $true)][string]$SourceRoot,
  [switch]$CleanupOwnedResidueOnly
)

$ErrorActionPreference = "Stop"
$source = [IO.Path]::GetFullPath($SourceRoot)
$project = Join-Path $source "tests\Ligase.Shutdown.Fixture\Ligase.Shutdown.Fixture.csproj"
$buildRoot = Join-Path "D:\Development\Ligase\Build" (
  "shutdown-fixture-build-" + [Guid]::NewGuid().ToString("N"))
if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $project -PathType Leaf)) {
  throw "shutdownFixtureInputInvalid"
}

if ($CleanupOwnedResidueOnly) {
  $owned = @(Get-ChildItem "D:\Development\Ligase\Build" -Directory |
    Where-Object { $_.Name -like "shutdown-fixture-build-*" -or
      $_.Name -like "shutdown-protocol-*" -or
      $_.Name -like "full-test-temp-legacy-force-*" -or
      $_.Name -like "full-test-temp-live-eligibility-*" -or
      $_.Name -eq "legacy-force-compile-6f5c42d94a8a45a6b93f9be5fd739d49" })
  $running = @(Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -like "D:\Development\Ligase\Build\shutdown-*" -or
    $_.ExecutablePath -like "D:\Development\Ligase\Build\legacy-force-*" })
  if ($running.Count -ne 0) { throw "shutdownFixtureCleanupProcessRunning" }
  foreach ($root in $owned) {
    Remove-Item -LiteralPath $root.FullName -Recurse -Force
  }
  $absence1 = @(Get-ChildItem "D:\Development\Ligase\Build" -Directory |
    Where-Object { $_.Name -like "shutdown-fixture-build-*" -or
      $_.Name -like "shutdown-protocol-*" -or
      $_.Name -like "full-test-temp-legacy-force-*" -or
      $_.Name -like "full-test-temp-live-eligibility-*" -or
      $_.Name -eq "legacy-force-compile-6f5c42d94a8a45a6b93f9be5fd739d49" }).Count
  Start-Sleep -Milliseconds 100
  $absence2 = @(Get-ChildItem "D:\Development\Ligase\Build" -Directory |
    Where-Object { $_.Name -like "shutdown-fixture-build-*" -or
      $_.Name -like "shutdown-protocol-*" -or
      $_.Name -like "full-test-temp-legacy-force-*" -or
      $_.Name -like "full-test-temp-live-eligibility-*" -or
      $_.Name -eq "legacy-force-compile-6f5c42d94a8a45a6b93f9be5fd739d49" }).Count
  if ($absence1 -ne 0 -or $absence2 -ne 0) {
    throw "shutdownFixtureCleanupAbsenceUnproven"
  }
  [ordered]@{ code="shutdownFixtureOwnedResidueRemoved"; removed=$owned.Count
    absence1=0; absence2=0 } | ConvertTo-Json -Compress
  return
}

New-Item -ItemType Directory -Path $buildRoot | Out-Null
$fixtureRootsBefore = @((Get-ChildItem -LiteralPath "D:\Development\Ligase\Build" `
  -Directory | Where-Object Name -Like "shutdown-protocol-*" | ForEach-Object FullName))
try {
  $bin = Join-Path $buildRoot "bin\"
  $obj = Join-Path $buildRoot "obj\"
  & $DotNetPath build $project --configuration Debug -m:1 `
    -p:BaseOutputPath=$bin -p:BaseIntermediateOutputPath=$obj
  if ($LASTEXITCODE -ne 0) { throw "shutdownFixtureBuildFailed" }
  $fixture = Join-Path $bin (
    "Debug\net8.0-windows\Ligase.Shutdown.Fixture.exe")
  if (-not (Test-Path -LiteralPath $fixture -PathType Leaf)) {
    throw "shutdownFixtureExecutableMissing"
  }

  $results = @()
  foreach ($case in @(
      "success", "legacyReordered", "legacyCleanupFault", "ackUnavailable", "terminalFailure",
      "terminalEof", "residual", "forceRequired", "forceNoConsent",
      "forceNoManifest", "forceManifestDrift", "forceExtraLocker",
      "forceNative351", "forceTimeout", "forcePermission", "forceResidue", "stale")) {
    $savedErrorActionPreference = $ErrorActionPreference
    try {
      $ErrorActionPreference = "Continue"
      $output = @(& $fixture --mode orchestrate --sourceRoot $source --case $case 2>&1)
      $fixtureExit = $LASTEXITCODE
    } finally {
      $ErrorActionPreference = $savedErrorActionPreference
    }
    if ($fixtureExit -ne 0) {
      throw "shutdownFixtureCaseFailed:${case}:$($output -join [Environment]::NewLine)"
    }
    $newRoots = @(Get-ChildItem -LiteralPath "D:\Development\Ligase\Build" `
      -Directory | Where-Object Name -Like "shutdown-protocol-*" | Where-Object {
        $_.FullName -notin $fixtureRootsBefore
      })
    foreach ($root in $newRoots) {
      $deadline = [DateTime]::UtcNow.AddSeconds(5)
      do {
        try {
          Remove-Item -LiteralPath $root.FullName -Recurse -Force
          break
        } catch {
          if ([DateTime]::UtcNow -ge $deadline) { throw }
          Start-Sleep -Milliseconds 50
        }
      } while ($true)
    }
    $results += ([string]$output[-1] | ConvertFrom-Json)
  }

  $remaining = @(Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -like "D:\Development\Ligase\Build\shutdown-protocol-*"
  })
  if ($remaining.Count -ne 0) { throw "shutdownFixtureProcessResidue" }
  $roots = @(Get-ChildItem -LiteralPath "D:\Development\Ligase\Build" -Directory |
    Where-Object Name -Like "shutdown-protocol-*" | Where-Object {
      $_.FullName -notin $fixtureRootsBefore
    })
  if ($roots.Count -ne 0) { throw "shutdownFixtureFileResidue" }
  [ordered]@{
    code = "shutdownProtocolFixturePassed"
    cases = @($results).Count
    acknowledged = [bool]$results[0].acknowledged
    legacyReorderedAccepted = [bool]$results[1].acknowledged
    legacyCleanupFaultRecovered = [bool]$results[2].restartManagerCompatibility
    launcherStarts = [int]$results[0].launcherStarts
    ackUnavailableRecoveredByRestartManager = [bool]$results[3].restartManagerCompatibility
    terminalFailureRecoveredByRestartManager = [bool]$results[4].restartManagerCompatibility
    terminalEofRecoveredByRestartManager = [bool]$results[5].restartManagerCompatibility
    residualRecoveredByRestartManager = [bool]$results[6].restartManagerCompatibility
    forceUsed = [bool]$results[7].forceUsed
    forceNegatives = @($results | Where-Object forceRejected).Count
    staleIgnored = [bool]$results[-1].staleIgnored
    processResidue = 0
    fileResidue = 0
  } | ConvertTo-Json -Compress
} finally {
  if (Test-Path -LiteralPath $buildRoot -PathType Container) {
    Remove-Item -LiteralPath $buildRoot -Recurse -Force
  }
}
