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
  [ValidateRange(1, 64)]
  [int]$CoreBuildParallelism = 1,
  [ValidateSet("UnsignedDev", "PublicRelease")]
  [string]$ReleaseKind = "UnsignedDev",
  [string]$SigningTool,
  [string[]]$AllowedPublisher = @(),
  [Parameter(Mandatory)]
  [string]$NefconExecutable,
  [Parameter(Mandatory)]
  [string]$SudoVdaDriverBinary,
  [Parameter(Mandatory)]
  [string]$BoostArchive,
  [switch]$ValidateExternalInputsOnly,
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

function Test-PeMachineX64(
  [Parameter(Mandatory)][string]$Path
) {
  $stream = [IO.File]::Open(
    $Path,
    [IO.FileMode]::Open,
    [IO.FileAccess]::Read,
    [IO.FileShare]::Read)
  try {
    if ($stream.Length -lt 64) { return $false }
    $reader = [IO.BinaryReader]::new($stream)
    try {
      if ($reader.ReadUInt16() -ne 0x5A4D) { return $false }
      $stream.Position = 0x3C
      $peOffset = $reader.ReadUInt32()
      if ($peOffset -gt $stream.Length - 6) { return $false }
      $stream.Position = $peOffset
      if ($reader.ReadUInt32() -ne 0x00004550) { return $false }
      return $reader.ReadUInt16() -eq 0x8664
    } finally {
      $reader.Dispose()
    }
  } finally {
    $stream.Dispose()
  }
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

$boostArchivePath = [IO.Path]::GetFullPath($BoostArchive)
if ([IO.Path]::GetPathRoot($boostArchivePath) -cne "D:\" -or
    -not (Test-Path -LiteralPath $boostArchivePath -PathType Leaf)) {
  throw "boostArchiveUnavailable"
}
if ((Get-Item -LiteralPath $boostArchivePath).Length -ne 102078704) {
  throw "boostArchiveSizeMismatch"
}
$boostArchiveHash = (Get-FileHash -Algorithm SHA256 `
  -LiteralPath $boostArchivePath).Hash
if ($boostArchiveHash -cne
    "67ACEC02D0D118B5DE9EB441F5FB707B3A1CDD884BE00CA24B9A73C995511F74") {
  throw "boostArchiveHashMismatch"
}

$nefconPath = [IO.Path]::GetFullPath($NefconExecutable)
if ([IO.Path]::GetPathRoot($nefconPath) -cne "D:\" -or
    -not (Test-Path -LiteralPath $nefconPath -PathType Leaf)) {
  throw "virtualDisplayInstallerToolUnavailable"
}
if ((Get-Item -LiteralPath $nefconPath).Length -ne 586152) {
  throw "virtualDisplayInstallerToolSizeMismatch"
}
$nefconHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $nefconPath).Hash
if ($nefconHash -cne
    "19A113297EAFEFD796AA91C1A64D199628D9C58DC53928899D2E5D6A68074EFE") {
  throw "virtualDisplayInstallerToolHashMismatch"
}
$nefconSignature = Get-AuthenticodeSignature -LiteralPath $nefconPath
if ($nefconSignature.Status -ne "Valid" -or
    $null -eq $nefconSignature.SignerCertificate -or
    $nefconSignature.SignerCertificate.Thumbprint -cne
      "1F431092EC96A80B41AB5317F53AC02EA6F9B89B" -or
    $null -eq $nefconSignature.TimeStamperCertificate) {
  throw "virtualDisplayInstallerToolSignatureInvalid"
}

$sudoVdaPath = [IO.Path]::GetFullPath($SudoVdaDriverBinary)
if ([IO.Path]::GetPathRoot($sudoVdaPath) -cne "D:\" -or
    -not (Test-Path -LiteralPath $sudoVdaPath -PathType Leaf)) {
  throw "virtualDisplayDriverBinaryUnavailable"
}
if ((Get-Item -LiteralPath $sudoVdaPath).Length -ne 83216) {
  throw "virtualDisplayDriverBinarySizeMismatch"
}
if (-not (Test-PeMachineX64 $sudoVdaPath)) {
  throw "virtualDisplayDriverBinaryArchitectureMismatch"
}
$sudoVdaHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sudoVdaPath).Hash
if ($sudoVdaHash -cne
    "47EE263CB5DE9382C6630A2D7F3DAFEC4A49419F953BEEC869CA5DD0C460FF63") {
  throw "virtualDisplayDriverBinaryHashMismatch"
}
$sudoVdaSignature = Get-AuthenticodeSignature -LiteralPath $sudoVdaPath
if ($sudoVdaSignature.Status -ne "Valid" -or
    $null -eq $sudoVdaSignature.SignerCertificate -or
    $sudoVdaSignature.SignerCertificate.Thumbprint -cne
      "3C918FC73525AD8B1521B6DB26B71F694277CC49") {
  throw "virtualDisplayDriverBinarySignatureInvalid"
}
$sudoVdaInf = Join-Path $sourceRoot "src_assets/windows/drivers/sudovda/SudoVDA.inf"
$sudoVdaInfText = [IO.File]::ReadAllText($sudoVdaInf)
if ($sudoVdaInfText -notmatch
    '(?m)^DriverVer\s*=\s*07/14/2025,1\.10\.9\.289\s*$') {
  throw "virtualDisplayDriverVersionMismatch"
}
if ($sudoVdaInfText -notmatch '(?m)^SudoVDA\.dll=1\s*$' -or
    $sudoVdaInfText -notmatch '(?m)^CopyFiles=UMDriverCopy\s*$' -or
    $sudoVdaInfText -notmatch '(?m)^SudoVDA\.dll\s*$') {
  throw "virtualDisplayDriverInfClosureInvalid"
}
if ($ValidateExternalInputsOnly) {
  [ordered]@{
    code = "installerExternalInputsValid"
    boostArchiveSha256 = $boostArchiveHash
    boostArchiveSize = 102078704
    nefconSha256 = $nefconHash
    sudoVdaDriverSha256 = $sudoVdaHash
    sudoVdaDriverSize = 83216
    sudoVdaDriverArchitecture = "x86-64"
    sudoVdaDriverVersion = "1.10.9.289"
    sudoVdaDriverSignerThumbprint =
      $sudoVdaSignature.SignerCertificate.Thumbprint
  } | ConvertTo-Json -Compress
  return
}

$work = Join-Path $output "work-$head-$Configuration-$Platform"
$desktop = Join-Path $work "desktop"
$watcher = Join-Path $work "watcher"
$launcher = Join-Path $work "launcher"
$transactionHelper = Join-Path $work "transaction-helper"
$virtualDisplaySetupHelper = Join-Path $work "virtual-display-setup-helper"
$dotnetArtifacts = Join-Path $work "dotnet-artifacts"
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
  & $CMake --build $cppRoot --config $Configuration --target sunshine `
    --parallel $CoreBuildParallelism
  if ($LASTEXITCODE -ne 0) { throw "coreBuildFailed" }
  & $DotNet build-server shutdown
  if ($LASTEXITCODE -ne 0) { throw "desktopBuildServerShutdownFailed" }
  if ((Test-Path -LiteralPath $dotnetArtifacts) -or
      (Test-Path -LiteralPath $desktop)) {
    throw "desktopBuildWorkspaceNotClean"
  }
  & $DotNet publish (Join-Path $sourceRoot "src/Ligase.Desktop/Ligase.Host.Desktop.csproj") `
    -c $Configuration -p:Platform=$Platform -p:LigaseStructuredPackage=true `
    -p:UseSharedCompilation=false -nodeReuse:false `
    -p:UseArtifactsOutput=true -p:ArtifactsPath=$dotnetArtifacts `
    -r win-x64 --self-contained true -o $desktop
  if ($LASTEXITCODE -ne 0) { throw "desktopPublishFailed" }
  $desktopPayloadValidation = & (Join-Path $PSScriptRoot "Test-LigaseDesktopPayload.ps1") `
    -DesktopDirectory $desktop -SourceRoot $sourceRoot
  if ($LASTEXITCODE -ne 0) {
    throw "desktopPayloadValidationFailed:$desktopPayloadValidation"
  }
  $desktopStartupValidation = & (Join-Path $PSScriptRoot "Test-LigaseDesktopStartup.ps1") `
    -DesktopDirectory $desktop
  if ($LASTEXITCODE -ne 0) {
    throw "desktopStartupValidationFailed:$desktopStartupValidation"
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
  & $DotNet publish (Join-Path $sourceRoot "tools/Ligase.Installation.TransactionHelper/Ligase.Installation.TransactionHelper.csproj") `
    -c $Configuration -p:Platform=$Platform -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:UseSharedCompilation=false -o $transactionHelper
  if ($LASTEXITCODE -ne 0) { throw "transactionHelperPublishFailed" }
  & $DotNet publish (Join-Path $sourceRoot "tools/Ligase.VirtualDisplay.Setup/Ligase.VirtualDisplay.Setup.csproj") `
    -c $Configuration -p:Platform=$Platform -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:UseSharedCompilation=false `
    -o $virtualDisplaySetupHelper
  if ($LASTEXITCODE -ne 0) { throw "virtualDisplaySetupHelperPublishFailed" }
}

$coreBinary = Join-Path $cppRoot "sunshine.exe"
$desktopBinary = Join-Path $desktop "Ligase.Host.Desktop.exe"
$watcherBinary = Join-Path $watcher "Ligase.GameWatcher.exe"
$launcherBinary = Join-Path $launcher "Ligase Host.exe"
$transactionHelperBinary = Join-Path $transactionHelper "Ligase.Installation.TransactionHelper.exe"
$virtualDisplaySetupHelperBinary = Join-Path $virtualDisplaySetupHelper (
  "Ligase.VirtualDisplay.Setup.exe")
foreach ($entry in @(
  @{ code = "launcherArtifactMissing"; path = $launcherBinary },
  @{ code = "desktopArtifactMissing"; path = $desktopBinary },
  @{ code = "managedCoreArtifactMissing"; path = $coreBinary },
  @{ code = "gameWatcherArtifactMissing"; path = $watcherBinary },
  @{ code = "transactionHelperArtifactMissing"; path = $transactionHelperBinary },
  @{ code = "virtualDisplaySetupHelperArtifactMissing"; path = $virtualDisplaySetupHelperBinary }
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
Copy-Item -LiteralPath $nefconPath -Destination (
  Join-Path $temporaryStage "Deployment/Drivers/sudovda/nefconc.exe")
Copy-Item -LiteralPath $sudoVdaPath -Destination (
  Join-Path $temporaryStage "Deployment/Drivers/sudovda/SudoVDA.dll")
$virtualDisplayPackageStream = [IO.MemoryStream]::new()
try {
  foreach ($name in @(
      "SudoVDA.dll", "SudoVDA.inf", "sudovda.cat", "sudovda.cer") |
      Sort-Object -CaseSensitive) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $temporaryStage (
      "Deployment/Drivers/sudovda/$name")))
    $virtualDisplayPackageStream.Write($bytes, 0, $bytes.Length)
  }
  $virtualDisplayPackageHasher = [Security.Cryptography.SHA256]::Create()
  try {
    $virtualDisplayPackageHash = $virtualDisplayPackageHasher.ComputeHash(
      $virtualDisplayPackageStream.ToArray())
    $virtualDisplayPackageSha256 = [BitConverter]::ToString(
      $virtualDisplayPackageHash).Replace('-', '').ToLowerInvariant()
  } finally {
    $virtualDisplayPackageHasher.Dispose()
  }
} finally {
  $virtualDisplayPackageStream.Dispose()
}
Copy-Item -LiteralPath (Join-Path $sourceRoot "src_assets/windows/misc/firewall") `
  -Destination (Join-Path $temporaryStage "Deployment/Firewall") -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Manage-LigaseInstallation.ps1") `
  -Destination (Join-Path $temporaryStage "Deployment")
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Resolve-LigaseInstallDirectory.ps1") `
  -Destination (Join-Path $temporaryStage "Deployment")
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Invoke-LigaseInstaller.ps1") `
  -Destination (Join-Path $temporaryStage "Deployment")
Copy-Item -LiteralPath $transactionHelperBinary `
  -Destination (Join-Path $temporaryStage "Deployment")
Copy-Item -LiteralPath $virtualDisplaySetupHelperBinary `
  -Destination (Join-Path $temporaryStage "Deployment")
Copy-Item -LiteralPath (Join-Path $sourceRoot (
    "docs/ligase-host/virtual-display-setup-request-v1.schema.json")) `
  -Destination (Join-Path $temporaryStage "Deployment")
Copy-Item -LiteralPath (Join-Path $sourceRoot (
    "docs/ligase-host/virtual-display-setup-result-v1.schema.json")) `
  -Destination (Join-Path $temporaryStage "Deployment")
$virtualDisplayRequestSchemaSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (
  Join-Path $temporaryStage "Deployment/virtual-display-setup-request-v1.schema.json"
)).Hash.ToLowerInvariant()
$virtualDisplayResultSchemaSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (
  Join-Path $temporaryStage "Deployment/virtual-display-setup-result-v1.schema.json"
)).Hash.ToLowerInvariant()

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
  "Deployment/Invoke-LigaseInstaller.ps1",
  "Deployment/Ligase.Installation.TransactionHelper.exe",
  "Deployment/Ligase.VirtualDisplay.Setup.exe",
  "Deployment/virtual-display-setup-request-v1.schema.json",
  "Deployment/virtual-display-setup-result-v1.schema.json",
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
    setupHelper = "Deployment/Ligase.VirtualDisplay.Setup.exe"
    requestSchema = "Deployment/virtual-display-setup-request-v1.schema.json"
    requestSchemaSha256 = $virtualDisplayRequestSchemaSha256
    resultSchema = "Deployment/virtual-display-setup-result-v1.schema.json"
    resultSchemaSha256 = $virtualDisplayResultSchemaSha256
    packageSha256 = $virtualDisplayPackageSha256
    installerTool = "Deployment/Drivers/sudovda/nefconc.exe"
    installerToolSha256 = $nefconHash.ToLowerInvariant()
    installerToolSignerThumbprint =
      $nefconSignature.SignerCertificate.Thumbprint
    driverBinary = "Deployment/Drivers/sudovda/SudoVDA.dll"
    driverBinarySize = 83216
    driverBinarySha256 = $sudoVdaHash.ToLowerInvariant()
    driverBinaryArchitecture = "x86-64"
    driverVersion = "1.10.9.289"
    driverBinarySignerThumbprint =
      $sudoVdaSignature.SignerCertificate.Thumbprint
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
    "Drivers/sudovda/SudoVDA.dll"
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
$installerArgumentValidation = & (
  Join-Path $PSScriptRoot "Test-LigaseInstallDirectoryRuntime.ps1") `
  -MakeNsis $MakeNsis `
  -DotNet $DotNet `
  -OutputRoot (Join-Path $work "installer-argument-runtime")
if ($LASTEXITCODE -ne 0) {
  throw "installerArgumentRuntimeValidationFailed:$installerArgumentValidation"
}
$nsisArguments = @(
  "/INPUTCHARSET",
  "UTF8",
  "/DStageDir=$stage",
  "/DOutputFile=$package"
)
if ($ReleaseKind -eq "PublicRelease") {
  $nsisArguments += "/DSignerTool=$SigningTool"
}
$nsisArguments += (Join-Path $PSScriptRoot "LigaseHost.nsi")
$nsisResult = & (Join-Path $PSScriptRoot "Invoke-NsisCompiler.ps1") `
  -MakeNsis $MakeNsis `
  -CompilerArguments $nsisArguments `
  -WorkingDirectory ([Environment]::CurrentDirectory) `
  -EvidenceDirectory (Join-Path $work "nsis-evidence")
if ($null -eq $nsisResult -or
    [string]$nsisResult.code -cne "nsisCompilerEvidenceCaptured" -or
    $null -eq $nsisResult.exitCode) {
  throw "nsisEvidenceResultInvalid"
}
if ([int]$nsisResult.exitCode -ne 0) {
  throw "nsisBuildFailed:$([int]$nsisResult.exitCode)"
}
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
