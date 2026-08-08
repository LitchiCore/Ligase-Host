[CmdletBinding()]
param(
  [Parameter(Mandatory)][string] $MakeNsis,
  [Parameter(Mandatory)][string] $OutputRoot,
  [string] $DotNet = "dotnet.exe",
  [ValidateSet("all")][string] $InstallerProcessCaseFilter = "all",
  [switch] $StopAfterInstallerProcessCases
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($OutputRoot)
if ([IO.Path]::GetPathRoot($root) -cne "D:\") {
  throw "runtimeRootMustBeD"
}
if (Test-Path -LiteralPath $root) {
  if (@(Get-ChildItem -LiteralPath $root -Force).Count -ne 0) {
    throw "harnessOutputRootNotClean"
  }
} else {
  New-Item -ItemType Directory -Path $root | Out-Null
}
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../.."))
if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot ".git") -PathType Container)) {
  throw "sourceRootInvalid"
}

function Get-Sha256([byte[]]$Bytes) {
  $sha = [Security.Cryptography.SHA256]::Create()
  try { return [Convert]::ToHexString($sha.ComputeHash($Bytes)).ToLowerInvariant() }
  finally { $sha.Dispose() }
}

function Write-Utf8Atomic([string]$Path, [string]$Value) {
  $temporary = Join-Path (Split-Path $Path -Parent) (
    "." + [guid]::NewGuid().ToString("N") + ".tmp")
  try {
    $bytes = [Text.UTF8Encoding]::new($false, $true).GetBytes($Value)
    $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew,
      [IO.FileAccess]::Write, [IO.FileShare]::None, 4096,
      [IO.FileOptions]::WriteThrough)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
    [IO.File]::Move($temporary, $Path)
  } finally {
    if (Test-Path -LiteralPath $temporary) {
      Remove-Item -LiteralPath $temporary -Force
    }
  }
}

function ConvertTo-WindowsCommandLineArgument(
  [Parameter(Mandatory)][AllowEmptyString()][string]$Value
) {
  if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
  $builder = [Text.StringBuilder]::new(); [void]$builder.Append('"')
  $backslashes = 0
  foreach ($character in $Value.ToCharArray()) {
    if ($character -eq '\') { $backslashes++; continue }
    if ($character -eq '"') {
      [void]$builder.Append(('\' * (($backslashes * 2) + 1)))
      [void]$builder.Append('"'); $backslashes = 0; continue
    }
    [void]$builder.Append(('\' * $backslashes)); $backslashes = 0
    [void]$builder.Append($character)
  }
  [void]$builder.Append(('\' * ($backslashes * 2))); [void]$builder.Append('"')
  return $builder.ToString()
}

function Invoke-Bounded(
  [string]$FileName, [string[]]$Arguments,
  [hashtable]$Environment = @{}, [int]$DeadlineMilliseconds = 15000
) {
  $start = [Diagnostics.ProcessStartInfo]::new()
  $start.FileName = $FileName
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  $start.Arguments = (($Arguments | ForEach-Object {
        ConvertTo-WindowsCommandLineArgument ([string]$_)
      }) -join " ")
  foreach ($entry in $Environment.GetEnumerator()) {
    $start.Environment[[string]$entry.Key] = [string]$entry.Value
  }
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
  try {
    if (-not $process.Start()) { throw "runtimeProcessStartFailed" }
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($DeadlineMilliseconds)) {
      $process.Kill($true); $process.WaitForExit(2000) | Out-Null
      throw "runtimeProcessDeadlineExceeded"
    }
    if (-not [Threading.Tasks.Task]::WaitAll(
        [Threading.Tasks.Task[]]@($stdout, $stderr), 2000)) {
      throw "runtimePipeDrainFailed"
    }
    $out = $stdout.Result; $err = $stderr.Result
    if ([Text.Encoding]::UTF8.GetByteCount($out) -gt 65536 -or
        [Text.Encoding]::UTF8.GetByteCount($err) -gt 65536) {
      throw "runtimeOutputOverflow"
    }
    return [ordered]@{ exitCode=$process.ExitCode; stdout=$out; stderr=$err }
  } finally { $process.Dispose() }
}

$managePath = Join-Path $PSScriptRoot "Manage-LigaseInstallation.ps1"
$nsisPath = Join-Path $PSScriptRoot "LigaseHost.nsi"
$buildPath = Join-Path $PSScriptRoot "Build-LigaseInstaller.ps1"
$setupSource = Join-Path $sourceRoot "tools/Ligase.VirtualDisplay.Setup/Program.cs"
$requestSchema = Join-Path $sourceRoot (
  "docs/ligase-host/virtual-display-setup-request-v1.schema.json")
$resultSchema = Join-Path $sourceRoot (
  "docs/ligase-host/virtual-display-setup-result-v1.schema.json")
$architecture = Join-Path $sourceRoot (
  "docs/ligase-host/virtual-display-setup-architecture.md")
foreach ($path in @($managePath,$nsisPath,$buildPath,$setupSource,
    $requestSchema,$resultSchema,$architecture)) {
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "runtimeAuthorityMissing"
  }
}

$legacyPatterns = @(
  "VirtualDisplayDiagnostic", "VirtualDisplayInventoryHelper",
  "Invoke-VirtualDisplayNativeRemoval", "Invoke-VirtualDisplayRemovalReconciliation",
  "Get-PnpDeviceProperty", "EncodedCommand", "virtual-display-outcome",
  "finalize-handoff", "CleanupLegacyDriverTrust", "--inventory",
  "--remove-exact", "--validate-remove-fixture")
$productionText = [IO.File]::ReadAllText($managePath) + "`n" +
  [IO.File]::ReadAllText($nsisPath) + "`n" +
  [IO.File]::ReadAllText($buildPath) + "`n" +
  [IO.File]::ReadAllText($setupSource)
foreach ($pattern in $legacyPatterns) {
  if ($productionText.IndexOf($pattern, [StringComparison]::Ordinal) -ge 0) {
    throw "legacyVirtualDisplayAuthorityPresent"
  }
}
$setupText = [IO.File]::ReadAllText($setupSource)
if ($productionText.IndexOf("Ligase.VirtualDisplay.Setup",
      [StringComparison]::Ordinal) -lt 0 -or
    $setupText.IndexOf("DiUninstallDevice",
      [StringComparison]::Ordinal) -lt 0 -or
    $setupText.IndexOf("SetupDiRemoveDevice",
      [StringComparison]::Ordinal) -ge 0) {
  throw "nativeSetupOwnerDrifted"
}

$node = (Get-Command node.exe -ErrorAction Stop).Source
$contractGate = Join-Path $PSScriptRoot "Test-VirtualDisplaySetupContracts.mjs"
$contractRun = Invoke-Bounded $node @($contractGate, $sourceRoot) @{} 30000
if ($contractRun.exitCode -ne 0 -or $contractRun.stderr.Length -ne 0) {
  throw "draft2020ContractGateFailed"
}
$contractLines = @($contractRun.stdout -split "`r?`n" | Where-Object Length)
if ($contractLines.Count -ne 1) { throw "draft2020ContractOutputInvalid" }
$contract = $contractLines[0] | ConvertFrom-Json
if ($contract.code -cne "legacyV1EmptyStoresAddendumPassed" -or
    [int]$contract.topLevelBranches -ne 23 -or
    [int]$contract.positive -lt 18 -or [int]$contract.negative -lt 20 -or
    [int]$contract.ownedHashes -ne 9) {
  throw "draft2020ContractResultInvalid"
}

$consumerRoot = Join-Path $root "result-consumer"
$deployment = Join-Path $consumerRoot "Deployment"
$fixtures = $consumerRoot
New-Item -ItemType Directory -Path $deployment | Out-Null
$resultSchemaSource = Join-Path $sourceRoot (
  "docs/ligase-host/virtual-display-setup-result-v1.schema.json")
$resultSchemaTarget = Join-Path $deployment (
  "virtual-display-setup-result-v1.schema.json")
Copy-Item -LiteralPath $resultSchemaSource -Destination $resultSchemaTarget
$schemaHash = (Get-FileHash -LiteralPath $resultSchemaTarget -Algorithm SHA256).Hash.ToLowerInvariant()
$consumerManifest = [ordered]@{
  schemaVersion=1; installLayout="structured-v1"; platform="x64"
  configuration="Release"; installMode="packaged"; releaseKind="UnsignedDev"
  virtualDisplay=[ordered]@{
    resultSchema="Deployment/virtual-display-setup-result-v1.schema.json"
    resultSchemaSha256=$schemaHash
  }
} | ConvertTo-Json -Depth 5 -Compress
[IO.File]::WriteAllText((Join-Path $consumerRoot "ligase-install-manifest.json"),
  $consumerManifest, [Text.UTF8Encoding]::new($false))
$emit = Invoke-Bounded $node @($contractGate, $sourceRoot,
  "--emit-consumer-fixtures", $fixtures) @{} 30000
if ($emit.exitCode -ne 0 -or $emit.stderr.Length -ne 0) {
  throw "resultConsumerFixtureGenerationFailed"
}
$manage = Join-Path $PSScriptRoot "Manage-LigaseInstallation.ps1"
$consumerEnvironment = @{ LIGASE_INSTALL_VALIDATION_HARNESS="1" }
foreach ($case in @(
    @{name="valid"; accepted=$true},
    @{name="missing-branch"; accepted=$false},
    @{name="contradictory-branch"; accepted=$false})) {
  $run = Invoke-Bounded "powershell.exe" @(
    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $manage,
    "-Action", "ValidateVirtualDisplayResultContract",
    "-InstallDirectory", $consumerRoot, "-ValidationRoot", $consumerRoot,
    "-VirtualDisplayResultPath", (Join-Path $fixtures ($case.name + ".json"))) `
    $consumerEnvironment 30000
  if (($case.accepted -and ($run.exitCode -ne 0 -or
        $run.stdout.Trim() -cne '{"code":"virtualDisplayResultContractAccepted"}')) -or
      (-not $case.accepted -and $run.exitCode -eq 0)) {
    throw "productionResultConsumerGateFailed"
  }
}

$validationHelperRoot = Join-Path (Split-Path $root -Parent) (
  "virtual-display-setup-validation-helper")
$validationArtifacts = Join-Path $root "validation-helper-artifacts"
& $DotNet publish (Join-Path $sourceRoot (
    "tools/Ligase.VirtualDisplay.Setup/Ligase.VirtualDisplay.Setup.csproj")) `
  -c Release -r win-x64 --self-contained true `
  -p:VirtualDisplaySetupValidation=true -p:PublishSingleFile=true `
  -p:UseSharedCompilation=false -p:UseArtifactsOutput=true `
  -p:ArtifactsPath=$validationArtifacts -o $validationHelperRoot | Out-Null
if ($LASTEXITCODE -ne 0) { throw "setupValidationHelperBuildFailed" }
$helper = Join-Path $validationHelperRoot "Ligase.VirtualDisplay.Setup.exe"
if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) {
  throw "setupValidationHelperMissing"
}
$runnerRoot = Join-Path $root "runner-consumer"
$runnerDeployment = Join-Path $runnerRoot "Deployment"
New-Item -ItemType Directory -Path $runnerDeployment | Out-Null
$runnerHelper = Join-Path $runnerDeployment "Ligase.VirtualDisplay.Setup.exe"
Copy-Item -LiteralPath $helper -Destination $runnerHelper
Copy-Item -LiteralPath $resultSchemaSource -Destination (Join-Path $runnerDeployment (
  "virtual-display-setup-result-v1.schema.json"))
$runnerHelperHash = (Get-FileHash $runnerHelper -Algorithm SHA256).Hash.ToLowerInvariant()
$runnerManifest = [ordered]@{
  schemaVersion=1; installLayout="structured-v1"; sourceHead=('3' * 40)
  platform="x64"; configuration="Release"; installMode="packaged"
  releaseKind="UnsignedDev"; artifacts=@()
  privilegedHelpers=@([ordered]@{
    relativePath="Deployment/Ligase.VirtualDisplay.Setup.exe"
    signedArtifactSha256=$runnerHelperHash
  })
  virtualDisplay=[ordered]@{
    setupHelper="Deployment/Ligase.VirtualDisplay.Setup.exe"
    resultSchema="Deployment/virtual-display-setup-result-v1.schema.json"
    resultSchemaSha256=$schemaHash; packageSha256=('4' * 64)
  }
} | ConvertTo-Json -Depth 7 -Compress
[IO.File]::WriteAllText((Join-Path $runnerRoot "ligase-install-manifest.json"),
  $runnerManifest, [Text.UTF8Encoding]::new($false))
$runnerPassed = 0
foreach ($fault in @("timeout", "overflow", "dualPipePending",
    "startRetain", "schemaInvalidSuccess")) {
  $fixtureMode = if ($fault -ceq "startRetain") { "timeout" } else { $fault }
  $environment = @{
    LIGASE_INSTALL_VALIDATION_HARNESS="1"
    LIGASE_VDISPLAY_RUNNER_VALIDATION="1"
    LIGASE_VDISPLAY_RUNNER_FAULT=$fault
    LIGASE_VDISPLAY_CALLER_FIXTURE=$fixtureMode
  }
  $run = Invoke-Bounded "powershell.exe" @(
    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $manage,
    "-Action", "InstallVirtualDisplay", "-InstallDirectory", $runnerRoot,
    "-ValidationRoot", $runnerRoot) $environment 10000
  if ($run.exitCode -ne 20 -or
      $run.stdout.Trim().IndexOf('"success":false',
        [StringComparison]::Ordinal) -lt 0) {
    throw "productionRunnerFaultGateFailed"
  }
  $runnerPassed++
}
$sequenceEnvironment = @{
  LIGASE_INSTALL_VALIDATION_HARNESS="1"
  LIGASE_VDISPLAY_RUNNER_VALIDATION="1"
  LIGASE_VDISPLAY_CALLER_FIXTURE="verifiedProvisionFailure"
}
$provisionSequence = Invoke-Bounded "powershell.exe" @(
  "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $manage,
  "-Action", "InstallVirtualDisplay", "-InstallDirectory", $runnerRoot,
  "-ValidationRoot", $runnerRoot) $sequenceEnvironment 10000
if ($provisionSequence.exitCode -ne 20) {
  throw "virtualDisplayOutcomePrimaryFixtureFailed"
}
$sequenceEnvironment.LIGASE_VDISPLAY_CALLER_FIXTURE =
  "verifiedUninstallRecovery"
$uninstallSequence = Invoke-Bounded "powershell.exe" @(
  "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $manage,
  "-Action", "UninstallVirtualDisplay", "-InstallDirectory", $runnerRoot,
  "-ValidationRoot", $runnerRoot) $sequenceEnvironment 10000
if ($uninstallSequence.exitCode -ne 0) {
  throw "virtualDisplayOutcomeRecoveryFixtureFailed"
}
$setupOutcomePath = Join-Path $runnerRoot "last-outcome.json"
$setupOutcomeRaw = [IO.File]::ReadAllText($setupOutcomePath)
$setupOutcome = $setupOutcomeRaw | ConvertFrom-Json
if ($setupOutcome.phase -cne "virtualDisplaySetup" -or
    $setupOutcome.success -ne $false -or
    $setupOutcome.resultCode -cne "ownershipReadFailed" -or
    $setupOutcome.failedField -cne "virtualDisplay" -or
    $setupOutcome.components.virtualDisplay -cne "failed" -or
    $setupOutcome.virtualDisplaySetup.primary.operation -cne "provision" -or
    $setupOutcome.virtualDisplaySetup.primary.code -cne "ownershipReadFailed" -or
    $setupOutcome.virtualDisplaySetup.primary.stage -cne "readOwnership" -or
    -not [bool]$setupOutcome.virtualDisplaySetup.primary.firstFailureFrozen -or
    $setupOutcome.virtualDisplaySetup.recovery.operation -cne "uninstall" -or
    $setupOutcome.virtualDisplaySetup.recovery.code -cne
      "uninstalledLegacyPackageRetained" -or
    $setupOutcome.virtualDisplaySetup.recovery.stage -cne "completed" -or
    [string]$setupOutcome.virtualDisplaySetup.primary.resultFileSha256 -notmatch
      '^[0-9a-f]{64}$' -or
    [string]$setupOutcome.virtualDisplaySetup.recovery.resultFileSha256 -notmatch
      '^[0-9a-f]{64}$') {
  throw "virtualDisplayOutcomeSequenceInvalid"
}
if (@(Get-ChildItem -LiteralPath $runnerRoot -Force -Filter (
    ".last-outcome-*.tmp")).Count -ne 0) {
  throw "virtualDisplayOutcomeTemporaryResidue"
}
$runnerProcesses = @(Get-CimInstance Win32_Process | Where-Object {
  [string]$_.ExecutablePath -ceq $runnerHelper })
if ($runnerProcesses.Count -ne 0) { throw "productionRunnerProcessResidue" }
$fixtureCases = @(
  @{ name="inventoryZero"; count=0; present=0; bound=0 },
  @{ name="inventoryOne"; count=1; present=1; bound=1 },
  @{ name="inventoryPhantom"; count=1; present=0; bound=0 },
  @{ name="inventoryTwo"; count=2; present=1; bound=1 })
$inventoryPassed = 0
foreach ($fixtureCase in $fixtureCases) {
  $run = Invoke-Bounded $helper @(
    "--validate-contract-fixture",$fixtureCase.name) @{}
  $lines = @($run.stdout -split "`r?`n" | Where-Object Length)
  if ($run.exitCode -ne 0 -or $run.stderr.Length -ne 0 -or $lines.Count -ne 1) {
    throw "setupInventoryFixtureFailed"
  }
  $projection = $lines[0] | ConvertFrom-Json
  if ($projection.state -cne "available" -or
      [int]$projection.count -ne [int]$fixtureCase.count -or
      [int]$projection.present -ne [int]$fixtureCase.present -or
      [int]$projection.bound -ne [int]$fixtureCase.bound) {
    throw "setupInventoryFixtureProjectionInvalid"
  }
  $inventoryPassed++
}
$removeRun = Invoke-Bounded $helper @(
  "--validate-contract-fixture","removalLegal") @{}
$removeLines = @($removeRun.stdout -split "`r?`n" | Where-Object Length)
if ($removeRun.exitCode -ne 0 -or $removeRun.stderr.Length -ne 0 -or
    $removeLines.Count -ne 1) { throw "setupRemovalFixtureFailed" }
$removeProjection = $removeLines[0] | ConvertFrom-Json
if ($removeProjection.state -cne "removed" -or
    -not [bool]$removeProjection.strictDecrease) {
  throw "setupRemovalTupleInvalid"
}
$driftRun = Invoke-Bounded $helper @(
  "--validate-contract-fixture","authorityDrift") @{}
if ($driftRun.exitCode -eq 0) { throw "setupRemovalAuthorityDriftAccepted" }

if (@(Get-ChildItem -LiteralPath $root -Force -Filter ".*.tmp").Count -ne 0) {
  throw "runtimeTemporaryResidue"
}

[ordered]@{
  code = "installDirectoryRuntimeHarnessPassed"
  draft2020 = [ordered]@{
    requestCompiled = $true
    resultCompiled = $true
    topLevelBranches = [int]$contract.topLevelBranches
    positive = [int]$contract.positive
    negative = [int]$contract.negative
    certificateOwnershipHashes = [int]$contract.ownedHashes
  }
  productionResultConsumer = [ordered]@{
    accepted = 1
    rejected = 2
    schemaPinned = $true
  }
  productionRunner = [ordered]@{
    faultCases = $runnerPassed
    processZero = $true
    boundedPipes = $true
    retainedStartClosed = $true
  }
  nativeSetup = [ordered]@{
    inventoryFixtures = $inventoryPassed
    legalRemovalTuple = $true
    authorityDriftRejected = $true
    processStartCount = ($inventoryPassed + 2)
    systemMutation = $false
  }
  legacyProductionReferences = 0
  temporaryResidue = 0
} | ConvertTo-Json -Depth 6 -Compress
$global:LASTEXITCODE = 0
