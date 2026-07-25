[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string]$DesktopDirectory,
  [Parameter(Mandatory)]
  [string]$SourceRoot
)

$ErrorActionPreference = "Stop"

function Write-Outcome([string]$Code, [bool]$Success) {
  [ordered]@{
    schemaVersion = 1
    code = $Code
    success = $Success
  } | ConvertTo-Json -Compress
}

try {
  $desktop = [IO.Path]::GetFullPath($DesktopDirectory)
  $source = [IO.Path]::GetFullPath($SourceRoot)
  if (-not (Test-Path -LiteralPath $desktop -PathType Container)) {
    throw "desktopPayloadUnavailable"
  }
  if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "sourceRootUnavailable"
  }

  $requiredFiles = @(
    "Ligase.Host.Desktop.exe",
    "Ligase.Host.Desktop.pri",
    "App.xbf",
    "MainWindow.xbf",
    "Microsoft.ui.xaml.dll",
    "Microsoft.WindowsAppRuntime.dll",
    "Microsoft.WindowsAppRuntime.Bootstrap.dll",
    "coreclr.dll",
    "hostfxr.dll"
  )
  foreach ($relative in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $desktop $relative) -PathType Leaf)) {
      throw "desktopRuntimeAssetMissing"
    }
  }

  $xamlRoot = Join-Path $source "src\Ligase.Desktop"
  foreach ($xaml in Get-ChildItem -LiteralPath $xamlRoot -Recurse -Filter "*.xaml" -File) {
    $relative = $xaml.FullName.Substring($xamlRoot.Length + 1)
    if ($relative.StartsWith("bin\", [StringComparison]::OrdinalIgnoreCase) -or
        $relative.StartsWith("obj\", [StringComparison]::OrdinalIgnoreCase)) {
      continue
    }
    $xbf = [IO.Path]::ChangeExtension($relative, ".xbf")
    if (-not (Test-Path -LiteralPath (Join-Path $desktop $xbf) -PathType Leaf)) {
      throw "desktopXamlResourceMissing"
    }
  }

  Write-Outcome "desktopPayloadReady" $true
  exit 0
}
catch {
  $code = if ($_.Exception.Message -match '^[a-zA-Z][a-zA-Z0-9]+$') {
    $_.Exception.Message
  } else {
    "desktopPayloadInvalid"
  }
  Write-Outcome $code $false
  exit 10
}
