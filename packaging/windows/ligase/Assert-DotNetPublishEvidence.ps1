param(
  [Parameter(Mandatory)]$Evidence,
  [Parameter(Mandatory)][string]$DotNet,
  [Parameter(Mandatory)][string[]]$Arguments,
  [Parameter(Mandatory)][string]$WorkingDirectory,
  [Parameter(Mandatory)][string]$EvidenceRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-CanonicalPathSha256([string]$Path) {
  $canonical = [IO.Path]::GetFullPath($Path).Replace('/', '\')
  $root = [IO.Path]::GetPathRoot($canonical)
  if ($canonical.Length -gt $root.Length) { $canonical = $canonical.TrimEnd('\') }
  $bytes = [Text.UTF8Encoding]::new($false, $true).GetBytes($canonical.ToLowerInvariant())
  $sha = [Security.Cryptography.SHA256]::Create()
  try { return [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
  finally { $sha.Dispose() }
}
function Assert-ExactProperties($Value, [string[]]$Expected) {
  if ($null -eq $Value) { throw 'dotnetPublishEvidenceInvalid' }
  $actual = @($Value.PSObject.Properties.Name)
  if (@($Expected | Where-Object { -not ($actual -ccontains $_) }).Count -ne 0 -or @($actual | Where-Object { -not ($Expected -ccontains $_) }).Count -ne 0) { throw 'dotnetPublishEvidenceInvalid' }
}
function Test-Hex64($Value) { return ($Value -is [string] -and $Value -cmatch '^[0-9a-f]{64}$') }
function Test-JsonInteger($Value) { return ($Value -is [int] -or $Value -is [long]) }
function Test-JsonBoolean($Value) { return ($Value -is [bool]) }
function Test-StringArray($Value) {
  if ($null -eq $Value -or $Value -is [string] -or $Value -isnot [Collections.IEnumerable]) { return $false }
  foreach ($item in $Value) { if ($item -isnot [string]) { return $false } }
  return $true
}
function Read-Terminal([string]$Path, [string]$ExpectedRunId) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or ((Get-Item -LiteralPath $Path).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'dotnetPublishEvidenceInvalid' }
  $terminal = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
  Assert-ExactProperties $terminal @('schemaVersion','resultCode','state','primary','execution','cleanup','streams','correlation','writtenUtc')
  Assert-ExactProperties $terminal.primary @('stage','reasonCode')
  Assert-ExactProperties $terminal.execution @('exitCode','elapsedMilliseconds','peakOwnedPrivateBytes')
  Assert-ExactProperties $terminal.cleanup @('state','rootIdentityAlive','descendantCount','ownedPrivateBytes','lockPathAbsent')
  Assert-ExactProperties $terminal.streams @('stdout','stderr')
  $streamFields = @('capturedByteCount','totalByteCount','capturedSha256','fullStreamSha256','truncated','closed','encodingState','canonicalPathSha256','persistedSize','persistedSha256')
  Assert-ExactProperties $terminal.streams.stdout $streamFields
  Assert-ExactProperties $terminal.streams.stderr $streamFields
  Assert-ExactProperties $terminal.correlation @('runId','rootIdentityState','rootCreationState','rootPid','rootCreationFileTimeUtc100ns','standardPathCanonicalSha256')
  $reasonPairs = @{none=@('none');processStart=@('processStartFailed');processExit=@('nonzeroExit');execution=@('runnerException');deadline=@('timeout');resourceLimit=@('privateBytesLimitExceeded');identitySnapshot=@('snapshotDeadlineExceeded','identityUnavailable','identityDrift','jobListChanged');pipeDrain=@('stdoutReadFailed','stderrReadFailed','pipePending','outputOverflow');cleanup=@('rootStopFailed','descendantsRemaining','ownedPrivateBytesRemaining','cleanupDeadlineExceeded');ownership=@('ownershipHandleLost')}
  $stage=[string]$terminal.primary.stage; $reason=[string]$terminal.primary.reasonCode
  [uint64]$pidValue=0; [uint64]$fileTime=0; [uint64]$descendants=0; [uint64]$owned=0; [uint64]$peak=0; [long]$elapsed=0
  if ($terminal.primary.stage -isnot [string] -or $terminal.primary.reasonCode -isnot [string] -or -not $reasonPairs.ContainsKey($stage) -or $reason -notin $reasonPairs[$stage] -or -not (Test-JsonInteger $terminal.schemaVersion) -or [long]$terminal.schemaVersion -ne 1 -or $terminal.resultCode -isnot [string] -or $terminal.resultCode -cne 'orchestrationTerminal' -or $terminal.state -isnot [string] -or $terminal.correlation.runId -isnot [string] -or $terminal.correlation.runId -cne $ExpectedRunId -or $terminal.correlation.runId -cnotmatch '^[0-9a-f]{32}$' -or $terminal.correlation.standardPathCanonicalSha256 -isnot [string] -or $terminal.correlation.standardPathCanonicalSha256 -cne (Get-CanonicalPathSha256 $Path) -or $terminal.correlation.rootIdentityState -isnot [string] -or $terminal.correlation.rootIdentityState -cne 'created' -or $terminal.correlation.rootCreationState -isnot [string] -or $terminal.correlation.rootCreationState -cne 'created' -or -not (Test-JsonInteger $terminal.correlation.rootPid) -or -not [uint64]::TryParse([string]$terminal.correlation.rootPid,[ref]$pidValue) -or $pidValue -lt 1 -or $pidValue -gt [uint32]::MaxValue -or $terminal.correlation.rootCreationFileTimeUtc100ns -isnot [string] -or $terminal.correlation.rootCreationFileTimeUtc100ns -cnotmatch '^(?:0|[1-9][0-9]{0,19})$' -or -not [uint64]::TryParse($terminal.correlation.rootCreationFileTimeUtc100ns,[ref]$fileTime) -or $fileTime -eq 0 -or -not (Test-JsonInteger $terminal.cleanup.descendantCount) -or -not [uint64]::TryParse([string]$terminal.cleanup.descendantCount,[ref]$descendants) -or $terminal.cleanup.ownedPrivateBytes -isnot [string] -or -not [uint64]::TryParse($terminal.cleanup.ownedPrivateBytes,[ref]$owned) -or $terminal.execution.peakOwnedPrivateBytes -isnot [string] -or -not [uint64]::TryParse($terminal.execution.peakOwnedPrivateBytes,[ref]$peak) -or -not (Test-JsonInteger $terminal.execution.elapsedMilliseconds) -or -not [long]::TryParse([string]$terminal.execution.elapsedMilliseconds,[ref]$elapsed) -or $elapsed -lt 0 -or $terminal.cleanup.state -isnot [string] -or $terminal.cleanup.state -cne 'completed' -or -not (Test-JsonBoolean $terminal.cleanup.rootIdentityAlive) -or $terminal.cleanup.rootIdentityAlive -or -not (Test-JsonBoolean $terminal.cleanup.lockPathAbsent) -or $descendants -ne 0 -or $owned -ne 0 -or -not $terminal.cleanup.lockPathAbsent -or -not (Test-JsonBoolean $terminal.streams.stdout.closed) -or -not (Test-JsonBoolean $terminal.streams.stderr.closed) -or -not $terminal.streams.stdout.closed -or -not $terminal.streams.stderr.closed -or -not (Test-JsonInteger $terminal.execution.exitCode) -or [long]$terminal.execution.exitCode -lt [int]::MinValue -or [long]$terminal.execution.exitCode -gt [int]::MaxValue) { throw 'dotnetPublishEvidenceInvalid' }
  $success = $terminal.state -ceq 'passed' -and $stage -ceq 'none' -and $reason -ceq 'none' -and [long]$terminal.execution.exitCode -eq 0
  $nonzero = $terminal.state -ceq 'failed' -and $stage -ceq 'processExit' -and $reason -ceq 'nonzeroExit' -and [long]$terminal.execution.exitCode -ne 0
  if (-not $success -and -not $nonzero) { throw 'dotnetPublishEvidenceInvalid' }
  $evidenceRoot = Split-Path -Parent $Path
  foreach ($streamName in @('stdout','stderr')) {
    $streamValue = $terminal.streams.$streamName
    $streamPath = Join-Path $evidenceRoot "$streamName.bin"
    if (-not (Test-Path -LiteralPath $streamPath -PathType Leaf) -or ((Get-Item -LiteralPath $streamPath).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'dotnetPublishEvidenceInvalid' }
    $streamLength = (Get-Item -LiteralPath $streamPath).Length
    $streamSha = (Get-FileHash -LiteralPath $streamPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [uint64]$captured=0; [uint64]$total=0; [uint64]$persisted=0
    if (-not (Test-JsonInteger $streamValue.capturedByteCount) -or -not (Test-JsonInteger $streamValue.totalByteCount) -or -not (Test-JsonInteger $streamValue.persistedSize) -or -not [uint64]::TryParse([string]$streamValue.capturedByteCount,[ref]$captured) -or -not [uint64]::TryParse([string]$streamValue.totalByteCount,[ref]$total) -or -not [uint64]::TryParse([string]$streamValue.persistedSize,[ref]$persisted) -or $captured -gt 65536 -or $streamValue.encodingState -isnot [string] -or $streamValue.encodingState -cne 'rawBytes' -or -not (Test-JsonBoolean $streamValue.truncated) -or -not (Test-JsonBoolean $streamValue.closed) -or -not $streamValue.closed -or -not (Test-Hex64 $streamValue.capturedSha256) -or -not (Test-Hex64 $streamValue.persistedSha256) -or -not (Test-Hex64 $streamValue.fullStreamSha256) -or -not (Test-Hex64 $streamValue.canonicalPathSha256) -or $streamValue.canonicalPathSha256 -cne (Get-CanonicalPathSha256 $streamPath) -or $captured -ne [uint64]$streamLength -or $persisted -ne [uint64]$streamLength -or $streamValue.capturedSha256 -cne $streamSha -or $streamValue.persistedSha256 -cne $streamSha -or $total -lt $captured -or ($streamValue.truncated -ne ($total -gt $captured)) -or (-not $streamValue.truncated -and ($total -ne $captured -or $streamValue.fullStreamSha256 -cne $streamSha))) { throw 'dotnetPublishEvidenceInvalid' }
  }
  return $terminal
}

$command = Get-Command -Name $DotNet -CommandType Application -ErrorAction Stop
$expectedExecutable = [IO.Path]::GetFullPath($command.Source)
$item = Get-Item -LiteralPath $expectedExecutable
$expectedWorkingDirectory = [IO.Path]::GetFullPath($WorkingDirectory)
$expectedRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$expectedVersionRoot = $expectedRoot + '.dotnet-info'
$expectedPath = Join-Path $expectedRoot 'monitor-terminal.json'
$expectedVersionPath = Join-Path $expectedVersionRoot 'monitor-terminal.json'
$expectedEnvironment = @('DOTNET_CLI_HOME','NUGET_PACKAGES','TEMP','TMP','DOTNET_GENERATE_ASPNET_CERTIFICATE','DOTNET_CLI_TELEMETRY_OPTOUT','DOTNET_NOLOGO','DOTNET_ADD_GLOBAL_TOOLS_TO_PATH','DOTNET_SKIP_FIRST_TIME_EXPERIENCE')
$fields = @('code','executablePath','executableSize','executableSha256','fileProductVersion','versionEvidencePath','versionEvidenceSha256','versionRunId','workingDirectory','arguments','allowedEnvironmentNames','elapsedMilliseconds','exitCode','evidencePath','evidenceSha256','publishRunId')
Assert-ExactProperties $Evidence $fields
if ($Evidence.code -isnot [string] -or $Evidence.code -cne 'dotnetPublishEvidenceCaptured' -or -not (Test-JsonInteger $Evidence.exitCode) -or [long]$Evidence.exitCode -lt [int]::MinValue -or [long]$Evidence.exitCode -gt [int]::MaxValue -or $Evidence.executablePath -isnot [string] -or $Evidence.executablePath -cne $expectedExecutable -or -not (Test-JsonInteger $Evidence.executableSize) -or [long]$Evidence.executableSize -ne $item.Length -or -not (Test-Hex64 $Evidence.executableSha256) -or $Evidence.executableSha256 -cne (Get-FileHash -LiteralPath $expectedExecutable -Algorithm SHA256).Hash.ToLowerInvariant() -or $Evidence.fileProductVersion -isnot [string] -or $Evidence.fileProductVersion -cne [Diagnostics.FileVersionInfo]::GetVersionInfo($expectedExecutable).ProductVersion -or $Evidence.workingDirectory -isnot [string] -or $Evidence.workingDirectory -cne $expectedWorkingDirectory -or $Evidence.evidencePath -isnot [string] -or $Evidence.evidencePath -cne $expectedPath -or $Evidence.versionEvidencePath -isnot [string] -or $Evidence.versionEvidencePath -cne $expectedVersionPath -or -not (Test-Hex64 $Evidence.evidenceSha256) -or -not (Test-Hex64 $Evidence.versionEvidenceSha256) -or $Evidence.publishRunId -isnot [string] -or $Evidence.publishRunId -cnotmatch '^[0-9a-f]{32}$' -or $Evidence.versionRunId -isnot [string] -or $Evidence.versionRunId -cnotmatch '^[0-9a-f]{32}$' -or -not (Test-JsonInteger $Evidence.elapsedMilliseconds) -or [long]$Evidence.elapsedMilliseconds -lt 0 -or -not (Test-StringArray $Evidence.arguments) -or -not (Test-StringArray $Evidence.allowedEnvironmentNames) -or $Evidence.arguments.Count -ne $Arguments.Count -or $Evidence.allowedEnvironmentNames.Count -ne $expectedEnvironment.Count) { throw 'dotnetPublishEvidenceInvalid' }
for ($index=0; $index -lt $Arguments.Count; $index++) { if ($Evidence.arguments[$index] -cne $Arguments[$index]) { throw 'dotnetPublishEvidenceInvalid' } }
for ($index=0; $index -lt $expectedEnvironment.Count; $index++) { if ($Evidence.allowedEnvironmentNames[$index] -cne $expectedEnvironment[$index]) { throw 'dotnetPublishEvidenceInvalid' } }
foreach ($rootPath in @($expectedRoot,$expectedVersionRoot)) { if (-not (Test-Path -LiteralPath $rootPath -PathType Container) -or ((Get-Item -LiteralPath $rootPath).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'dotnetPublishEvidenceInvalid' } }
if ((Get-FileHash -LiteralPath $expectedPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Evidence.evidenceSha256 -or (Get-FileHash -LiteralPath $expectedVersionPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Evidence.versionEvidenceSha256) { throw 'dotnetPublishEvidenceInvalid' }
$versionTerminal = Read-Terminal $expectedVersionPath $Evidence.versionRunId
$publishTerminal = Read-Terminal $expectedPath $Evidence.publishRunId
if ($versionTerminal.state -cne 'passed' -or $versionTerminal.primary.stage -cne 'none' -or $versionTerminal.primary.reasonCode -cne 'none' -or [long]$versionTerminal.execution.exitCode -ne 0 -or [long]$publishTerminal.execution.exitCode -ne [long]$Evidence.exitCode) { throw 'dotnetPublishEvidenceInvalid' }
[pscustomobject]@{ code='dotnetPublishEvidenceVerified'; exitCode=[int]$publishTerminal.execution.exitCode; publishRunId=$publishTerminal.correlation.runId; versionRunId=$versionTerminal.correlation.runId }
