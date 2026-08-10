param(
  [Parameter(Mandatory)][string]$DotNet,
  [Parameter(Mandatory)][string[]]$PublishArguments,
  [Parameter(Mandatory)][string]$WorkingDirectory,
  [Parameter(Mandatory)][string]$EvidenceDirectory,
  [Parameter(Mandatory)][string]$DiagnosticLibraryRoot,
  [int]$DeadlineMilliseconds = 1200000,
  [string]$StdoutWriterFault = '',
  [string]$StderrWriterFault = '',
  [string]$ValidationFault = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertTo-NativeArgument([AllowEmptyString()][string]$Value) {
  if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
  $builder = [Text.StringBuilder]::new()
  $null = $builder.Append('"')
  $slashes = 0
  foreach ($character in $Value.ToCharArray()) {
    if ($character -eq '\') { $slashes++; continue }
    if ($character -eq '"') {
      $null = $builder.Append(('\' * (2 * $slashes + 1)))
      $null = $builder.Append('"')
      $slashes = 0
      continue
    }
    $null = $builder.Append(('\' * $slashes))
    $slashes = 0
    $null = $builder.Append($character)
  }
  $null = $builder.Append(('\' * (2 * $slashes)))
  $null = $builder.Append('"')
  return $builder.ToString()
}

function Get-LowerSha256([string]$Path) {
  return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Invoke-DiagnosticChild([string[]]$Arguments, [string]$Root, [bool]$ApplyFaults) {
  if (Test-Path -LiteralPath $Root) { throw 'dotnetPublishEvidenceNotFresh' }
  $exactCommandLine = (@($script:executablePath) + @($Arguments) |
      ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' '
  $runnerArguments = @(
    '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $script:runnerPath,
    '-EvidenceRoot', $Root, '-ApplicationPath', $script:executablePath,
    '-ExactCommandLine', $exactCommandLine, '-WorkingDirectory', $script:workingDirectory,
    '-HardCapMilliseconds', [string]$DeadlineMilliseconds
  )
  if ($ApplyFaults -and $ValidationFault) { $runnerArguments += @('-ValidationFault', $ValidationFault) }
  if ($ApplyFaults -and $StdoutWriterFault) { $runnerArguments += @('-StdoutWriterFault', $StdoutWriterFault) }
  if ($ApplyFaults -and $StderrWriterFault) { $runnerArguments += @('-StderrWriterFault', $StderrWriterFault) }

  $start = [Diagnostics.ProcessStartInfo]::new()
  $start.FileName = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
  $start.Arguments = ($runnerArguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' '
  $start.WorkingDirectory = $script:workingDirectory
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $start
  if (-not $process.Start()) { throw 'dotnetPublishRunnerStartFailed' }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  try {
    if (-not $process.WaitForExit($DeadlineMilliseconds + 30000)) {
      try { $process.Kill() } catch { }
      throw 'dotnetPublishRunnerTimeout'
    }
    $null = $stdoutTask.GetAwaiter().GetResult()
    $null = $stderrTask.GetAwaiter().GetResult()
  }
  finally { $process.Dispose() }

  $terminalPath = Join-Path $Root 'monitor-terminal.json'
  if (-not (Test-Path -LiteralPath $terminalPath -PathType Leaf)) {
    throw 'dotnetPublishEvidenceUnavailable'
  }
  $terminal = Get-Content -LiteralPath $terminalPath -Raw | ConvertFrom-Json
  $required = @('state', 'primary', 'execution', 'streams', 'correlation')
  foreach ($name in $required) {
    if (-not (@($terminal.PSObject.Properties.Name) -ccontains $name)) {
      throw 'dotnetPublishEvidenceInvalid'
    }
  }
  return [pscustomobject]@{
    arguments = @($Arguments)
    terminalPath = $terminalPath
    terminalSha256 = Get-LowerSha256 $terminalPath
    terminal = $terminal
  }
}

$dotNetCommand = Get-Command -Name $DotNet -CommandType Application -ErrorAction Stop
$script:executablePath = [IO.Path]::GetFullPath($dotNetCommand.Source)
$script:workingDirectory = [IO.Path]::GetFullPath($WorkingDirectory)
$script:runnerPath = Join-Path $DiagnosticLibraryRoot 'Invoke-OrchestrationRun.ps1'
if (-not (Test-Path -LiteralPath $script:runnerPath -PathType Leaf)) { throw 'dotnetDiagnosticRunnerUnavailable' }
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'dotnetPublishEvidenceNotFresh' }

$clock = [Diagnostics.Stopwatch]::StartNew()
$versionEvidence = Invoke-DiagnosticChild @('--info') ($EvidenceDirectory + '.dotnet-info') $false
if ([int]$versionEvidence.terminal.execution.exitCode -ne 0 -or
    -not $versionEvidence.terminal.streams.stdout.closed -or
    -not $versionEvidence.terminal.streams.stderr.closed) {
  throw 'dotnetPublishVersionEvidenceUnavailable'
}
$publishEvidence = Invoke-DiagnosticChild @($PublishArguments) $EvidenceDirectory $true
$item = Get-Item -LiteralPath $script:executablePath
$allowedEnvironmentNames = @(
  'DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'TEMP', 'TMP', 'DOTNET_GENERATE_ASPNET_CERTIFICATE',
  'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO', 'DOTNET_ADD_GLOBAL_TOOLS_TO_PATH',
  'DOTNET_SKIP_FIRST_TIME_EXPERIENCE'
)
return [pscustomobject]@{
  code = 'dotnetPublishEvidenceCaptured'
  executablePath = $script:executablePath
  executableSize = [int64]$item.Length
  executableSha256 = Get-LowerSha256 $script:executablePath
  fileProductVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($script:executablePath).ProductVersion
  versionEvidencePath = $versionEvidence.terminalPath
  versionEvidenceSha256 = $versionEvidence.terminalSha256
  versionRunId = $versionEvidence.terminal.correlation.runId
  workingDirectory = $script:workingDirectory
  arguments = @($PublishArguments)
  allowedEnvironmentNames = $allowedEnvironmentNames
  elapsedMilliseconds = [int64]$clock.ElapsedMilliseconds
  exitCode = $publishEvidence.terminal.execution.exitCode
  evidencePath = $publishEvidence.terminalPath
  evidenceSha256 = $publishEvidence.terminalSha256
  publishRunId = $publishEvidence.terminal.correlation.runId
}
