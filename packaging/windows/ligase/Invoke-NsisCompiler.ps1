param(
  [Parameter(Mandatory = $true)][string]$MakeNsis,
  [Parameter(Mandatory = $true)][string[]]$CompilerArguments,
  [Parameter(Mandatory = $true)][string]$WorkingDirectory,
  [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
  [int]$DeadlineMilliseconds = 600000,
  [int]$StreamByteLimit = 1048576
)

$ErrorActionPreference = "Stop"
$utf8 = [Text.UTF8Encoding]::new($false, $true)

function Get-Sha256Hex([byte[]]$Bytes) {
  $sha = [Security.Cryptography.SHA256]::Create()
  try { return [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-', '').ToLowerInvariant() }
  finally { $sha.Dispose() }
}

function Quote-NativeArgument([string]$Value) {
  if ($Value -notmatch '[\s"]') { return $Value }
  return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function Invoke-BoundedProcess([string[]]$Arguments, [int]$Deadline, [int]$Limit) {
  $start = [Diagnostics.ProcessStartInfo]::new()
  $start.FileName = $MakeNsis
  $start.Arguments = (@($Arguments | ForEach-Object { Quote-NativeArgument $_ }) -join ' ')
  $start.WorkingDirectory = $WorkingDirectory
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  $start.StandardOutputEncoding = $utf8
  $start.StandardErrorEncoding = $utf8
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $start
  if (-not $process.Start()) { throw 'nsisProcessStartFailed' }
  try {
    $stdoutBuffer = [char[]]::new(1024)
    $stderrBuffer = [char[]]::new(1024)
    $stdout = [Text.StringBuilder]::new()
    $stderr = [Text.StringBuilder]::new()
    $stdoutTask = $process.StandardOutput.ReadAsync($stdoutBuffer, 0, $stdoutBuffer.Length)
    $stderrTask = $process.StandardError.ReadAsync($stderrBuffer, 0, $stderrBuffer.Length)
    $stdoutClosed = $false
    $stderrClosed = $false
    $overflow = $false
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (-not ($process.HasExited -and $stdoutClosed -and $stderrClosed)) {
      foreach ($streamName in @('stdout', 'stderr')) {
        $task = if ($streamName -eq 'stdout') { $stdoutTask } else { $stderrTask }
        if ($task.IsCompleted) {
          $count = $task.GetAwaiter().GetResult()
          if ($count -eq 0) {
            if ($streamName -eq 'stdout') { $stdoutClosed = $true } else { $stderrClosed = $true }
          } else {
            $builder = if ($streamName -eq 'stdout') { $stdout } else { $stderr }
            $buffer = if ($streamName -eq 'stdout') { $stdoutBuffer } else { $stderrBuffer }
            if ($utf8.GetByteCount($builder.ToString()) + $utf8.GetByteCount($buffer, 0, $count) -gt $Limit) {
              $overflow = $true
              break
            }
            $null = $builder.Append([string]::new($buffer, 0, $count))
            $next = if ($streamName -eq 'stdout') {
              $process.StandardOutput.ReadAsync($stdoutBuffer, 0, $stdoutBuffer.Length)
            } else {
              $process.StandardError.ReadAsync($stderrBuffer, 0, $stderrBuffer.Length)
            }
            if ($streamName -eq 'stdout') { $stdoutTask = $next } else { $stderrTask = $next }
          }
        }
      }
      if ($overflow -or $clock.ElapsedMilliseconds -ge $Deadline) {
        try { $process.Kill() } catch {}
        if (-not $process.WaitForExit(5000)) { throw 'nsisCleanupUnavailable' }
        throw $(if ($overflow) { 'nsisOutputOverflow' } else { 'nsisProcessTimeout' })
      }
      if (-not ($process.HasExited -and $stdoutClosed -and $stderrClosed)) { Start-Sleep -Milliseconds 10 }
    }
    [pscustomobject]@{
      exitCode = $process.ExitCode
      stdout = $stdout.ToString()
      stderr = $stderr.ToString()
      stdoutClosed = $stdoutClosed
      stderrClosed = $stderrClosed
      elapsedMilliseconds = $clock.ElapsedMilliseconds
    }
  } finally { $process.Dispose() }
}

function Write-AtomicEvidence([string]$Path, [byte[]]$Bytes) {
  $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
  $backup = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.bak'
  try {
    $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew,
      [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
    try { $stream.Write($Bytes, 0, $Bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
    if (-not [Linq.Enumerable]::SequenceEqual($Bytes, [IO.File]::ReadAllBytes($temporary))) {
      throw 'nsisEvidenceTempReadbackFailed'
    }
    if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, $backup, $true) }
    else { [IO.File]::Move($temporary, $Path) }
    $readback = [IO.File]::ReadAllBytes($Path)
    if (-not [Linq.Enumerable]::SequenceEqual($Bytes, $readback) -or
        (Get-Sha256Hex $Bytes) -cne (Get-Sha256Hex $readback)) {
      throw 'nsisEvidenceFinalReadbackFailed'
    }
  } finally {
    $cleanupFailed = $false
    foreach ($residue in @($temporary, $backup)) {
      try { if (Test-Path -LiteralPath $residue) { Remove-Item -LiteralPath $residue -Force } }
      catch { $cleanupFailed = $true }
    }
    if ($cleanupFailed) { throw 'nsisEvidenceCleanupFailed' }
  }
}

$resolvedCompiler = [IO.Path]::GetFullPath((Get-Item -LiteralPath $MakeNsis).FullName)
$resolvedWorkingDirectory = [IO.Path]::GetFullPath($WorkingDirectory)
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
$version = Invoke-BoundedProcess @('/VERSION') 30000 4096
if ($version.exitCode -ne 0 -or -not $version.stdoutClosed -or -not $version.stderrClosed) {
  throw 'nsisVersionReadFailed'
}
$captured = Invoke-BoundedProcess $CompilerArguments $DeadlineMilliseconds $StreamByteLimit
$stdoutBytes = $utf8.GetBytes($captured.stdout)
$stderrBytes = $utf8.GetBytes($captured.stderr)
$stdoutPath = Join-Path $EvidenceDirectory 'makensis.stdout.log'
$stderrPath = Join-Path $EvidenceDirectory 'makensis.stderr.log'
$compilerItem = Get-Item -LiteralPath $resolvedCompiler
$evidence = [ordered]@{
  schemaVersion = 1
  code = 'nsisCompilerEvidenceCaptured'
  executablePath = $resolvedCompiler
  executableSize = [int64]$compilerItem.Length
  executableSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedCompiler).Hash.ToLowerInvariant()
  version = $version.stdout.Trim()
  workingDirectory = $resolvedWorkingDirectory
  arguments = @($CompilerArguments)
  exitCode = [int]$captured.exitCode
  stdout = [ordered]@{ size = [int64]$stdoutBytes.Length; sha256 = Get-Sha256Hex $stdoutBytes; closed = $captured.stdoutClosed }
  stderr = [ordered]@{ size = [int64]$stderrBytes.Length; sha256 = Get-Sha256Hex $stderrBytes; closed = $captured.stderrClosed }
  elapsedMilliseconds = [int64]$captured.elapsedMilliseconds
}
$manifestPath = Join-Path $EvidenceDirectory 'makensis-result.json'
try {
  Write-AtomicEvidence $stdoutPath $stdoutBytes
  Write-AtomicEvidence $stderrPath $stderrBytes
  Write-AtomicEvidence $manifestPath ($utf8.GetBytes(($evidence | ConvertTo-Json -Depth 8 -Compress)))
  $manifest = [IO.File]::ReadAllText($manifestPath, $utf8) | ConvertFrom-Json
  if ($manifest.exitCode -ne $captured.exitCode -or
      $manifest.stdout.sha256 -cne (Get-Sha256Hex $stdoutBytes) -or
      $manifest.stderr.sha256 -cne (Get-Sha256Hex $stderrBytes)) {
    throw 'nsisEvidenceCorrelationFailed'
  }
} catch {
  if ($captured.stdout.Length -gt 0) { [Console]::Out.Write($captured.stdout) }
  if ($captured.stderr.Length -gt 0) { [Console]::Error.Write($captured.stderr) }
  throw "nsisEvidenceUnavailable:compilerExit=$($captured.exitCode):$($_.Exception.Message)"
}
if ($captured.stdout.Length -gt 0) { [Console]::Out.Write($captured.stdout) }
if ($captured.stderr.Length -gt 0) { [Console]::Error.Write($captured.stderr) }
return [pscustomobject]@{
  code = 'nsisCompilerEvidenceCaptured'
  exitCode = [int]$captured.exitCode
  evidencePath = $manifestPath
}
