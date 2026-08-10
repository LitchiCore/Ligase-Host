param(
  [Parameter(Mandatory)][string]$DiagnosticLibraryRoot,
  [Parameter(Mandatory)][string]$ValidationRoot,
  [string]$DotNet = 'C:\Program Files\dotnet\dotnet.exe'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True([bool]$Condition, [string]$Code) { if (-not $Condition) { throw $Code } }
function New-CaseRoot([string]$Name) {
  $path = Join-Path $ValidationRoot $Name
  Assert-True (-not (Test-Path -LiteralPath $path)) "caseRootNotFresh:$Name"
  $null = New-Item -ItemType Directory -Path $path
  return $path
}

Assert-True ($ValidationRoot.StartsWith('D:\', [StringComparison]::OrdinalIgnoreCase)) 'validationRootMustBeD'
Assert-True (-not (Test-Path -LiteralPath $ValidationRoot)) 'validationRootNotFresh'
$null = New-Item -ItemType Directory -Path $ValidationRoot
$wrapper = Join-Path $PSScriptRoot 'Invoke-DotNetPublish.ps1'
$consumer = Join-Path $PSScriptRoot 'Assert-DotNetPublishEvidence.ps1'
$build = Join-Path $PSScriptRoot 'Build-LigaseInstaller.ps1'
$wrapperText = Get-Content -LiteralPath $wrapper -Raw
$buildText = Get-Content -LiteralPath $build -Raw
Assert-True ($wrapperText -notmatch '(?m)^\s*exit(?:\s|$)') 'wrapperExitRegression'
Assert-True ($buildText -notmatch '&\s*\$DotNet\s+publish') 'directDotNetPublishRegression'
Assert-True ($wrapperText.IndexOf('ConvertTo-NativeArgument', [StringComparison]::Ordinal) -ge 0) 'nativeArgvSeamMissing'
Assert-True ($wrapperText.IndexOf('Write-RawDiagnostic', [StringComparison]::Ordinal) -lt 0) 'wrapperMustNotReimplementDiagnosticWriter'
$consumerCallIndex = $buildText.IndexOf('Assert-DotNetPublishEvidence.ps1', [StringComparison]::Ordinal)
Assert-True ($consumerCallIndex -gt 0 -and $consumerCallIndex -lt $buildText.IndexOf('desktopPayloadValidation', [StringComparison]::Ordinal) -and $consumerCallIndex -lt $buildText.IndexOf('artifactSignatureInvalid', [StringComparison]::Ordinal) -and $consumerCallIndex -lt $buildText.LastIndexOf('installerBuilt', [StringComparison]::Ordinal)) 'consumerMustPrecedePayloadSignatureAndResult'

$fixtureRoot = New-CaseRoot 'fixture'
$fixtureSource = Join-Path $fixtureRoot 'Fixture.cs'
$fixtureExe = Join-Path $fixtureRoot 'DotNetFixture.exe'
@'
using System;
using System.IO;
using System.Threading;
public static class Fixture {
  public static int Main(string[] args) {
    if (args.Length == 1 && args[0] == "--info") { Console.WriteLine("fixture-info"); return 0; }
    string mode = args.Length > 1 ? args[1] : "empty";
    if (mode == "exit37") { Console.Error.Write("controlled-stderr"); return 37; }
    if (mode == "invalidUtf8") { Console.OpenStandardError().Write(new byte[]{0xff,0xfe,0x80},0,3); return 37; }
    if (mode == "overflow") { byte[] b=new byte[70000]; for(int i=0;i<b.Length;i++) b[i]=(byte)'x'; Console.OpenStandardOutput().Write(b,0,b.Length); return 37; }
    if (mode == "pending") { Console.Write("prefix"); Thread.Sleep(5000); return 0; }
    return 0;
  }
}
'@ | Set-Content -LiteralPath $fixtureSource -Encoding UTF8
Add-Type -Path $fixtureSource -OutputAssembly $fixtureExe -OutputType ConsoleApplication

$cases = @(
  @{ Name='exit37'; Args=@('publish','exit37'); Deadline=15000; Exit=37; Truncated=$false },
  @{ Name='empty'; Args=@('publish','empty'); Deadline=15000; Exit=0; Truncated=$false },
  @{ Name='invalidUtf8'; Args=@('publish','invalidUtf8'); Deadline=15000; Exit=37; Truncated=$false },
  @{ Name='overflow'; Args=@('publish','overflow'); Deadline=15000; Exit=37; Truncated=$true }
)
$results = @()
foreach ($case in $cases) {
  $root = New-CaseRoot $case.Name
  $evidence = Join-Path $root 'evidence'
  $result = & $wrapper -DotNet $fixtureExe -PublishArguments $case.Args -WorkingDirectory $root -EvidenceDirectory $evidence -DiagnosticLibraryRoot $DiagnosticLibraryRoot -DeadlineMilliseconds $case.Deadline
  $terminal = Get-Content -LiteralPath $result.evidencePath -Raw | ConvertFrom-Json
  Assert-True ($result.code -ceq 'dotnetPublishEvidenceCaptured') "$($case.Name):code"
  Assert-True ([int]$result.exitCode -eq $case.Exit) "$($case.Name):exit"
  Assert-True ($terminal.streams.stdout.closed -and $terminal.streams.stderr.closed) "$($case.Name):pipes"
  Assert-True (($terminal.streams.stdout.truncated -or $terminal.streams.stderr.truncated) -eq $case.Truncated) "$($case.Name):truncated"
  Assert-True ((Get-FileHash -LiteralPath $result.evidencePath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $result.evidenceSha256) "$($case.Name):terminalHash"
  $verified = & $consumer -Evidence $result -DotNet $fixtureExe -Arguments $case.Args -WorkingDirectory $root -EvidenceRoot $evidence
  Assert-True ($verified.code -ceq 'dotnetPublishEvidenceVerified' -and $verified.exitCode -eq $case.Exit) "$($case.Name):consumer"
  $results += [pscustomobject]@{ Evidence=$result; Terminal=$terminal; Root=$root }
}
Assert-True ($results[0].Terminal.streams.stderr.totalByteCount -eq 17) 'exit37StderrBytes'
Assert-True ($results[1].Terminal.streams.stdout.totalByteCount -eq 0 -and $results[1].Terminal.streams.stderr.totalByteCount -eq 0) 'emptyStreams'
Assert-True ($results[2].Terminal.streams.stderr.totalByteCount -eq 3) 'invalidUtf8Bytes'
Assert-True ($results[3].Terminal.streams.stdout.capturedByteCount -eq 65536 -and $results[3].Terminal.streams.stdout.totalByteCount -eq 70000) 'overflowCounts'

$base = $results[1].Evidence
$baseRoot = Split-Path -Parent $base.evidencePath
$baseArguments = @('publish','empty')
$rejected = 0
function Assert-ConsumerReject($Mutated, [string]$Name) {
  $didReject = $false
  try { $null = & $consumer -Evidence $Mutated -DotNet $fixtureExe -Arguments $baseArguments -WorkingDirectory (Split-Path -Parent $baseRoot) -EvidenceRoot $baseRoot }
  catch { $didReject = $_.Exception.Message -ceq 'dotnetPublishEvidenceInvalid' }
  Assert-True $didReject "crossSpliceAccepted:$Name"
  $script:rejected++
}
foreach ($mutation in @(
  @{Name='hostPath';Field='executablePath';Value=$wrapper},
  @{Name='hostSize';Field='executableSize';Value=1},
  @{Name='hostSha';Field='executableSha256';Value=('0'*64)},
  @{Name='hostVersion';Field='fileProductVersion';Value='drift'},
  @{Name='cwd';Field='workingDirectory';Value=$ValidationRoot},
  @{Name='publishHash';Field='evidenceSha256';Value=('0'*64)},
  @{Name='publishRunId';Field='publishRunId';Value=('0'*32)},
  @{Name='versionHash';Field='versionEvidenceSha256';Value=('0'*64)},
  @{Name='versionRunId';Field='versionRunId';Value=('0'*32)}
)) {
  $copy = $base | ConvertTo-Json -Depth 8 | ConvertFrom-Json
  $copy.($mutation.Field) = $mutation.Value
  Assert-ConsumerReject $copy $mutation.Name
}
foreach ($typedExit in @(@{Name='null';Value=$null},@{Name='string';Value='0'},@{Name='bool';Value=$false},@{Name='float';Value=[double]0.5})) {
  $copy = $base | ConvertTo-Json -Depth 8 | ConvertFrom-Json
  $copy.exitCode = $typedExit.Value
  Assert-ConsumerReject $copy "evidenceExit-$($typedExit.Name)"
}
$envOrder = $base | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$envOrder.allowedEnvironmentNames = @($envOrder.allowedEnvironmentNames | Sort-Object -Descending)
Assert-ConsumerReject $envOrder 'envOrder'
$envValue = $base | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$envValue.allowedEnvironmentNames[0] = 'DRIFT'
Assert-ConsumerReject $envValue 'envValue'
$publishPath = $base | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$publishPath.evidencePath = $base.versionEvidencePath
$publishPath.evidenceSha256 = $base.versionEvidenceSha256
Assert-ConsumerReject $publishPath 'publishPath'
$versionPath = $base | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$versionPath.versionEvidencePath = $base.evidencePath
$versionPath.versionEvidenceSha256 = $base.evidenceSha256
Assert-ConsumerReject $versionPath 'versionPath'
foreach ($obsoleteField in @('runnerExitCode','runnerStdout','runnerStderrPresent')) {
  $copy = $base | ConvertTo-Json -Depth 8 | ConvertFrom-Json
  $copy | Add-Member -NotePropertyName $obsoleteField -NotePropertyValue 0
  Assert-ConsumerReject $copy "obsolete-$obsoleteField"
}

$versionBytes = [IO.File]::ReadAllBytes($base.versionEvidencePath)
$versionObject = [Text.UTF8Encoding]::new($false,$true).GetString($versionBytes) | ConvertFrom-Json
foreach ($terminalMutation in @('exit','stdoutEof','stderrEof','exitNull','exitString','exitBool')) {
  $mutatedTerminal = [Text.UTF8Encoding]::new($false,$true).GetString($versionBytes) | ConvertFrom-Json
  if ($terminalMutation -ceq 'exit') { $mutatedTerminal.execution.exitCode = 37; $mutatedTerminal.state = 'failed'; $mutatedTerminal.primary.stage='processExit'; $mutatedTerminal.primary.reasonCode='nonzeroExit' }
  elseif ($terminalMutation -ceq 'stdoutEof') { $mutatedTerminal.streams.stdout.closed = $false }
  elseif ($terminalMutation -ceq 'stderrEof') { $mutatedTerminal.streams.stderr.closed = $false }
  elseif ($terminalMutation -ceq 'exitNull') { $mutatedTerminal.execution.exitCode = $null }
  elseif ($terminalMutation -ceq 'exitString') { $mutatedTerminal.execution.exitCode = '0' }
  else { $mutatedTerminal.execution.exitCode = $false }
  [IO.File]::WriteAllText($base.versionEvidencePath,($mutatedTerminal|ConvertTo-Json -Depth 8 -Compress),[Text.UTF8Encoding]::new($false,$true))
  $copy = $base | ConvertTo-Json -Depth 8 | ConvertFrom-Json
  $copy.versionEvidenceSha256 = (Get-FileHash -LiteralPath $base.versionEvidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
  Assert-ConsumerReject $copy "versionTerminal-$terminalMutation"
  [IO.File]::WriteAllBytes($base.versionEvidencePath,$versionBytes)
}
$publishBytes = [IO.File]::ReadAllBytes($base.evidencePath)
$identityTerminal = [Text.UTF8Encoding]::new($false,$true).GetString($publishBytes) | ConvertFrom-Json
$identityTerminal.state='failed';$identityTerminal.primary.stage='identitySnapshot';$identityTerminal.primary.reasonCode='listDrift'
$identityTerminal.execution.exitCode=$null
$identityTerminal.identitySnapshotDetail=[pscustomobject][ordered]@{innerStage='jobListSecond';reasonCode='listDrift';attemptCount=[long]3;firstListCount=[long]2;secondListCount=[long]1;remainingDeadlineTicks=[long]100}
[IO.File]::WriteAllText($base.evidencePath,($identityTerminal|ConvertTo-Json -Depth 10 -Compress),[Text.UTF8Encoding]::new($false,$true))
$identityEvidence=$base|ConvertTo-Json -Depth 8|ConvertFrom-Json;$identityEvidence.exitCode=$null;$identityEvidence.evidenceSha256=(Get-FileHash $base.evidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
$identityAccepted=$false
try{$null=& $consumer -Evidence $identityEvidence -DotNet $fixtureExe -Arguments $baseArguments -WorkingDirectory (Split-Path -Parent $baseRoot) -EvidenceRoot $baseRoot}catch{$identityAccepted=$_.Exception.Message-like'dotnetPublishEvidenceUnavailable:identitySnapshot:listDrift'}
Assert-True $identityAccepted 'identityDetailPositiveNotTypedUnavailable'
[IO.File]::WriteAllBytes($base.evidencePath,$publishBytes)
foreach($detailMutation in @('missing','extra','detailOnNonidentity','nullOnIdentity','stageDrift','reasonDrift','attemptZero','countNegative','countOverflow','remainingNegative')){
  $mutated=[Text.UTF8Encoding]::new($false,$true).GetString($publishBytes)|ConvertFrom-Json
  if($detailMutation-eq'detailOnNonidentity'){$mutated.identitySnapshotDetail=[pscustomobject]@{innerStage='jobListSecond';reasonCode='listDrift';attemptCount=3;firstListCount=2;secondListCount=1;remainingDeadlineTicks=100}}
  else{$mutated.state='failed';$mutated.primary.stage='identitySnapshot';$mutated.primary.reasonCode='listDrift';$mutated.identitySnapshotDetail=[pscustomobject][ordered]@{innerStage='jobListSecond';reasonCode='listDrift';attemptCount=[long]3;firstListCount=[long]2;secondListCount=[long]1;remainingDeadlineTicks=[long]100};switch($detailMutation){'missing'{$mutated.identitySnapshotDetail.PSObject.Properties.Remove('attemptCount')};'extra'{$mutated.identitySnapshotDetail|Add-Member extra 1};'nullOnIdentity'{$mutated.identitySnapshotDetail=$null};'stageDrift'{$mutated.identitySnapshotDetail.innerStage='openProcess'};'reasonDrift'{$mutated.identitySnapshotDetail.reasonCode='processDisappeared'};'attemptZero'{$mutated.identitySnapshotDetail.attemptCount=0};'countNegative'{$mutated.identitySnapshotDetail.firstListCount=-1};'countOverflow'{$mutated.identitySnapshotDetail.secondListCount=4097};'remainingNegative'{$mutated.identitySnapshotDetail.remainingDeadlineTicks=-1}}}
  [IO.File]::WriteAllText($base.evidencePath,($mutated|ConvertTo-Json -Depth 10 -Compress),[Text.UTF8Encoding]::new($false,$true));$copy=$base|ConvertTo-Json -Depth 8|ConvertFrom-Json;$copy.evidenceSha256=(Get-FileHash $base.evidencePath -Algorithm SHA256).Hash.ToLowerInvariant();Assert-ConsumerReject $copy "identityDetail-$detailMutation";[IO.File]::WriteAllBytes($base.evidencePath,$publishBytes)
}
foreach($exitMutation in @('identityTerminalInteger','identityOuterInteger','identityBothInteger','processExitTerminalNull','processExitOuterNull')){
  $mutated=[Text.UTF8Encoding]::new($false,$true).GetString($publishBytes)|ConvertFrom-Json
  $copy=$base|ConvertTo-Json -Depth 8|ConvertFrom-Json
  if($exitMutation -like 'identity*'){$mutated.state='failed';$mutated.primary.stage='identitySnapshot';$mutated.primary.reasonCode='listDrift';$mutated.execution.exitCode=$null;$mutated.identitySnapshotDetail=[pscustomobject][ordered]@{innerStage='jobListSecond';reasonCode='listDrift';attemptCount=[long]3;firstListCount=[long]2;secondListCount=[long]1;remainingDeadlineTicks=[long]100};$copy.exitCode=$null}
  else{$mutated.state='failed';$mutated.primary.stage='processExit';$mutated.primary.reasonCode='nonzeroExit';$mutated.execution.exitCode=37;$copy.exitCode=37}
  if($exitMutation -in @('identityTerminalInteger','identityBothInteger')){$mutated.execution.exitCode=37}
  if($exitMutation -in @('identityOuterInteger','identityBothInteger')){$copy.exitCode=37}
  if($exitMutation -ceq 'processExitTerminalNull'){$mutated.execution.exitCode=$null}
  if($exitMutation -ceq 'processExitOuterNull'){$copy.exitCode=$null}
  [IO.File]::WriteAllText($base.evidencePath,($mutated|ConvertTo-Json -Depth 10 -Compress),[Text.UTF8Encoding]::new($false,$true));$copy.evidenceSha256=(Get-FileHash $base.evidencePath -Algorithm SHA256).Hash.ToLowerInvariant();Assert-ConsumerReject $copy "exitCorrelation-$exitMutation";[IO.File]::WriteAllBytes($base.evidencePath,$publishBytes)
}
foreach ($terminalMutation in @('outputOverflowExit0','cleanupFailedExit0','pipePendingExit0','passedNonzero','stageReasonDrift','capturedShaDrift','fullShaDrift','countDrift','truncatedDrift','exitNull','exitString','exitBool','exitFloat','closedString','truncatedString','cleanupRootString','cleanupLockNumber','countString','countBool','countFloat','sizeString','elapsedString','elapsedBool','elapsedFloat','rootPidString','rootPidBool','rootPidFloat')) {
  $mutatedTerminal = [Text.UTF8Encoding]::new($false,$true).GetString($publishBytes) | ConvertFrom-Json
  switch ($terminalMutation) {
    'outputOverflowExit0' { $mutatedTerminal.state='failed';$mutatedTerminal.primary.stage='pipeDrain';$mutatedTerminal.primary.reasonCode='outputOverflow' }
    'cleanupFailedExit0' { $mutatedTerminal.state='failed';$mutatedTerminal.primary.stage='cleanup';$mutatedTerminal.primary.reasonCode='rootStopFailed';$mutatedTerminal.cleanup.state='failed';$mutatedTerminal.cleanup.rootIdentityAlive=$true }
    'pipePendingExit0' { $mutatedTerminal.state='failed';$mutatedTerminal.primary.stage='pipeDrain';$mutatedTerminal.primary.reasonCode='pipePending' }
    'passedNonzero' { $mutatedTerminal.execution.exitCode=37 }
    'stageReasonDrift' { $mutatedTerminal.state='failed';$mutatedTerminal.primary.stage='cleanup';$mutatedTerminal.primary.reasonCode='nonzeroExit' }
    'capturedShaDrift' { $mutatedTerminal.streams.stdout.capturedSha256=('0'*64) }
    'fullShaDrift' { $mutatedTerminal.streams.stdout.fullStreamSha256=('0'*64) }
    'countDrift' { $mutatedTerminal.streams.stdout.capturedByteCount=1 }
    'truncatedDrift' { $mutatedTerminal.streams.stdout.truncated=$true }
    'exitNull' { $mutatedTerminal.execution.exitCode=$null }
    'exitString' { $mutatedTerminal.execution.exitCode='0' }
    'exitBool' { $mutatedTerminal.execution.exitCode=$false }
    'exitFloat' { $mutatedTerminal.execution.exitCode=[double]0.5 }
    'closedString' { $mutatedTerminal.streams.stdout.closed='true' }
    'truncatedString' { $mutatedTerminal.streams.stdout.truncated='false' }
    'cleanupRootString' { $mutatedTerminal.cleanup.rootIdentityAlive='false' }
    'cleanupLockNumber' { $mutatedTerminal.cleanup.lockPathAbsent=1 }
    'countString' { $mutatedTerminal.streams.stdout.capturedByteCount='0' }
    'countBool' { $mutatedTerminal.streams.stdout.capturedByteCount=$false }
    'countFloat' { $mutatedTerminal.streams.stdout.capturedByteCount=[double]0.5 }
    'sizeString' { $mutatedTerminal.streams.stdout.persistedSize='0' }
    'elapsedString' { $mutatedTerminal.execution.elapsedMilliseconds='1' }
    'elapsedBool' { $mutatedTerminal.execution.elapsedMilliseconds=$false }
    'elapsedFloat' { $mutatedTerminal.execution.elapsedMilliseconds=[double]0.5 }
    'rootPidString' { $mutatedTerminal.correlation.rootPid=[string]$mutatedTerminal.correlation.rootPid }
    'rootPidBool' { $mutatedTerminal.correlation.rootPid=$true }
    'rootPidFloat' { $mutatedTerminal.correlation.rootPid=[double]1.5 }
  }
  [IO.File]::WriteAllText($base.evidencePath,($mutatedTerminal|ConvertTo-Json -Depth 8 -Compress),[Text.UTF8Encoding]::new($false,$true))
  $copy = $base | ConvertTo-Json -Depth 8 | ConvertFrom-Json
  $copy.evidenceSha256 = (Get-FileHash -LiteralPath $base.evidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($terminalMutation -ceq 'passedNonzero') { $copy.exitCode=37 }
  Assert-ConsumerReject $copy "publishTerminal-$terminalMutation"
  [IO.File]::WriteAllBytes($base.evidencePath,$publishBytes)
}

$pendingRoot = New-CaseRoot 'pending'
$pendingFailed = $false
try { $null = & $wrapper -DotNet $fixtureExe -PublishArguments @('publish','pending') -WorkingDirectory $pendingRoot -EvidenceDirectory (Join-Path $pendingRoot 'evidence') -DiagnosticLibraryRoot $DiagnosticLibraryRoot -DeadlineMilliseconds 1000 }
catch { $pendingFailed = $_.Exception.Message -like 'dotnetPublishEvidenceUnavailable*' }
Assert-True $pendingFailed 'pendingPipeMustFailClosed'

$writerFaults = 0
foreach ($stream in @('stdout','stderr')) {
  foreach ($stage in @('create','write','flush','replace','readback','hash')) {
    $faultRoot = New-CaseRoot "writerFault-$stream-$stage"
    $writerFailed = $false
    try {
      $parameters = @{ DotNet=$fixtureExe; PublishArguments=@('publish','exit37'); WorkingDirectory=$faultRoot; EvidenceDirectory=(Join-Path $faultRoot 'evidence'); DiagnosticLibraryRoot=$DiagnosticLibraryRoot; DeadlineMilliseconds=15000 }
      if ($stream -ceq 'stdout') { $parameters.StdoutWriterFault = $stage } else { $parameters.StderrWriterFault = $stage }
      $null = & $wrapper @parameters
    }
    catch { $writerFailed = $_.Exception.Message -like 'dotnetPublishEvidenceUnavailable*' }
    Assert-True $writerFailed "writerFaultMustBeUnavailable:${stream}:$stage"
    $writerFaults++
  }
}

$realRoot = New-CaseRoot 'realDotNet'
$project = Join-Path $realRoot 'Probe.csproj'
$program = Join-Path $realRoot 'Program.cs'
'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>' | Set-Content -LiteralPath $project -Encoding UTF8
'System.Console.WriteLine("probe");' | Set-Content -LiteralPath $program -Encoding UTF8
$real = & $wrapper -DotNet $DotNet -PublishArguments @('publish',$project,'-o',(Join-Path $realRoot 'out'),'--nologo','-p:RestoreIgnoreFailedSources=true','-p:UseSharedCompilation=false','-nodeReuse:false') -WorkingDirectory $realRoot -EvidenceDirectory (Join-Path $realRoot 'evidence') -DiagnosticLibraryRoot $DiagnosticLibraryRoot -DeadlineMilliseconds 120000
Assert-True ($real.exitCode -eq 0) 'realDotNetPublishFailed'
$realVerified = & $consumer -Evidence $real -DotNet $DotNet -Arguments $real.arguments -WorkingDirectory $realRoot -EvidenceRoot (Join-Path $realRoot 'evidence')
Assert-True ($realVerified.code -ceq 'dotnetPublishEvidenceVerified' -and $realVerified.exitCode -eq 0) 'realDotNetConsumerFailed'
$realTerminal = Get-Content -LiteralPath $real.evidencePath -Raw | ConvertFrom-Json
Assert-True ($realTerminal.state -ceq 'passed' -and $realTerminal.primary.stage -ceq 'none' -and $realTerminal.primary.reasonCode -ceq 'none' -and $realTerminal.execution.exitCode -eq 0 -and $realTerminal.cleanup.state -ceq 'completed' -and $realTerminal.streams.stdout.closed -and $realTerminal.streams.stderr.closed) 'realDotNetSuccessTuple'
Assert-True (Test-Path -LiteralPath (Join-Path $realRoot 'out\Probe.dll') -PathType Leaf) 'realDotNetArtifactMissing'
'continued' | Set-Content -LiteralPath (Join-Path $realRoot 'same-host-continuation.marker') -Encoding ASCII

$residue = @(Get-ChildItem -LiteralPath $ValidationRoot -Recurse -Force -File | Where-Object { $_.Name -match '\.(?:tmp|bak)$' })
Assert-True ($residue.Count -eq 0) 'temporaryResidue'
[pscustomobject]@{
  code = 'dotnetPublishEvidenceTestsPassed'
  controlledCases = $cases.Count
  pendingRejected = $pendingFailed
  writerFaultsRejected = $writerFaults
  crossSplicesRejected = $rejected
  realPublishExitCode = [int]$real.exitCode
  sameHostContinued = Test-Path -LiteralPath (Join-Path $realRoot 'same-host-continuation.marker')
  temporaryResidueCount = $residue.Count
} | ConvertTo-Json -Compress
