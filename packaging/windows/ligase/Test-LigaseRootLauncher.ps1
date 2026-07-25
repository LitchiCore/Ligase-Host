[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string]$LauncherPath
)

$ErrorActionPreference = "Stop"
$tempAuthority = if ([string]::IsNullOrWhiteSpace($env:LIGASE_TEMP_ROOT)) {
  [IO.Path]::GetTempPath()
} else {
  [IO.Path]::GetFullPath($env:LIGASE_TEMP_ROOT)
}
$fixture = Join-Path $tempAuthority (
  "ligase-launcher-runtime-" + [Guid]::NewGuid().ToString("N"))
$root = Join-Path $fixture "Ligase Host 测试"
$desktop = Join-Path $root "Desktop"
$cwd = Join-Path $fixture "random cwd"
$marker = Join-Path $fixture "child-cwd.txt"

try {
  New-Item -ItemType Directory -Path $desktop, $cwd -Force | Out-Null
  Copy-Item -LiteralPath $LauncherPath -Destination (Join-Path $root "Ligase Host.exe")
  Copy-Item -LiteralPath "$env:SystemRoot\System32\cmd.exe" `
    -Destination (Join-Path $desktop "Ligase.Host.Desktop.exe")
  $desktopHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (
    Join-Path $desktop "Ligase.Host.Desktop.exe")).Hash.ToLowerInvariant()
  $manifest = [ordered]@{
    schemaVersion = 1
    installLayout = "structured-v1"
    artifacts = @(
      [ordered]@{
        role = "desktop"
        relativePath = "Desktop/Ligase.Host.Desktop.exe"
        signedArtifactSha256 = $desktopHash
      }
    )
  }
  [IO.File]::WriteAllText(
    (Join-Path $root "ligase-install-manifest.json"),
    ($manifest | ConvertTo-Json -Depth 4 -Compress),
    [Text.UTF8Encoding]::new($false))

  $process = Start-Process `
    -FilePath (Join-Path $root "Ligase Host.exe") `
    -WorkingDirectory $cwd `
    -ArgumentList @("/d", "/c", "cd > `"$marker`"") `
    -PassThru `
    -Wait
  if ($null -eq $process) {
    throw "launcherStartFailed"
  }
  $process.WaitForExit()
  if ($process.ExitCode -ne 0) {
    throw "launcherChildExitMismatch"
  }
  if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or
      (Get-Content -Raw -LiteralPath $marker).Trim() -cne $cwd) {
    throw "launcherWorkingDirectoryMismatch"
  }
  [ordered]@{
    schemaVersion = 1
    code = "launcherRuntimeReady"
    success = $true
  } | ConvertTo-Json -Compress
  exit 0
}
catch {
  [ordered]@{
    schemaVersion = 1
    code = "launcherRuntimeFailed"
    success = $false
  } | ConvertTo-Json -Compress
  exit 10
}
finally {
  if (Test-Path -LiteralPath $fixture) {
    Remove-Item -LiteralPath $fixture -Recurse -Force
  }
}
