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
  try {
    return [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace(
      "-", "").ToLowerInvariant()
  }
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
$nsisCapturePath = Join-Path $PSScriptRoot "Invoke-NsisCompiler.ps1"
$setupSource = Join-Path $sourceRoot "tools/Ligase.VirtualDisplay.Setup/Program.cs"
$requestSchema = Join-Path $sourceRoot (
  "docs/ligase-host/virtual-display-setup-request-v1.schema.json")
$resultSchema = Join-Path $sourceRoot (
  "docs/ligase-host/virtual-display-setup-result-v1.schema.json")
$architecture = Join-Path $sourceRoot (
  "docs/ligase-host/virtual-display-setup-architecture.md")
foreach ($path in @($managePath,$nsisPath,$buildPath,$nsisCapturePath,$setupSource,
    $requestSchema,$resultSchema,$architecture)) {
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "runtimeAuthorityMissing"
  }
}

$nsisEvidenceRoot = Join-Path $root "nsis-evidence-cases"
New-Item -ItemType Directory -Path $nsisEvidenceRoot | Out-Null
$fakeCompiler = Join-Path $nsisEvidenceRoot "controlled-makensis.cmd"
[IO.File]::WriteAllText($fakeCompiler, @'
@echo off
if "%~1"=="/VERSION" (
  echo vControlled-1
  exit /b 0
)
echo controlled stdout
echo controlled compiler error 1>&2
exit /b 37
'@, [Text.Encoding]::ASCII)
$failureEvidence = Join-Path $nsisEvidenceRoot "failure"
$failureDriver = Join-Path $nsisEvidenceRoot "failure-driver.ps1"
[IO.File]::WriteAllText($failureDriver, @"
`$result = & '$nsisCapturePath' -MakeNsis '$fakeCompiler' -CompilerArguments @('fixture.nsi') -WorkingDirectory '$nsisEvidenceRoot' -EvidenceDirectory '$failureEvidence'
if (`$result.code -cne 'nsisCompilerEvidenceCaptured') { exit 98 }
[IO.File]::WriteAllText('$failureEvidence\caller-consumed.marker', [string]`$result.exitCode)
exit [int]`$result.exitCode
"@, [Text.UTF8Encoding]::new($false))
$failureRun = Invoke-Bounded "powershell.exe" @(
  "-NoLogo","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass",
  "-File",$failureDriver) @{}
if ($failureRun.exitCode -ne 37 -or
    $failureRun.stderr -notmatch 'controlled compiler error' -or
    (Get-Content -Raw -LiteralPath (
      Join-Path $failureEvidence "caller-consumed.marker")) -cne "37") {
  throw "nsisControlledFailureNotPreserved"
}
$failureManifest = Get-Content -Raw -LiteralPath (
  Join-Path $failureEvidence "makensis-result.json") | ConvertFrom-Json
if ($failureManifest.exitCode -ne 37 -or
    $failureManifest.version -cne "vControlled-1" -or
    -not [bool]$failureManifest.stdout.closed -or
    -not [bool]$failureManifest.stderr.closed -or
    (Get-FileHash -Algorithm SHA256 -LiteralPath (
      Join-Path $failureEvidence "makensis.stderr.log")).Hash.ToLowerInvariant() -cne
      [string]$failureManifest.stderr.sha256) {
  throw "nsisControlledFailureEvidenceInvalid"
}
$successScript = Join-Path $nsisEvidenceRoot "success.nsi"
$successArtifact = Join-Path $nsisEvidenceRoot "success.exe"
[IO.File]::WriteAllText($successScript, @"
Unicode true
Name "Ligase NSIS Evidence Fixture"
OutFile "$successArtifact"
Section
SectionEnd
"@, [Text.UTF8Encoding]::new($false))
$successEvidence = Join-Path $nsisEvidenceRoot "success"
$successDriver = Join-Path $nsisEvidenceRoot "success-driver.ps1"
[IO.File]::WriteAllText($successDriver, @"
`$result = & '$nsisCapturePath' -MakeNsis '$MakeNsis' -CompilerArguments @('/INPUTCHARSET','UTF8','$successScript') -WorkingDirectory '$nsisEvidenceRoot' -EvidenceDirectory '$successEvidence'
if (`$result.code -cne 'nsisCompilerEvidenceCaptured' -or [int]`$result.exitCode -ne 0) { exit 99 }
[IO.File]::WriteAllText('$successEvidence\caller-continued.marker', 'signatureValidationReachable')
exit 0
"@, [Text.UTF8Encoding]::new($false))
$successRun = Invoke-Bounded "powershell.exe" @(
  "-NoLogo","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass",
  "-File",$successDriver) @{}
if ($successRun.exitCode -ne 0 -or -not (Test-Path -LiteralPath $successArtifact) -or
    (Get-Content -Raw -LiteralPath (
      Join-Path $successEvidence "caller-continued.marker")) -cne
      "signatureValidationReachable") {
  throw "nsisRealSuccessEvidenceFailed"
}
$successManifest = Get-Content -Raw -LiteralPath (
  Join-Path $successEvidence "makensis-result.json") | ConvertFrom-Json
if ($successManifest.exitCode -ne 0 -or
    -not [bool]$successManifest.stdout.closed -or
    -not [bool]$successManifest.stderr.closed -or
    [string]::IsNullOrWhiteSpace([string]$successManifest.version)) {
  throw "nsisRealSuccessEvidenceInvalid"
}
$nsisResidue = @(Get-ChildItem -LiteralPath $nsisEvidenceRoot -Recurse -File |
  Where-Object { $_.Name -match '\.(tmp|bak)$' })
if ($nsisResidue.Count -ne 0) { throw "nsisEvidenceResidue" }

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
$nsisText = [IO.File]::ReadAllText($nsisPath)
$uninstallMatches = [Text.RegularExpressions.Regex]::Matches(
  $nsisText, '(?m)^Section\s+"Uninstall"\s*$')
$localizedUninstallMatches = [Text.RegularExpressions.Regex]::Matches(
  $nsisText, '(?m)^Section\s+"卸载"\s*$')
$uninstallBlock = [Text.RegularExpressions.Regex]::Match(
  $nsisText, '(?ms)^Section\s+"Uninstall"\s*\r?\n(?<body>.*?)^SectionEnd\s*$')
if ($uninstallMatches.Count -ne 1 -or
    $localizedUninstallMatches.Count -ne 0 -or
    -not $uninstallBlock.Success -or
    $uninstallBlock.Groups['body'].Value.IndexOf(
      '-Action UninstallVirtualDisplay', [StringComparison]::Ordinal) -lt 0 -or
    $uninstallBlock.Groups['body'].Value.IndexOf(
      '-Action Uninstall ', [StringComparison]::Ordinal) -lt 0 -or
    $uninstallBlock.Groups['body'].Value.IndexOf(
      'DeleteRegKey HKLM', [StringComparison]::Ordinal) -lt 0 -or
    $uninstallBlock.Groups['body'].Value.IndexOf(
      'RMDir /r "$INSTDIR"', [StringComparison]::Ordinal) -lt 0) {
  throw "reservedUninstallSourceGateFailed"
}
$uninstallDeclaration = $uninstallMatches[0].Value
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
$windowsPowerShell = Join-Path $env:SystemRoot (
  "System32\WindowsPowerShell\v1.0\powershell.exe")
if (-not (Test-Path -LiteralPath $windowsPowerShell -PathType Leaf)) {
  throw "windowsPowerShell51Missing"
}
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
$recordEnvironment = @{ LIGASE_INSTALL_VALIDATION_HARNESS="1" }
$recordRun = Invoke-Bounded $windowsPowerShell @(
  "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", $manage,
  "-Action", "RecordEvidence", "-InstallDirectory", $runnerRoot,
  "-ValidationRoot", $runnerRoot, "-EvidencePhase", "initialized",
  "-EvidenceResultCode", "notStarted") $recordEnvironment 10000
$recordLines = @($recordRun.stdout -split "`r?`n" | Where-Object Length)
if ($recordRun.exitCode -ne 0 -or $recordRun.stderr.Length -ne 0 -or
    $recordLines.Count -ne 1 -or
    $recordLines[0] -cne ('{"code":"installerEvidenceRecorded","success":true,' +
      '"phase":"initialized","resultCode":"notStarted"}')) {
  throw "installerEvidenceAcknowledgementInvalid"
}
$recordAck = $recordLines[0] | ConvertFrom-Json
if (@($recordAck).Count -ne 1 -or
    $recordAck.code -cne "installerEvidenceRecorded" -or
    -not [bool]$recordAck.success -or $recordAck.phase -cne "initialized" -or
    $recordAck.resultCode -cne "notStarted") {
  throw "installerEvidenceAcknowledgementShapeInvalid"
}
$recordPath = Join-Path $runnerRoot "last-outcome.json"
$recordBytes = [IO.File]::ReadAllBytes($recordPath)
$recordHash = Get-Sha256 $recordBytes
$recordReadback = [IO.File]::ReadAllBytes($recordPath)
if ((Get-Sha256 $recordReadback) -cne $recordHash -or
    ([Text.UTF8Encoding]::new($false, $true).GetString($recordReadback) |
      ConvertFrom-Json).phase -cne "initialized" -or
    @(Get-ChildItem -LiteralPath $runnerRoot -Force -Filter (
      ".last-outcome-*.tmp")).Count -ne 0 -or
    @(Get-ChildItem -LiteralPath $runnerRoot -Force -Filter (
      ".last-outcome-backup-*.tmp")).Count -ne 0) {
  throw "installerEvidenceAtomicReadbackInvalid"
}
$duplicateFixturePath = Join-Path $root "duplicate-pipeline-fixture.ps1"
try {
  [IO.File]::WriteAllText($duplicateFixturePath, @'
$a = @(
  [pscustomobject]@{ phase="initialized"; resultCode="notStarted" },
  [pscustomobject]@{ phase="initialized"; resultCode="notStarted" })
@{
  count=@($a).Count
  phase=[string]$a.phase
  resultCode=[string]$a.resultCode
} | ConvertTo-Json -Compress
'@, [Text.UTF8Encoding]::new($false))
  $duplicateRun = Invoke-Bounded $windowsPowerShell @(
    "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
    "-File", $duplicateFixturePath) @{} 10000
  $duplicateProjection = $duplicateRun.stdout | ConvertFrom-Json
  if ($duplicateRun.exitCode -ne 0 -or $duplicateRun.stderr.Length -ne 0 -or
      [int]$duplicateProjection.count -ne 2 -or
      $duplicateProjection.phase -cne "initialized initialized" -or
      $duplicateProjection.resultCode -cne "notStarted notStarted") {
    throw "installerEvidenceDuplicatePipelineFixtureInvalid"
  }
} finally {
  if (Test-Path -LiteralPath $duplicateFixturePath) {
    Remove-Item -LiteralPath $duplicateFixturePath -Force
  }
}
$faultRun = Invoke-Bounded $windowsPowerShell @(
  "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", $manage,
  "-Action", "RecordEvidence", "-InstallDirectory", $runnerRoot,
  "-ValidationRoot", $runnerRoot, "-EvidenceResultCode", "invalid-value") `
  $recordEnvironment 10000
if ($faultRun.exitCode -eq 0 -or
    $faultRun.stdout.IndexOf('"success":true', [StringComparison]::Ordinal) -ge 0) {
  throw "installerEvidenceWriterFaultReportedSuccess"
}
$uninstallSelectionRun = Invoke-Bounded $windowsPowerShell @(
  "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", $manage,
  "-Action", "RecordEvidence", "-InstallDirectory", $runnerRoot,
  "-ValidationRoot", $runnerRoot, "-EvidencePhase", "uninstalling",
  "-EvidenceSuccess", "unknown", "-EvidenceResultCode", "uninstallStarted",
  "-EvidenceUninstallDisposition", "Quarantine", "-EvidenceUninstallState",
  "pending") $recordEnvironment 10000
if ($uninstallSelectionRun.exitCode -ne 0 -or
    $uninstallSelectionRun.stdout.Trim() -cne
      '{"code":"installerEvidenceRecorded","success":true,"phase":"uninstalling","resultCode":"uninstallStarted"}') {
  throw "uninstallSelectionEvidenceInvalid"
}
$selectionDocument = [IO.File]::ReadAllText($recordPath) | ConvertFrom-Json
if ($selectionDocument.phase -cne "uninstalling" -or
    $selectionDocument.uninstall.disposition -cne "Quarantine" -or
    $selectionDocument.uninstall.state -cne "pending") {
  throw "uninstallSelectionEvidenceReadbackInvalid"
}
$uninstallResultRun = Invoke-Bounded $windowsPowerShell @(
  "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", $manage,
  "-Action", "RecordEvidence", "-InstallDirectory", $runnerRoot,
  "-ValidationRoot", $runnerRoot, "-EvidencePhase", "uninstalled",
  "-EvidenceSuccess", "true", "-EvidenceResultCode", "uninstalled",
  "-EvidenceUninstallDisposition", "Quarantine", "-EvidenceUninstallState",
  "quarantined") $recordEnvironment 10000
if ($uninstallResultRun.exitCode -ne 0 -or
    $uninstallResultRun.stdout.Trim() -cne
      '{"code":"installerEvidenceRecorded","success":true,"phase":"uninstalled","resultCode":"uninstalled"}') {
  throw "uninstallResultEvidenceInvalid"
}
$resultDocument = [IO.File]::ReadAllText($recordPath) | ConvertFrom-Json
if ($resultDocument.phase -cne "uninstalled" -or
    $resultDocument.success -ne $true -or
    $resultDocument.uninstall.disposition -cne "Quarantine" -or
    $resultDocument.uninstall.state -cne "quarantined" -or
    @(Get-ChildItem -LiteralPath $runnerRoot -Force -Filter (
      ".last-outcome-*.tmp")).Count -ne 0 -or
    @(Get-ChildItem -LiteralPath $runnerRoot -Force -Filter (
      ".last-outcome-backup-*.tmp")).Count -ne 0) {
  throw "uninstallResultEvidenceReadbackInvalid"
}
$uninstallCrossSpliceRun = Invoke-Bounded $windowsPowerShell @(
  "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", $manage,
  "-Action", "RecordEvidence", "-InstallDirectory", $runnerRoot,
  "-ValidationRoot", $runnerRoot, "-EvidencePhase", "uninstalled",
  "-EvidenceSuccess", "true", "-EvidenceResultCode", "uninstalled",
  "-EvidenceUninstallDisposition", "Preserve", "-EvidenceUninstallState",
  "pending") $recordEnvironment 10000
if ($uninstallCrossSpliceRun.exitCode -eq 0 -or
    $uninstallCrossSpliceRun.stdout.IndexOf('"success":true',
      [StringComparison]::Ordinal) -ge 0) {
  throw "uninstallEvidenceCrossSpliceAccepted"
}

$lifecycleRoot = Join-Path $root "compiled-lifecycle"
New-Item -ItemType Directory -Path $lifecycleRoot | Out-Null
$lifecycleInstall = Join-Path $lifecycleRoot "installed"
$lifecycleScript = Join-Path $lifecycleRoot "lifecycle.nsi"
$lifecycleInstaller = Join-Path $lifecycleRoot "fixture-installer.exe"
$escapedInstaller = $lifecycleInstaller.Replace('$', '$$')
$escapedInstall = $lifecycleInstall.Replace('$', '$$')
[IO.File]::WriteAllText($lifecycleScript, @"
Unicode true
RequestExecutionLevel user
SilentInstall silent
SilentUnInstall silent
OutFile `"$escapedInstaller`"
InstallDir `"$escapedInstall`"
Section `"Core`"
  SetOutPath `"`$INSTDIR`"
  FileOpen `$0 `"`$INSTDIR\payload805`" w
  FileWrite `$0 `"805`"
  FileClose `$0
  FileOpen `$0 `"`$INSTDIR\arp`" w
  FileClose `$0
  FileOpen `$0 `"`$INSTDIR\shortcut`" w
  FileClose `$0
  FileOpen `$0 `"`$INSTDIR\firewall`" w
  FileClose `$0
  FileOpen `$0 `"`$INSTDIR\bootstrap`" w
  FileClose `$0
  FileOpen `$0 `"`$INSTDIR\vd-failed`" w
  FileClose `$0
  WriteUninstaller `"`$INSTDIR\uninstall.exe`"
SectionEnd
$uninstallDeclaration
  IfFileExists `"`$INSTDIR\vd-verified`" +2 0
  Abort
  FileOpen `$0 `"`$INSTDIR\uninstall-called`" w
  FileClose `$0
  Delete `"`$INSTDIR\payload805`"
  Delete `"`$INSTDIR\arp`"
  Delete `"`$INSTDIR\shortcut`"
  Delete `"`$INSTDIR\firewall`"
  Delete `"`$INSTDIR\bootstrap`"
SectionEnd
"@, [Text.UTF8Encoding]::new($false))
$compileLifecycle = Invoke-Bounded $MakeNsis @(
  "/INPUTCHARSET", "UTF8", $lifecycleScript) @{} 15000
if ($compileLifecycle.exitCode -ne 0 -or
    -not (Test-Path -LiteralPath $lifecycleInstaller -PathType Leaf)) {
  throw "compiledLifecycleBuildFailed"
}
$installLifecycle = Invoke-Bounded $lifecycleInstaller @("/S") @{} 15000
$coreMarkers = @("payload805", "arp", "shortcut", "firewall", "bootstrap")
if ($installLifecycle.exitCode -ne 0 -or
    @(Get-ChildItem -LiteralPath $lifecycleInstall -File | Where-Object {
      $_.Name -in $coreMarkers }).Count -ne 5 -or
    (Get-Content -LiteralPath (Join-Path $lifecycleInstall "payload805") -Raw) -cne
      "805" -or
    (Test-Path -LiteralPath (Join-Path $lifecycleInstall "uninstall-called"))) {
  throw "compiledInstallerReachedUninstallSection"
}
$uninstaller = Join-Path $lifecycleInstall "uninstall.exe"
$blockedUninstall = Invoke-Bounded $uninstaller @("/S") @{} 15000
if (@(Get-ChildItem -LiteralPath $lifecycleInstall -File | Where-Object {
      $_.Name -in $coreMarkers }).Count -ne 5 -or
    (Test-Path -LiteralPath (Join-Path $lifecycleInstall "uninstall-called"))) {
  throw "compiledUninstallerFailClosedInvalid"
}
[IO.File]::WriteAllText((Join-Path $lifecycleInstall "vd-verified"), "1",
  [Text.UTF8Encoding]::new($false))
$verifiedUninstall = Invoke-Bounded $uninstaller @("/S") @{} 15000
if ($verifiedUninstall.exitCode -ne 0 -or
    -not (Test-Path -LiteralPath (
      Join-Path $lifecycleInstall "uninstall-called") -PathType Leaf) -or
    @(Get-ChildItem -LiteralPath $lifecycleInstall -File | Where-Object {
      $_.Name -in $coreMarkers }).Count -ne 0) {
  throw "compiledUninstallerSectionUnreachable"
}
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
  installerEvidenceAcknowledgement = [ordered]@{
    windowsPowerShell51 = $true
    lineCount = 1
    objectCount = 1
    atomicReadback = $true
    duplicatePipelineRejected = $true
    faultSuccessRejected = $true
  }
  nsisCompilerEvidence = [ordered]@{
    controlledFailureExit = 37
    controlledFailureStderrPreserved = $true
    realCompileExit = 0
    realArtifact = $true
    productionCallerContinued = $true
    productionCallerConsumedFailure = $true
    stdoutClosed = $true
    stderrClosed = $true
    temporaryResidue = 0
  }
  installerLifecycle = [ordered]@{
    sourceReservedUninstallSection = $true
    compiledReservedUninstallSection = $true
    installerUninstallCalls = 0
    corePayloadOwned = 805
    arpRetainedAfterVirtualDisplayFailure = $true
    shortcutRetainedAfterVirtualDisplayFailure = $true
    firewallRetainedAfterVirtualDisplayFailure = $true
    bootstrapRetainedAfterVirtualDisplayFailure = $true
    uninstallerBlockedBeforeVirtualDisplayVerified = $true
    uninstallerReachedAfterVirtualDisplayVerified = $true
    uninstallEvidenceAtomic = $true
    uninstallEvidenceCrossSpliceRejected = $true
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
