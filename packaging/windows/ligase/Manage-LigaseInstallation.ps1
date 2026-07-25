[CmdletBinding()]
param(
  [ValidateSet(
    "DryRun",
    "Install",
    "Readback",
    "Uninstall",
    "InstallVirtualDisplay",
    "UninstallVirtualDisplay",
    "CleanupLegacyDriverTrust")]
  [string]$Action = "DryRun",
  [Parameter(Mandatory)]
  [string]$InstallDirectory,
  [ValidateSet("Preserve", "Quarantine")]
  [string]$DataDisposition = "Preserve",
  [switch]$ConfigureFirewall,
  [switch]$ConfirmLegacyTrustCleanup
)

$ErrorActionPreference = "Stop"
$installRoot = [IO.Path]::GetFullPath($InstallDirectory)
$manifestPath = Join-Path $installRoot "ligase-install-manifest.json"
$bootstrapPath = Join-Path $installRoot "ligase-bootstrap.json"
$mutex = [Threading.Mutex]::new($false, "Global\Ligase.Host.Installer.v1")
$held = $false

function Write-Outcome([string]$Code, [bool]$Success, [hashtable]$Fields = @{}) {
  $value = [ordered]@{ code = $Code; success = $Success }
  foreach ($key in $Fields.Keys) { $value[$key] = $Fields[$key] }
  $value | ConvertTo-Json -Depth 8 -Compress
}

function Read-Manifest {
  if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "installManifestMissing"
  }
  $raw = Get-Content -LiteralPath $manifestPath -Raw
  $document = $raw | ConvertFrom-Json
  if ($document.schemaVersion -ne 1 -or
      $document.platform -ne "x64" -or
      $document.configuration -notin @("Debug", "Release") -or
      $document.installMode -ne "packaged" -or
      $document.releaseKind -notin @("UnsignedDev", "PublicRelease")) {
    throw "installManifestInvalid"
  }
  return $document
}

function Test-Artifacts($Manifest) {
  $states = @()
  foreach ($artifact in $Manifest.artifacts) {
    if ($artifact.role -notin @("desktop", "managedCore", "gameWatcher") -or
        [IO.Path]::IsPathRooted([string]$artifact.relativePath) -or
        ([string]$artifact.relativePath).Contains("..")) {
      throw "installManifestInvalid"
    }
    $path = [IO.Path]::GetFullPath((Join-Path $installRoot $artifact.relativePath))
    if (-not $path.StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase)) {
      throw "installManifestInvalid"
    }
    $available = Test-Path -LiteralPath $path -PathType Leaf
    $actual = if ($available) {
      (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
    } else { "" }
    $signatureCode = "nonRelease"
    if ($available -and $Manifest.releaseKind -eq "PublicRelease") {
      $signature = Get-AuthenticodeSignature -LiteralPath $path
      $signatureCode = if (
        $signature.Status -eq "Valid" -and
        $null -ne $signature.TimeStamperCertificate -and
        $signature.SignerCertificate.Subject -ceq
          ([string]$artifact.signature.signerSubject) -and
        $signature.SignerCertificate.Thumbprint -ceq
          ([string]$artifact.signature.signerThumbprint)
      ) { "valid" } else { "artifactSignatureInvalid" }
    }
    $states += [ordered]@{
      role = [string]$artifact.role
      available = $available
      version = [string]$artifact.version
      hashMatches = $available -and $actual -ceq ([string]$artifact.signedArtifactSha256)
      machineCode = if (-not $available) { "artifactMissing" }
        elseif ($actual -cne ([string]$artifact.signedArtifactSha256)) { "artifactHashMismatch" }
        elseif ($signatureCode -eq "artifactSignatureInvalid") { $signatureCode }
        else { "available" }
      signatureStatus = $signatureCode
    }
  }
  if (@($states | Where-Object { $_.machineCode -ne "available" }).Count -gt 0) {
    throw "artifactReadbackFailed"
  }
  if ($Manifest.releaseKind -eq "PublicRelease") {
    foreach ($helper in $Manifest.privilegedHelpers) {
      $path = [IO.Path]::GetFullPath((Join-Path $installRoot $helper.relativePath))
      $signature = Get-AuthenticodeSignature -LiteralPath $path
      if (
        -not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant() -cne
          ([string]$helper.signedArtifactSha256) -or
        $signature.Status -ne "Valid" -or
        $null -eq $signature.TimeStamperCertificate -or
        $signature.SignerCertificate.Subject -cne ([string]$helper.signerSubject) -or
        $signature.SignerCertificate.Thumbprint -cne ([string]$helper.signerThumbprint)
      ) {
        throw "helperSignatureInvalid"
      }
    }
  }
  return $states
}

function Get-FirewallReadback($Manifest) {
  $firewall = $Manifest.firewall
  $script = Join-Path $installRoot $firewall.script
  try {
    $raw = & $script `
      -Action Readback `
      -Manifest (Join-Path $installRoot $firewall.manifest) `
      -Program (Join-Path $installRoot "Apollo/sunshine.exe") `
      -BasePort $firewall.basePort
    $owned = $raw | ConvertFrom-Json
    $legacy = @(Get-NetFirewallRule -PolicyStore ActiveStore -ErrorAction Stop |
      Where-Object {
        $_.Enabled -eq "True" -and
        $_.Group -ne "Ligase Host LAN Access" -and
        ($_.DisplayName -match "Apollo|Sunshine" -or $_.Name -match "Apollo|Sunshine")
      } |
      Select-Object -First 20 -ExpandProperty DisplayName)
    return [ordered]@{
      state = if ([bool]$owned.configured) { "configured" } else { "notConfigured" }
      machineCode = [string]$owned.code
      legacyBroadRuleCount = $legacy.Count
      legacyBroadRuleNames = @($legacy)
      legacyCleanupRequiresExplicitConsent = $legacy.Count -gt 0
    }
  } catch {
    return [ordered]@{
      state = "unavailable"
      machineCode = "firewallReadbackFailed"
      legacyBroadRuleCount = 0
      legacyBroadRuleNames = @()
      legacyCleanupRequiresExplicitConsent = $false
    }
  }
}

function Get-VirtualDisplay {
  try {
    $devices = @(Get-PnpDevice -PresentOnly -ErrorAction Stop |
      Where-Object {
        $_.FriendlyName -match "SudoVDA|Virtual Display" -or
        $_.InstanceId -match "SUDOVDA"
      })
    if ($devices.Count -eq 0) {
      return [ordered]@{
        state = "notInstalled"
        machineCode = "virtualDisplayNotInstalled"
        physicalDesktopAvailable = $true
      }
    }
    $reboot = @($devices | Where-Object { $_.Status -ne "OK" }).Count -gt 0
    return [ordered]@{
      state = if ($reboot) { "rebootRequired" } else { "available" }
      machineCode = if ($reboot) { "virtualDisplayRebootRequired" } else { "available" }
      physicalDesktopAvailable = $true
    }
  } catch {
    return [ordered]@{
      state = "failed"
      machineCode = "virtualDisplayReadbackFailed"
      physicalDesktopAvailable = $true
    }
  }
}

function Get-DriverTrust($Manifest) {
  $driver = $Manifest.virtualDisplay
  $thumbprint = [string]$driver.certificateThumbprint
  if ([string]::IsNullOrWhiteSpace($thumbprint)) {
    return [ordered]@{ state = "missing"; machineCode = "driverCertificateMissing" }
  }
  $locations = @()
  foreach ($location in @("LocalMachine", "CurrentUser")) {
    foreach ($store in @("Root", "TrustedPublisher")) {
      if (Test-Path -LiteralPath "Cert:\$location\$store\$thumbprint") {
        $locations += "$location/$store"
      }
    }
  }
  $state = if ([bool]$driver.selfSigned -and $locations.Count -gt 0) {
    "locallyTrustedSelfSigned"
  } elseif ([bool]$driver.selfSigned) {
    "untrusted"
  } elseif ([string]$driver.trust -eq "caTrusted") {
    "caTrusted"
  } else {
    [string]$driver.trust
  }
  return [ordered]@{
    state = $state
    machineCode = "driverTrust" + $state.Substring(0, 1).ToUpperInvariant() + $state.Substring(1)
    storeCount = $locations.Count
    selfSigned = [bool]$driver.selfSigned
    timestamped = [bool]$driver.timestamped
  }
}

function Get-DriverCertificateLocations([string]$Thumbprint) {
  $locations = @()
  foreach ($location in @("LocalMachine", "CurrentUser")) {
    foreach ($store in @("Root", "TrustedPublisher")) {
      if (Test-Path -LiteralPath "Cert:\$location\$store\$Thumbprint") {
        $locations += "$location/$store"
      }
    }
  }
  return @($locations)
}

function Test-DependentVirtualDisplay {
  try {
    return @(
      Get-PnpDevice -PresentOnly -ErrorAction Stop |
        Where-Object {
          $_.FriendlyName -match "SudoVDA|Virtual Display" -or
          $_.InstanceId -match "SUDOVDA"
        }).Count -gt 0
  } catch {
    return $true
  }
}

try {
  $held = $mutex.WaitOne([TimeSpan]::FromSeconds(2))
  if (-not $held) {
    Write-Outcome "installerBusy" $false
    exit 20
  }
  $manifest = Read-Manifest
  $artifacts = Test-Artifacts $manifest
  $virtualDisplay = Get-VirtualDisplay
  $driverTrust = Get-DriverTrust $manifest
  $firewallReadback = Get-FirewallReadback $manifest
  $encoder = [ordered]@{
    state = "requiresRuntimeProbe"
    machineCode = "encoderProbePendingFirstLaunch"
    requiredForInstall = $false
    requiredForStreaming = $true
  }

  if ($Action -eq "InstallVirtualDisplay") {
    $thumbprint = [string]$manifest.virtualDisplay.certificateThumbprint
    $before = @(Get-DriverCertificateLocations $thumbprint)
    & (Join-Path $installRoot $manifest.virtualDisplay.installer) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "virtualDisplayInstallFailed" }
    $after = @(Get-DriverCertificateLocations $thumbprint)
    $ownedStores = @($after | Where-Object { $before -notcontains $_ })
    [IO.File]::WriteAllText(
      (Join-Path $installRoot "Drivers/sudovda/.ligase-driver-ownership.json"),
      (@{
        schemaVersion = 1
        certificateThumbprint = $thumbprint
        certificateStores = $ownedStores
      } | ConvertTo-Json -Compress),
      [Text.UTF8Encoding]::new($false))
    Write-Outcome "virtualDisplayInstalled" $true @{
      trust = $driverTrust
      certificateStoresAdded = $ownedStores.Count
      restartRequired = $true
    }
    exit 0
  }

  if ($Action -eq "UninstallVirtualDisplay") {
    & (Join-Path $installRoot $manifest.virtualDisplay.uninstaller) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "virtualDisplayUninstallFailed" }
    $marker = Join-Path $installRoot "Drivers/sudovda/.ligase-driver-ownership.json"
    $removed = 0
    if ((Test-Path -LiteralPath $marker) -and -not (Test-DependentVirtualDisplay)) {
      $ownership = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
      foreach ($store in @($ownership.certificateStores)) {
        $certificate = "Cert:\$store\$($ownership.certificateThumbprint)"
        if (Test-Path -LiteralPath $certificate) {
          Remove-Item -LiteralPath $certificate -Force
          ++$removed
        }
      }
      Remove-Item -LiteralPath $marker -Force
    }
    Write-Outcome "virtualDisplayUninstalled" $true @{
      ownedCertificateStoresRemoved = $removed
      dependencyRemaining = Test-DependentVirtualDisplay
      restartRequired = $true
    }
    exit 0
  }

  if ($Action -eq "CleanupLegacyDriverTrust") {
    if (-not $ConfirmLegacyTrustCleanup) {
      Write-Outcome "userConfirmationRequired" $false @{
        trust = $driverTrust
        recoveryAction = "confirmLegacyTrustCleanup"
      }
      exit 21
    }
    if (Test-DependentVirtualDisplay) {
      Write-Outcome "driverDependencyRemaining" $false
      exit 22
    }
    $removed = 0
    foreach ($store in @(Get-DriverCertificateLocations (
      [string]$manifest.virtualDisplay.certificateThumbprint))) {
      Remove-Item -LiteralPath (
        "Cert:\$store\$($manifest.virtualDisplay.certificateThumbprint)") -Force
      ++$removed
    }
    Write-Outcome "legacyDriverTrustRemoved" $true @{
      certificateStoresRemoved = $removed
    }
    exit 0
  }

  if ($Action -eq "DryRun") {
    Write-Outcome "dryRunReady" $true @{
      installMode = "packaged"
      artifacts = $artifacts
      virtualDisplay = $virtualDisplay
      driverTrust = $driverTrust
      firewall = $firewallReadback
      encoder = $encoder
      firewallAction = if ($ConfigureFirewall) { "applyOwnedExactRules" } else { "none" }
      dataRootAction = "createFreshInstance"
      restartRequired = $virtualDisplay.state -eq "rebootRequired"
    }
    exit 0
  }

  if ($Action -eq "Install") {
    if ($manifest.releaseKind -eq "PublicRelease") {
      $uninstaller = Join-Path $installRoot "Uninstall.exe"
      $signature = Get-AuthenticodeSignature -LiteralPath $uninstaller
      $publisher = [string]$manifest.artifacts[0].signature.signerSubject
      if (
        $signature.Status -ne "Valid" -or
        $null -eq $signature.TimeStamperCertificate -or
        $signature.SignerCertificate.Subject -cne $publisher
      ) {
        throw "uninstallerSignatureInvalid"
      }
    }
    $dataRoot = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) `
      ("Ligase Host\Instances\" + [guid]::NewGuid().ToString("D"))
    [IO.File]::WriteAllText(
      $bootstrapPath,
      (@{ schemaVersion = 1; dataRoot = $dataRoot } | ConvertTo-Json -Compress),
      [Text.UTF8Encoding]::new($false))
    if ($ConfigureFirewall) {
      & (Join-Path $installRoot $manifest.firewall.script) `
        -Action Apply `
        -Manifest (Join-Path $installRoot $manifest.firewall.manifest) `
        -Program (Join-Path $installRoot "Apollo/sunshine.exe") `
        -BasePort $manifest.firewall.basePort | Out-Null
      if ($LASTEXITCODE -ne 0) {
        Remove-Item -LiteralPath $bootstrapPath -Force -ErrorAction SilentlyContinue
        throw "firewallApplyFailed"
      }
    }
    Write-Outcome "installed" $true @{
      installMode = "packaged"
      dataRootState = "fresh"
      virtualDisplay = $virtualDisplay
      driverTrust = $driverTrust
      firewall = $firewallReadback
      encoder = $encoder
      restartRequired = $virtualDisplay.state -eq "rebootRequired"
    }
    exit 0
  }

  if ($Action -eq "Uninstall") {
    if ($ConfigureFirewall) {
      & (Join-Path $installRoot $manifest.firewall.script) `
        -Action Remove `
        -Manifest (Join-Path $installRoot $manifest.firewall.manifest) `
        -Program (Join-Path $installRoot "Apollo/sunshine.exe") `
        -BasePort $manifest.firewall.basePort | Out-Null
      if ($LASTEXITCODE -ne 0) { throw "firewallRemoveFailed" }
    }
    $dataRootState = "preserved"
    if ($DataDisposition -eq "Quarantine" -and (Test-Path -LiteralPath $bootstrapPath)) {
      $bootstrap = Get-Content -LiteralPath $bootstrapPath -Raw | ConvertFrom-Json
      if (Test-Path -LiteralPath $bootstrap.dataRoot -PathType Container) {
        $quarantine = Join-Path (
          [Environment]::GetFolderPath("LocalApplicationData")) (
          "Ligase Host\Quarantine\" + [guid]::NewGuid().ToString("D"))
        New-Item -ItemType Directory -Path (Split-Path $quarantine) -Force | Out-Null
        Move-Item -LiteralPath $bootstrap.dataRoot -Destination $quarantine
      }
      $dataRootState = "quarantined"
    }
    Write-Outcome "uninstalled" $true @{
      dataRootState = $dataRootState
      ownedFirewallRulesRemoved = [bool]$ConfigureFirewall
    }
    exit 0
  }

  $bootstrapState = if (-not (Test-Path -LiteralPath $bootstrapPath)) { "fresh" }
    else {
      try {
        $bootstrap = Get-Content -LiteralPath $bootstrapPath -Raw | ConvertFrom-Json
        if (Test-Path -LiteralPath $bootstrap.dataRoot) { "existing" } else { "fresh" }
      } catch { "inaccessible" }
    }
  Write-Outcome "readbackComplete" $true @{
    installMode = "packaged"
    sourceHead = [string]$manifest.sourceHead
    artifacts = $artifacts
    virtualDisplay = $virtualDisplay
    driverTrust = $driverTrust
    firewall = $firewallReadback
    encoder = $encoder
    dataRootState = $bootstrapState
    restartRequired = $virtualDisplay.state -eq "rebootRequired"
  }
  exit 0
} catch {
  $knownCodes = @(
    "installManifestMissing",
    "installManifestInvalid",
    "artifactReadbackFailed",
    "helperSignatureInvalid",
    "uninstallerSignatureInvalid",
    "firewallApplyFailed",
    "firewallRemoveFailed",
    "virtualDisplayInstallFailed",
    "virtualDisplayUninstallFailed")
  $message = [string]$_.Exception.Message
  $code = if ($knownCodes -contains $message) {
    $message
  } else {
    "installationActionFailed"
  }
  Write-Outcome $code $false
  exit 10
} finally {
  if ($held) { $mutex.ReleaseMutex() }
  $mutex.Dispose()
}
