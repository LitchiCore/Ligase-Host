[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
  [string] $InstallerPath,
  [Parameter(Mandatory)]
  [string] $InstallDirectory,
  [Parameter(Mandatory)]
  [string] $DataRoot,
  [switch] $Wait,
  [switch] $NoElevation
)

$ErrorActionPreference = "Stop"

function ConvertTo-WindowsCommandLineArgument(
  [Parameter(Mandatory)][AllowEmptyString()][string] $Value
) {
  if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') {
    return $Value
  }
  $builder = [Text.StringBuilder]::new()
  [void]$builder.Append('"')
  $backslashes = 0
  foreach ($character in $Value.ToCharArray()) {
    if ($character -eq '\') {
      $backslashes++
      continue
    }
    if ($character -eq '"') {
      [void]$builder.Append(('\' * (($backslashes * 2) + 1)))
      [void]$builder.Append('"')
      $backslashes = 0
      continue
    }
    [void]$builder.Append(('\' * $backslashes))
    $backslashes = 0
    [void]$builder.Append($character)
  }
  [void]$builder.Append(('\' * ($backslashes * 2)))
  [void]$builder.Append('"')
  return $builder.ToString()
}

$arguments = @(
  "/InstallDirectory=$InstallDirectory",
  "/DataRoot=$DataRoot"
)
$serialized = ($arguments |
  ForEach-Object { ConvertTo-WindowsCommandLineArgument $_ }) -join " "

# This is the only supported PowerShell automation seam. One tested serializer
# owns Windows quoting; callers never concatenate or pre-quote native argv.
$startParameters = @{
  FilePath = [IO.Path]::GetFullPath($InstallerPath)
  ArgumentList = $serialized
  PassThru = $true
}
if (-not $NoElevation) {
  $startParameters.Verb = "RunAs"
}
$process = Start-Process @startParameters
if ($null -eq $process) {
  throw "installerStartFailed"
}
if ($Wait) {
  $process.WaitForExit()
  exit $process.ExitCode
}

[ordered]@{
  code = "installerStarted"
  processId = $process.Id
} | ConvertTo-Json -Compress
