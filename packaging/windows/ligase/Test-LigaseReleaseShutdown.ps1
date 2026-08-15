[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$DotNetPath,
  [Parameter(Mandatory)][string]$StageTemplate,
  [Parameter(Mandatory)][string]$OutputRoot,
  [ValidateRange(5, 10)][int]$Iterations = 5
)

$ErrorActionPreference = "Stop"
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../.."))
$output = [IO.Path]::GetFullPath($OutputRoot)
$template = [IO.Path]::GetFullPath($StageTemplate)
if ([IO.Path]::GetPathRoot($output) -cne "D:\" -or
    -not (Test-Path -LiteralPath $DotNetPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $template "ligase-install-manifest.json") -PathType Leaf)) {
  throw "releaseShutdownInputInvalid"
}
if (Test-Path -LiteralPath $output) {
  if (@(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) {
    throw "releaseShutdownOutputNotEmpty"
  }
} else {
  New-Item -ItemType Directory -Path $output | Out-Null
}

function ConvertTo-WindowsArgument([string]$Value) {
  if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
  $builder = [Text.StringBuilder]::new(); [void]$builder.Append('"')
  $slashes = 0
  foreach ($character in $Value.ToCharArray()) {
    if ($character -eq '\') { $slashes++; continue }
    if ($character -eq '"') {
      [void]$builder.Append(('\' * (($slashes * 2) + 1)))
      [void]$builder.Append('"'); $slashes = 0; continue
    }
    [void]$builder.Append(('\' * $slashes)); $slashes = 0
    [void]$builder.Append($character)
  }
  [void]$builder.Append(('\' * ($slashes * 2))); [void]$builder.Append('"')
  return $builder.ToString()
}

function Start-TypedProcess(
  [string]$FileName, [string[]]$Arguments, [hashtable]$Environment = @{},
  [bool]$Redirect = $false
) {
  $start = [Diagnostics.ProcessStartInfo]::new()
  $start.FileName = $FileName
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $false
  $start.Arguments = (($Arguments | ForEach-Object {
    ConvertTo-WindowsArgument ([string]$_)
  }) -join ' ')
  if ($Redirect) {
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.CreateNoWindow = $true
  }
  foreach ($entry in $Environment.GetEnumerator()) {
    $start.EnvironmentVariables[[string]$entry.Key] = [string]$entry.Value
  }
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
  if (-not $process.Start()) { throw "releaseShutdownProcessStartFailed" }
  return $process
}

function Invoke-Checked([string]$FileName, [string[]]$Arguments) {
  $process = Start-TypedProcess $FileName $Arguments @{} $true
  try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(180000)) {
      throw "releaseShutdownBuildDeadlineExceeded"
    }
    [Threading.Tasks.Task]::WaitAll(
      [Threading.Tasks.Task[]]@($stdout, $stderr), 5000) | Out-Null
    if ($process.ExitCode -ne 0) {
      throw "releaseShutdownToolFailed:$($process.ExitCode):$($stdout.Result):$($stderr.Result)"
    }
    return [pscustomobject]@{ Stdout=$stdout.Result; Stderr=$stderr.Result }
  } finally { $process.Dispose() }
}

function Get-ExactProductProcesses([string]$Root) {
  $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
  return @(Get-CimInstance Win32_Process | Where-Object {
    -not [string]::IsNullOrWhiteSpace([string]$_.ExecutablePath) -and
    [IO.Path]::GetFullPath([string]$_.ExecutablePath).StartsWith(
      $prefix, [StringComparison]::OrdinalIgnoreCase)
  } | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,CreationDate)
}

function Wait-ExactProcess([string]$Root, [string]$Name, [int]$TimeoutMs) {
  $clock = [Diagnostics.Stopwatch]::StartNew()
  while ($clock.ElapsedMilliseconds -lt $TimeoutMs) {
    $match = @(Get-ExactProductProcesses $Root | Where-Object Name -CEQ $Name)
    if ($match.Count -eq 1) { return $match[0] }
    Start-Sleep -Milliseconds 100
  }
  throw "releaseShutdownProcessUnavailable:$Name"
}

if (@(Get-Process -Name "Ligase Host","Ligase.Host.Desktop","sunshine","Ligase.GameWatcher" `
    -ErrorAction SilentlyContinue).Count -ne 0) {
  throw "releaseShutdownForeignProductRunning"
}

$build = Join-Path $output "build"
$buildSource = Join-Path $output "source"
$desktopOut = Join-Path $build "desktop"
$launcherOut = Join-Path $build "launcher"
$watcherOut = Join-Path $build "watcher"
New-Item -ItemType Directory -Path $desktopOut,$launcherOut,$watcherOut | Out-Null
$robocopy = Join-Path $env:SystemRoot 'System32\robocopy.exe'
$copy = Start-TypedProcess $robocopy @(
  $source,$buildSource,'/E','/COPY:DAT','/DCOPY:DAT','/R:1','/W:1',
  '/XD','.git','bin','obj') @{} $true
try {
  $copyOut = $copy.StandardOutput.ReadToEndAsync()
  $copyErr = $copy.StandardError.ReadToEndAsync()
  if (-not $copy.WaitForExit(120000)) { throw 'releaseShutdownSourceCopyTimeout' }
  [Threading.Tasks.Task]::WaitAll(
    [Threading.Tasks.Task[]]@($copyOut,$copyErr), 5000) | Out-Null
  if ($copy.ExitCode -gt 7) {
    throw "releaseShutdownSourceCopyFailed:$($copy.ExitCode):$($copyErr.Result)"
  }
} finally { $copy.Dispose() }
$common = @('-c','Release','-r','win-x64','--self-contained','true',
  '-p:Platform=x64','-p:UseSharedCompilation=false','-nodeReuse:false')
$null = Invoke-Checked $DotNetPath (@('publish',
  (Join-Path $buildSource 'src/Ligase.Desktop/Ligase.Host.Desktop.csproj')) +
  $common + @('-p:LigaseStructuredPackage=true',
    '-p:UseArtifactsOutput=true',
    ("-p:ArtifactsPath={0}" -f (Join-Path $build 'artifacts/desktop')),
    '-o',$desktopOut))
$null = Invoke-Checked $DotNetPath (@('publish',
  (Join-Path $buildSource 'tools/Ligase.Host.Launcher/Ligase.Host.Launcher.csproj')) +
  $common + @('-p:PublishAot=true',
    '-p:UseArtifactsOutput=true',
    ("-p:ArtifactsPath={0}" -f (Join-Path $build 'artifacts/launcher')),
    '-o',$launcherOut))
$null = Invoke-Checked $DotNetPath (@('publish',
  (Join-Path $buildSource 'tools/Ligase.GameWatcher/Ligase.GameWatcher.csproj')) +
  $common + @(
    '-p:UseArtifactsOutput=true',
    ("-p:ArtifactsPath={0}" -f (Join-Path $build 'artifacts/watcher')),
    '-o',$watcherOut))

$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$manage = Join-Path $buildSource 'packaging/windows/ligase/Manage-LigaseInstallation.ps1'
$results = @()
for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
  $caseRoot = Join-Path $output ("run-{0}" -f $iteration)
  $product = Join-Path $caseRoot 'Ligase Host'
  $data = Join-Path $caseRoot 'data'
  $evidence = Join-Path $output ("evidence-{0}" -f $iteration)
  New-Item -ItemType Directory -Path $caseRoot,$data,$evidence | Out-Null
  Copy-Item -LiteralPath $template -Destination $product -Recurse
  Remove-Item -LiteralPath (Join-Path $product 'Desktop') -Recurse -Force
  Remove-Item -LiteralPath (Join-Path $product 'Tools/GameWatcher') -Recurse -Force
  New-Item -ItemType Directory -Path (Join-Path $product 'Desktop'),(
    Join-Path $product 'Tools/GameWatcher') | Out-Null
  Get-ChildItem -LiteralPath $desktopOut -Force | Copy-Item -Destination (
    Join-Path $product 'Desktop') -Recurse -Force
  Get-ChildItem -LiteralPath $watcherOut -Force | Copy-Item -Destination (
    Join-Path $product 'Tools/GameWatcher') -Recurse -Force
  Copy-Item -LiteralPath (Join-Path $launcherOut 'Ligase Host.exe') -Destination (
    Join-Path $product 'Ligase Host.exe') -Force

  $manifestPath = Join-Path $product 'ligase-install-manifest.json'
  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  foreach ($artifact in @($manifest.artifacts)) {
    $path = Join-Path $product ([string]$artifact.relativePath).Replace('/','\')
    if ([string]$artifact.role -in @('launcher','desktop','gameWatcher')) {
      $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
      $artifact.unsignedContentSha256 = $hash
      $artifact.signedArtifactSha256 = $hash
      $artifact.size = (Get-Item -LiteralPath $path).Length
    }
  }
  [IO.File]::WriteAllText($manifestPath,
    ($manifest | ConvertTo-Json -Depth 20 -Compress),
    [Text.UTF8Encoding]::new($false))

  $launcher = Start-TypedProcess (Join-Path $product 'Ligase Host.exe') @(
    '--minimized','--data-root',$data) @{
      LIGASE_DATA_ROOT=$data
      LIGASE_SHUTDOWN_VALIDATION_HARNESS='1'
    }
  try {
    $desktop = Wait-ExactProcess $product 'Ligase.Host.Desktop.exe' 15000
    $core = Wait-ExactProcess $product 'sunshine.exe' 20000
    $watcher = Start-TypedProcess (
      Join-Path $product 'Tools/GameWatcher/Ligase.GameWatcher.exe') @(
      '--shutdown-validation-owner-pid',[string]$desktop.ProcessId) @{
        LIGASE_SHUTDOWN_VALIDATION_HARNESS='1'
      }
    try {
      $null = Wait-ExactProcess $product 'Ligase.GameWatcher.exe' 5000
      $close = Start-TypedProcess $powershell @(
        '-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass',
        '-File',$manage,'-Action','CloseRunningProduct',
        '-InstallDirectory',$product,'-ShutdownEvidenceRoot',$evidence,
        '-UserConfirmedClose') @{
          LIGASE_SHUTDOWN_VALIDATION_HARNESS='1'
        } $true
      try {
        $stdout = $close.StandardOutput.ReadToEndAsync()
        $stderr = $close.StandardError.ReadToEndAsync()
        if (-not $close.WaitForExit(20000)) { throw "releaseShutdownCloseDeadlineExceeded" }
        [Threading.Tasks.Task]::WaitAll(
          [Threading.Tasks.Task[]]@($stdout,$stderr), 3000) | Out-Null
        if ($close.ExitCode -ne 0) {
          throw "releaseShutdownCloseFailed:$($close.ExitCode):$($stdout.Result):$($stderr.Result)"
        }
      } finally { $close.Dispose() }
      if (-not $launcher.WaitForExit(5000)) { throw "releaseShutdownLauncherResidual" }
      if (-not $watcher.WaitForExit(5000)) { throw "releaseShutdownWatcherResidual" }
      Start-Sleep -Milliseconds 1000
      $remaining = @(Get-ExactProductProcesses $product)
      if ($remaining.Count -ne 0) { throw "releaseShutdownProcessResidual" }
      $terminalPath = @(Get-ChildItem -LiteralPath $evidence -Filter 'shutdown-terminal-*.json')
      if ($terminalPath.Count -ne 1) { throw "releaseShutdownTerminalCountInvalid" }
      $terminal = Get-Content -Raw -LiteralPath $terminalPath[0].FullName | ConvertFrom-Json
      if ($terminal.code -cne 'productStopped' -or
          -not [bool]$terminal.pipe.ackReceived -or
          [string]$terminal.pipe.desktopTerminalCode -notin @('exitCommitted','exitAlreadyCommitted') -or
          @($terminal.finalResidual.processes).Count -ne 0 -or
          @($terminal.finalResidual.restartManager.processes).Count -ne 0 -or
          [bool]$terminal.gracefulRestartManagerShutdown.forceUsed -or
          [bool]$terminal.forcedRestartManagerShutdown.forceUsed) {
        throw "releaseShutdownTerminalInvalid"
      }
      $results += [ordered]@{
        iteration=$iteration
        code=[string]$terminal.code
        desktopTerminalCode=[string]$terminal.pipe.desktopTerminalCode
        cleanupState=[string]$terminal.pipe.desktopCleanupState
        coreStopCode=[string]$terminal.pipe.desktopCoreStopCode
        restartManagerAttempted=[bool]$terminal.restartManagerShutdown.attempted
        elapsedMilliseconds=[long]$terminal.finalResidual.elapsedMilliseconds
        processResidue=0
        restartObserved=$false
      }
    } finally {
      if (-not $watcher.HasExited) {
        # Failure-only cleanup of this D-only owned validation process. The
        # product and installer normal shutdown paths contain no forced exit.
        $watcher.Kill(); $watcher.WaitForExit(3000) | Out-Null
      }
      $watcher.Dispose()
    }
  } finally {
    if (-not $launcher.HasExited) {
      foreach ($owned in @(Get-ExactProductProcesses $product | Sort-Object ProcessId -Descending)) {
        try {
          $process = Get-Process -Id ([int]$owned.ProcessId) -ErrorAction Stop
          try { $process.Kill(); $process.WaitForExit(3000) | Out-Null }
          finally { $process.Dispose() }
        } catch { }
      }
    }
    $launcher.Dispose()
  }
  Remove-Item -LiteralPath $caseRoot -Recurse -Force
}

$summary = [ordered]@{
  code='releaseProductShutdownGatePassed'
  iterations=$Iterations
  results=$results
  processResidue=0
  productRootResidue=0
  forceUsed=$false
}
[IO.File]::WriteAllText((Join-Path $output 'release-shutdown-summary.json'),
  ($summary | ConvertTo-Json -Depth 10 -Compress),
  [Text.UTF8Encoding]::new($false))
$summary | ConvertTo-Json -Depth 10 -Compress
