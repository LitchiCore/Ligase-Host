[CmdletBinding()]
param(
  [ValidateSet("Debug", "Release")]
  [string]$Configuration = "Release",
  [ValidateSet("x64")]
  [string]$Platform = "x64",
  [string]$CppBuildRoot,
  [string]$OutputRoot,
  [string]$DotNet = "dotnet.exe",
  [string]$CMake = "cmake.exe",
  [string]$MakeNsis = "makensis.exe",
  [ValidateSet("UnsignedDev", "PublicRelease")]
  [string]$ReleaseKind = "UnsignedDev",
  [string]$SigningTool,
  [string[]]$AllowedPublisher = @(),
  [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

function Get-CanonicalRelativePath(
  [Parameter(Mandatory)][string]$BaseDirectory,
  [Parameter(Mandatory)][string]$Path
) {
  $base = [IO.Path]::GetFullPath($BaseDirectory).TrimEnd("\") + "\"
  $value = [IO.Path]::GetFullPath($Path)
  if (-not $value.StartsWith(
      $base,
      [StringComparison]::OrdinalIgnoreCase)) {
    throw "packagePathEscapesRoot"
  }
  return $value.Substring($base.Length).Replace("\", "/")
}

$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
. (Join-Path $sourceRoot "scripts/ligase/Resolve-DevelopmentRoot.ps1")
$developmentBuildRoot = Resolve-LigaseBuildRoot
$shortHead = (& git -C $sourceRoot rev-parse --short=8 HEAD).Trim()
if ([string]::IsNullOrWhiteSpace($CppBuildRoot)) {
  $CppBuildRoot = Join-Path $developmentBuildRoot "build\$shortHead"
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
  $OutputRoot = Join-Path $developmentBuildRoot "artifacts\fresh-install"
}
$cppRoot = [IO.Path]::GetFullPath($CppBuildRoot)
$output = [IO.Path]::GetFullPath($OutputRoot)
$head = (& git -C $sourceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
  throw "sourceHeadUnavailable"
}

$work = Join-Path $output "work-$head-$Configuration-$Platform"
$desktop = Join-Path $work "desktop"
$watcher = Join-Path $work "watcher"
$launcher = Join-Path $work "launcher"
$stage = Join-Path $work "stage"
$label = if ($ReleaseKind -eq "UnsignedDev") { "UNSIGNED-DEV" } else { "release" }
$package = Join-Path $output "Ligase-Host-$head-$Configuration-$Platform-$label-installer.exe"
New-Item -ItemType Directory -Path $output -Force | Out-Null
if ($ReleaseKind -eq "PublicRelease" -and (
    [string]::IsNullOrWhiteSpace($SigningTool) -or
    -not (Test-Path -LiteralPath $SigningTool -PathType Leaf) -or
    $AllowedPublisher.Count -eq 0)) {
  throw "publicSigningSeamUnavailable"
}

if (-not $SkipBuild) {
  & $CMake --build $cppRoot --config $Configuration --target sunshine --parallel
  if ($LASTEXITCODE -ne 0) { throw "coreBuildFailed" }
  & $DotNet publish (Join-Path $sourceRoot "src/Ligase.Desktop/Ligase.Host.Desktop.csproj") `
    -c $Configuration -p:Platform=$Platform -p:LigaseStructuredPackage=true `
    -r win-x64 --self-contained true -o $desktop
  if ($LASTEXITCODE -ne 0) { throw "desktopPublishFailed" }
  $desktopPayloadValidation = & (Join-Path $PSScriptRoot "Test-LigaseDesktopPayload.ps1") `
    -DesktopDirectory $desktop -SourceRoot $sourceRoot
  if ($LASTEXITCODE -ne 0) {
    throw "desktopPayloadValidationFailed:$desktopPayloadValidation"
  }
  & $DotNet publish (Join-Path $sourceRoot "tools/Ligase.GameWatcher/Ligase.GameWatcher.csproj") `
    -c $Configuration -p:Platform=$Platform -r win-x64 --self-contained true -o $watcher
  if ($LASTEXITCODE -ne 0) { throw "gameWatcherPublishFailed" }
  & $DotNet publish (Join-Path $sourceRoot "tools/Ligase.Host.Launcher/Ligase.Host.Launcher.csproj") `
    -c $Configuration -p:LigaseLauncherNative=true -r win-x64 --self-contained true -o $launcher
  if ($LASTEXITCODE -ne 0) { throw "launcherPublishFailed" }
  $launcherRuntimeValidation = & (Join-Path $PSScriptRoot "Test-LigaseRootLauncher.ps1") `
    -LauncherPath (Join-Path $launcher "Ligase Host.exe")
  if ($LASTEXITCODE -ne 0) {
    throw "launcherRuntimeValidationFailed:$launcherRuntimeValidation"
  }
}

$coreBinary = Join-Path $cppRoot "sunshine.exe"
$desktopBinary = Join-Path $desktop "Ligase.Host.Desktop.exe"
$watcherBinary = Join-Path $watcher "Ligase.GameWatcher.exe"
$launcherBinary = Join-Path $launcher "Ligase Host.exe"
foreach ($entry in @(
  @{ code = "launcherArtifactMissing"; path = $launcherBinary },
  @{ code = "desktopArtifactMissing"; path = $desktopBinary },
  @{ code = "managedCoreArtifactMissing"; path = $coreBinary },
  @{ code = "gameWatcherArtifactMissing"; path = $watcherBinary }
)) {
  if (-not (Test-Path -LiteralPath $entry.path -PathType Leaf)) {
    throw $entry.code
  }
}

$temporaryStage = "$stage.pending"
if (Test-Path -LiteralPath $temporaryStage) {
  Remove-Item -LiteralPath $temporaryStage -Recurse -Force
}
New-Item -ItemType Directory -Path $temporaryStage | Out-Null
Copy-Item -LiteralPath $launcherBinary -Destination (Join-Path $temporaryStage "Ligase Host.exe")
New-Item -ItemType Directory -Path (Join-Path $temporaryStage "Desktop") | Out-Null
Get-ChildItem -LiteralPath $desktop | ForEach-Object {
  Copy-Item -LiteralPath $_.FullName `
    -Destination (Join-Path $temporaryStage "Desktop") -Recurse -Force
}
Get-ChildItem -LiteralPath (Join-Path $temporaryStage "Desktop") `
  -Filter "Ligase.GameWatcher.*" -File |
  Remove-Item -Force
New-Item -ItemType Directory -Path (Join-Path $temporaryStage "Tools/GameWatcher") | Out-Null
Get-ChildItem -LiteralPath $watcher | ForEach-Object {
  Copy-Item -LiteralPath $_.FullName `
    -Destination (Join-Path $temporaryStage "Tools/GameWatcher") -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $temporaryStage "Core") | Out-Null
Copy-Item -LiteralPath $coreBinary -Destination (Join-Path $temporaryStage "Core/sunshine.exe")
$coreAssets = Join-Path $cppRoot "assets"
if (-not (Test-Path -LiteralPath $coreAssets -PathType Container)) {
  throw "managedCoreAssetsMissing"
}
Copy-Item -LiteralPath $coreAssets -Destination (Join-Path $temporaryStage "Core/assets") -Recurse
New-Item -ItemType Directory -Path (Join-Path $temporaryStage "Deployment/Drivers") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceRoot "src_assets/windows/drivers/sudovda") `
  -Destination (Join-Path $temporaryStage "Deployment/Drivers/sudovda") -Recurse
Copy-Item -LiteralPath (Join-Path $sourceRoot "src_assets/windows/misc/firewall") `
  -Destination (Join-Path $temporaryStage "Deployment/Firewall") -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Manage-LigaseInstallation.ps1") `
  -Destination (Join-Path $temporaryStage "Deployment")
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Resolve-LigaseInstallDirectory.ps1") `
  -Destination (Join-Path $temporaryStage "Deployment")

$artifactDefinitions = @(
  @{ role = "launcher"; relativePath = "Ligase Host.exe" },
  @{ role = "desktop"; relativePath = "Desktop/Ligase.Host.Desktop.exe" },
  @{ role = "managedCore"; relativePath = "Core/sunshine.exe" },
  @{ role = "gameWatcher"; relativePath = "Tools/GameWatcher/Ligase.GameWatcher.exe" }
)
$artifacts = $artifactDefinitions | ForEach-Object {
  $artifactPath = Join-Path $temporaryStage $_.relativePath
  $unsignedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $artifactPath).Hash.ToLowerInvariant()
  if ($ReleaseKind -eq "PublicRelease") {
    & $SigningTool $artifactPath
    if ($LASTEXITCODE -ne 0) { throw "artifactSigningFailed" }
  }
  $signature = Get-AuthenticodeSignature -LiteralPath $artifactPath
  if ($ReleaseKind -eq "PublicRelease" -and (
      $signature.Status -ne "Valid" -or
      $null -eq $signature.TimeStamperCertificate -or
      $AllowedPublisher -notcontains $signature.SignerCertificate.Subject)) {
    throw "artifactSignatureInvalid"
  }
  [ordered]@{
    role = $_.role
    relativePath = $_.relativePath
    unsignedContentSha256 = $unsignedHash
    signedArtifactSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $artifactPath).Hash.ToLowerInvariant()
    size = (Get-Item -LiteralPath $artifactPath).Length
    version = [Diagnostics.FileVersionInfo]::GetVersionInfo($artifactPath).FileVersion
    signature = [ordered]@{
      status = if ($ReleaseKind -eq "PublicRelease") { "valid" } else { "nonRelease" }
      signerSubject = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
      signerThumbprint = if ($signature.SignerCertificate) { $signature.SignerCertificate.Thumbprint } else { $null }
      timestamped = $null -ne $signature.TimeStamperCertificate
    }
  }
}
$helperDefinitions = @(
  "Deployment/Manage-LigaseInstallation.ps1",
  "Deployment/Resolve-LigaseInstallDirectory.ps1",
  "Deployment/Firewall/Manage-LigaseFirewall.ps1"
)
$privilegedHelpers = $helperDefinitions | ForEach-Object {
  $helperPath = Join-Path $temporaryStage $_
  $unsignedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $helperPath).Hash.ToLowerInvariant()
  if ($ReleaseKind -eq "PublicRelease") {
    & $SigningTool $helperPath
    if ($LASTEXITCODE -ne 0) { throw "helperSigningFailed" }
  }
  $signature = Get-AuthenticodeSignature -LiteralPath $helperPath
  if ($ReleaseKind -eq "PublicRelease" -and (
      $signature.Status -ne "Valid" -or
      $null -eq $signature.TimeStamperCertificate -or
      $AllowedPublisher -notcontains $signature.SignerCertificate.Subject)) {
    throw "helperSignatureInvalid"
  }
  [ordered]@{
    relativePath = $_
    unsignedContentSha256 = $unsignedHash
    signedArtifactSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $helperPath).Hash.ToLowerInvariant()
    signerSubject = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
    signerThumbprint = if ($signature.SignerCertificate) { $signature.SignerCertificate.Thumbprint } else { $null }
    timestamped = $null -ne $signature.TimeStamperCertificate
  }
}
$driverSignature = Get-AuthenticodeSignature -LiteralPath (
  Join-Path $temporaryStage "Deployment/Drivers/sudovda/sudovda.cat")
$driverCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
  (Join-Path $temporaryStage "Deployment/Drivers/sudovda/sudovda.cer"))
$driverSelfSigned = $driverCertificate.Subject -ceq $driverCertificate.Issuer
$driverExpired = [DateTime]::UtcNow -lt $driverCertificate.NotBefore.ToUniversalTime() -or
  [DateTime]::UtcNow -gt $driverCertificate.NotAfter.ToUniversalTime()
$driverTrust = if ($driverExpired) { "expired" }
  elseif ($driverSelfSigned -and $driverSignature.Status -eq "Valid") {
    "locallyTrustedSelfSigned"
  }
  elseif ($driverSelfSigned) { "untrusted" }
  elseif ($driverSignature.Status -eq "Valid") { "caTrusted" }
  else { "untrusted" }
if ($ReleaseKind -eq "PublicRelease" -and (
    $driverTrust -ne "caTrusted" -or
    $null -eq $driverSignature.TimeStamperCertificate)) {
  throw "virtualDisplaySignatureInvalid"
}
$manifest = [ordered]@{
  schemaVersion = 1
  installLayout = "structured-v1"
  sourceHead = $head
  configuration = $Configuration
  platform = $Platform
  installMode = "packaged"
  releaseKind = $ReleaseKind
  artifacts = $artifacts
  privilegedHelpers = $privilegedHelpers
  virtualDisplay = [ordered]@{
    required = $false
    installer = "Deployment/Drivers/sudovda/install.bat"
    uninstaller = "Deployment/Drivers/sudovda/uninstall.bat"
    catalogSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (
      Join-Path $temporaryStage "Deployment/Drivers/sudovda/sudovda.cat")).Hash.ToLowerInvariant()
    signerSubject = if ($driverSignature.SignerCertificate) {
      $driverSignature.SignerCertificate.Subject
    } else { $null }
    signerThumbprint = if ($driverSignature.SignerCertificate) {
      $driverSignature.SignerCertificate.Thumbprint
    } else { $null }
    certificateThumbprint = $driverCertificate.Thumbprint
    subject = $driverCertificate.Subject
    issuer = $driverCertificate.Issuer
    selfSigned = $driverSelfSigned
    timestamped = $null -ne $driverSignature.TimeStamperCertificate
    trust = $driverTrust
  }
  firewall = [ordered]@{
    required = $false
    manifest = "Deployment/Firewall/ligase-firewall-v1.json"
    script = "Deployment/Firewall/Manage-LigaseFirewall.ps1"
    basePort = 48989
  }
  encoder = [ordered]@{
    requiredForInstall = $false
    requiredForStreaming = $true
    probe = "managedCoreRuntime"
  }
  ownedEntries = @(
    Get-ChildItem -LiteralPath $temporaryStage -Recurse -File |
      ForEach-Object {
        Get-CanonicalRelativePath $temporaryStage $_.FullName
      } |
      Sort-Object
    "ligase-install-manifest.json"
  )
  legacyFlatOwnedEntries = @(
    Get-ChildItem -LiteralPath $desktop -Recurse -File |
      ForEach-Object {
        Get-CanonicalRelativePath $desktop $_.FullName
      }
    Get-ChildItem -LiteralPath $watcher -Recurse -File |
      ForEach-Object {
        Get-CanonicalRelativePath $watcher $_.FullName
      }
    "Apollo/sunshine.exe"
    Get-ChildItem -LiteralPath $coreAssets -Recurse -File |
      ForEach-Object {
        "Apollo/assets/" +
          (Get-CanonicalRelativePath $coreAssets $_.FullName)
      }
    $legacyDriverRoot = Join-Path $sourceRoot "src_assets/windows/drivers/sudovda"
    Get-ChildItem -LiteralPath $legacyDriverRoot -Recurse -File |
      ForEach-Object {
        "Drivers/sudovda/" +
          (Get-CanonicalRelativePath $legacyDriverRoot $_.FullName)
      }
    "Manage-LigaseInstallation.ps1"
    "Firewall/Manage-LigaseFirewall.ps1"
    "Firewall/ligase-firewall-v1.json"
  ) | Sort-Object -Unique
}
[IO.File]::WriteAllText(
  (Join-Path $temporaryStage "ligase-install-manifest.json"),
  ($manifest | ConvertTo-Json -Depth 8 -Compress),
  [Text.UTF8Encoding]::new($false))

& (Join-Path $temporaryStage "Deployment/Manage-LigaseInstallation.ps1") `
  -Action Readback -InstallDirectory $temporaryStage | Out-Null
if ($LASTEXITCODE -ne 0) { throw "stagingReadbackFailed" }

if (Test-Path -LiteralPath $stage) {
  Remove-Item -LiteralPath $stage -Recurse -Force
}
Move-Item -LiteralPath $temporaryStage -Destination $stage
$nsisArguments = @(
  "/DStageDir=$stage",
  "/DOutputFile=$package"
)
if ($ReleaseKind -eq "PublicRelease") {
  $nsisArguments += "/DSignerTool=$SigningTool"
}
$nsisArguments += (Join-Path $PSScriptRoot "LigaseHost.nsi")
& $MakeNsis @nsisArguments
if ($LASTEXITCODE -ne 0) { throw "nsisBuildFailed" }
$installerSignature = Get-AuthenticodeSignature -LiteralPath $package
if ($ReleaseKind -eq "PublicRelease" -and (
    $installerSignature.Status -ne "Valid" -or
    $null -eq $installerSignature.TimeStamperCertificate -or
    $AllowedPublisher -notcontains $installerSignature.SignerCertificate.Subject)) {
  throw "installerSignatureInvalid"
}

[ordered]@{
  code = "installerBuilt"
  sourceHead = $head
  configuration = $Configuration
  platform = $Platform
  releaseKind = $ReleaseKind
  manifestSha256 = (Get-FileHash -Algorithm SHA256 `
    -LiteralPath (Join-Path $stage "ligase-install-manifest.json")).Hash.ToLowerInvariant()
  installerSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $package).Hash.ToLowerInvariant()
  installerSignature = if ($ReleaseKind -eq "PublicRelease") { "valid" } else { "nonRelease" }
} | ConvertTo-Json -Compress
