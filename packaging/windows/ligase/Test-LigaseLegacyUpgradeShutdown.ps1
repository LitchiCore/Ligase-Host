[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$OutputRoot,
  [ValidateRange(5, 5)][int]$Iterations = 5,
  [switch]$CleanupOnly
)

$ErrorActionPreference = "Stop"
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../.."))
$output = [IO.Path]::GetFullPath($OutputRoot)
if ([IO.Path]::GetPathRoot($output) -cne "D:\") {
  throw "legacyUpgradeOutputRejected"
}
$generations = @(
  [ordered]@{
    id = "final-delivery-02"
    stage = "D:\LitchiCore\Build\ligase-final-delivery-package-20260814-02\work-acfcac85cc9b99f106ce3f7c1af7f4b6e6388f7f-Release-x64\stage"
    manifestSize = 63087
    manifestSha256 = "655274B3EA1DCD32C7F5EDB85CCE0A9B4DD13E8CAA45256ABABA2E5B1E452657"
  },
  [ordered]@{
    id = "shutdown-cursor-03"
    stage = "D:\LitchiCore\Build\ligase-shutdown-cursor-delivery-package-20260814-03\work-acfcac85cc9b99f106ce3f7c1af7f4b6e6388f7f-Release-x64\stage"
    manifestSize = 63087
    manifestSha256 = "8C448C3BF34BB60D092E57090B5D55863588AA9923C71815AAFCB2B183C06123"
  }
)

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
  $start.FileName = $FileName; $start.UseShellExecute = $false
  $start.Arguments = (($Arguments | ForEach-Object {
    ConvertTo-WindowsArgument ([string]$_)
  }) -join ' ')
  $start.CreateNoWindow = $Redirect
  $start.RedirectStandardOutput = $Redirect
  $start.RedirectStandardError = $Redirect
  foreach ($entry in $Environment.GetEnumerator()) {
    $start.EnvironmentVariables[[string]$entry.Key] = [string]$entry.Value
  }
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
  if (-not $process.Start()) { throw "legacyUpgradeProcessStartFailed" }
  return $process
}

function Get-ExactProcesses([string]$Root) {
  $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
  return @(Get-CimInstance Win32_Process | Where-Object {
    -not [string]::IsNullOrWhiteSpace([string]$_.ExecutablePath) -and
    [IO.Path]::GetFullPath([string]$_.ExecutablePath).StartsWith(
      $prefix, [StringComparison]::OrdinalIgnoreCase)
  } | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,CreationDate)
}

function Wait-Role([string]$Root, [string]$Name, [int]$TimeoutMs) {
  $clock = [Diagnostics.Stopwatch]::StartNew()
  while ($clock.ElapsedMilliseconds -lt $TimeoutMs) {
    $found = @(Get-ExactProcesses $Root | Where-Object Name -CEQ $Name)
    if ($found.Count -eq 1) { return $found[0] }
    Start-Sleep -Milliseconds 100
  }
  throw "legacyUpgradeRoleUnavailable:$Name"
}

function Remove-OwnedCase([string]$CaseRoot) {
  $full = [IO.Path]::GetFullPath($CaseRoot)
  if (-not $full.StartsWith($output.TrimEnd('\') + '\',
      [StringComparison]::OrdinalIgnoreCase)) { throw "legacyUpgradeCleanupRejected" }
  if (@(Get-ExactProcesses $full).Count -ne 0) { throw "legacyUpgradeProcessResidue" }
  if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}

if ($CleanupOnly) {
  if (Test-Path -LiteralPath $output -PathType Container) {
    foreach ($child in @(Get-ChildItem -LiteralPath $output -Force)) {
      if ($child.PSIsContainer -and
          $child.Name -match '^(final-delivery-02|shutdown-cursor-03)-[1-5]$') {
        Remove-OwnedCase $child.FullName
      } elseif (-not $child.PSIsContainer -and
          $child.Name -ceq 'legacy-upgrade-summary.json') {
        Remove-Item -LiteralPath $child.FullName -Force
      } else { throw "legacyUpgradeCleanupForeignEntry" }
    }
    Remove-Item -LiteralPath $output -Force
  }
  Start-Sleep -Milliseconds 100
  if (Test-Path -LiteralPath $output) { throw "legacyUpgradeCleanupUnproven" }
  [Console]::Out.WriteLine('{"code":"legacyUpgradeCleanupCompleted","residue":0}')
  exit 0
}

if (Test-Path -LiteralPath $output) {
  if (@(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) {
    throw "legacyUpgradeOutputNotEmpty"
  }
} else { New-Item -ItemType Directory -Path $output | Out-Null }

$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$manage = Join-Path $source 'packaging/windows/ligase/Manage-LigaseInstallation.ps1'
$results = @()
foreach ($generation in $generations) {
  $stage = [IO.Path]::GetFullPath([string]$generation.stage)
  $manifest = Join-Path $stage 'ligase-install-manifest.json'
  if (-not (Test-Path -LiteralPath $manifest -PathType Leaf) -or
      (Get-Item -LiteralPath $manifest).Length -ne [int64]$generation.manifestSize -or
      (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -cne
        [string]$generation.manifestSha256) {
    throw "legacyUpgradeGenerationPinMismatch:$($generation.id)"
  }
  for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
    $caseRoot = Join-Path $output ("{0}-{1}" -f $generation.id,$iteration)
    $product = Join-Path $caseRoot 'Ligase Host'
    $data = Join-Path $caseRoot 'data'
    $evidence = Join-Path $caseRoot 'evidence'
    New-Item -ItemType Directory -Path $caseRoot,$data,$evidence | Out-Null
    Copy-Item -LiteralPath $stage -Destination $product -Recurse
    $port = 52000 + (($results.Count + 1) * 3)
    $coreConfig = Join-Path $data 'legacy-matrix-sunshine.conf'
    $core = Start-TypedProcess (Join-Path $product 'Core\sunshine.exe') @(
      '-1',("port={0}" -f $port),'upnp=disabled',$coreConfig) @{} $true
    $coreOut = $core.StandardOutput.ReadToEndAsync()
    $coreErr = $core.StandardError.ReadToEndAsync()
    $launcher = Start-TypedProcess (Join-Path $product 'Ligase Host.exe') @(
      '--minimized','--data-root',$data) @{
        LIGASE_DATA_ROOT=$data
        LIGASE_SHUTDOWN_VALIDATION_HARNESS='1'
    }
    $watcher = $null
    try {
      $null = Wait-Role $product 'Ligase.Host.Desktop.exe' 20000
      $null = Wait-Role $product 'sunshine.exe' 25000
      $desktop = @(Get-ExactProcesses $product | Where-Object Name -CEQ 'Ligase.Host.Desktop.exe')
      $watcher = Start-TypedProcess (
        Join-Path $product 'Tools\GameWatcher\Ligase.GameWatcher.exe') @(
        '--shutdown-validation-owner-pid',[string]$desktop[0].ProcessId) @{
          LIGASE_SHUTDOWN_VALIDATION_HARNESS='1'
        } $true
      $watcherOut = $watcher.StandardOutput.ReadToEndAsync()
      $watcherErr = $watcher.StandardError.ReadToEndAsync()
      $null = Wait-Role $product 'Ligase.GameWatcher.exe' 5000
      $close = Start-TypedProcess $powershell @(
        '-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass',
        '-File',$manage,'-Action','CloseRunningProduct',
        '-InstallDirectory',$product,'-ShutdownEvidenceRoot',$evidence,
        '-UserConfirmedClose') @{ LIGASE_SHUTDOWN_VALIDATION_HARNESS='1' } $true
      try {
        $stdout = $close.StandardOutput.ReadToEndAsync()
        $stderr = $close.StandardError.ReadToEndAsync()
        if (-not $close.WaitForExit(60000)) { throw "legacyUpgradeCloseDeadlineExceeded" }
        [Threading.Tasks.Task]::WaitAll(
          [Threading.Tasks.Task[]]@($stdout,$stderr), 5000) | Out-Null
        if ($close.ExitCode -ne 0) {
          throw "legacyUpgradeCloseFailed:$($generation.id):${iteration}:$($stdout.Result):$($stderr.Result)"
        }
      } finally { $close.Dispose() }
      if (-not $launcher.WaitForExit(5000)) { throw "legacyUpgradeLauncherResidual" }
      if (-not $core.WaitForExit(5000)) { throw "legacyUpgradeCoreResidual" }
      if (-not $watcher.WaitForExit(5000)) { throw "legacyUpgradeWatcherResidual" }
      $terminals = @(Get-ChildItem -LiteralPath $evidence -Filter 'shutdown-terminal-*.json')
      if ($terminals.Count -ne 1) { throw "legacyUpgradeTerminalCountInvalid" }
      $terminal = Get-Content -LiteralPath $terminals[0].FullName -Raw | ConvertFrom-Json
      $gracefulCompleted = [bool]$terminal.gracefulRestartManagerShutdown.attempted -and
        [string]$terminal.gracefulRestartManagerShutdown.code -ceq 'completed' -and
        -not [bool]$terminal.forcedRestartManagerShutdown.attempted
      $forcedCompleted = [bool]$terminal.gracefulRestartManagerShutdown.attempted -and
        [string]$terminal.gracefulRestartManagerShutdown.code -cne 'completed' -and
        [bool]$terminal.forcedRestartManagerShutdown.eligible -and
        [bool]$terminal.forcedRestartManagerShutdown.attempted -and
        [bool]$terminal.forcedRestartManagerShutdown.forceUsed -and
        [string]$terminal.forcedRestartManagerShutdown.code -ceq 'completed'
      if ($terminal.schemaVersion -ne 3 -or $terminal.code -cne 'productStopped' -or
          -not [bool]$terminal.pipe.ackReceived -or
          -not ($gracefulCompleted -xor $forcedCompleted) -or
          [int]$terminal.programWriteCalls -ne 0 -or
          @($terminal.finalResidual.processes).Count -ne 0 -or
          @($terminal.finalResidual.restartManager.processes).Count -ne 0) {
        throw "legacyUpgradeTerminalInvalid:$($generation.id):$iteration"
      }
      $results += [ordered]@{
        generation=[string]$generation.id; iteration=$iteration
        terminalSha256=(Get-FileHash -LiteralPath $terminals[0].FullName -Algorithm SHA256).Hash
        gracefulNativeCode=[int]$terminal.gracefulRestartManagerShutdown.nativeCode
        shutdownMode=if ($forcedCompleted) { 'forced' } else { 'graceful' }
        forcedNativeCode=if ($null -eq $terminal.forcedRestartManagerShutdown.nativeCode) {
          $null } else { [int]$terminal.forcedRestartManagerShutdown.nativeCode }
        forcedTargets=@($terminal.forcedRestartManagerShutdown.targets).Count
        processResidue=0; restartManagerResidue=0; programWriteCalls=0
      }
    } finally {
      foreach ($owned in @(Get-ExactProcesses $product | Sort-Object ProcessId -Descending)) {
        try {
          $process = Get-Process -Id ([int]$owned.ProcessId) -ErrorAction Stop
          try { $process.Kill(); $process.WaitForExit(3000) | Out-Null }
          finally { $process.Dispose() }
        } catch { }
      }
      if ($null -ne $watcher) { $watcher.Dispose() }
      $core.Dispose()
      $launcher.Dispose()
    }
    if (@(Get-ExactProcesses $product).Count -ne 0) { throw "legacyUpgradeProcessResidue" }
    Remove-OwnedCase $caseRoot
  }
}

$summary = [ordered]@{
  code='legacyUpgradeShutdownMatrixPassed'
  generations=@($generations | ForEach-Object { [string]$_.id })
  iterationsPerGeneration=$Iterations
  results=$results
  gracefulRuns=@($results | Where-Object shutdownMode -CEQ 'graceful').Count
  forcedRuns=@($results | Where-Object shutdownMode -CEQ 'forced').Count
  processResidue=0
  caseRootResidue=0
  installedProductTouched=$false
  systemMutation=$false
}
[IO.File]::WriteAllText((Join-Path $output 'legacy-upgrade-summary.json'),
  ($summary | ConvertTo-Json -Depth 12 -Compress),
  [Text.UTF8Encoding]::new($false))
$summary | ConvertTo-Json -Depth 12 -Compress
