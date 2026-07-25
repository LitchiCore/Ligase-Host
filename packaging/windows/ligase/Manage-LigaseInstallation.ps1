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
  [string]$DataRoot,
  [ValidateSet("Preserve", "Quarantine")]
  [string]$DataDisposition = "Preserve",
  [switch]$ConfigureFirewall,
  [switch]$ConfirmLegacyTrustCleanup
)

$ErrorActionPreference = "Stop"
$installRoot = [IO.Path]::GetFullPath($InstallDirectory)
$manifestPath = Join-Path $installRoot "ligase-install-manifest.json"
$bootstrapPath = Join-Path $installRoot "ligase-bootstrap.json"
$script:freshDataRootCreated = $null

Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

public static class LigaseInteractiveUser
{
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_IMPERSONATE = 0x0004;

    private enum WTS_INFO_CLASS
    {
        WTSUserName = 5,
        WTSDomainName = 7
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(
        uint processId, out uint sessionId);

    [DllImport("Wtsapi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr server, int sessionId, WTS_INFO_CLASS infoClass,
        out IntPtr buffer, out int bytesReturned);

    [DllImport("Wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private static string ReadSessionValue(
        int sessionId, WTS_INFO_CLASS infoClass)
    {
        IntPtr buffer;
        int bytes;
        if (!WTSQuerySessionInformation(
            IntPtr.Zero, sessionId, infoClass, out buffer, out bytes) ||
            buffer == IntPtr.Zero)
            throw new InvalidOperationException("interactiveOperatorUnavailable");
        try
        {
            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    public static WindowsIdentity OpenIdentity()
    {
        uint sessionId;
        if (!ProcessIdToSessionId(
            unchecked((uint)Process.GetCurrentProcess().Id), out sessionId) ||
            sessionId == 0)
            throw new InvalidOperationException("interactiveOperatorUnavailable");

        var user = ReadSessionValue(
            unchecked((int)sessionId), WTS_INFO_CLASS.WTSUserName);
        var domain = ReadSessionValue(
            unchecked((int)sessionId), WTS_INFO_CLASS.WTSDomainName);
        if (String.IsNullOrWhiteSpace(user))
            throw new InvalidOperationException("interactiveOperatorUnavailable");
        var account = String.IsNullOrWhiteSpace(domain)
            ? user
            : domain + "\\" + user;
        var expectedSid = ((NTAccount)new NTAccount(account)).Translate(
            typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (expectedSid == null)
            throw new InvalidOperationException("interactiveOperatorUnavailable");

        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != sessionId)
                        continue;
                    IntPtr token;
                    if (!OpenProcessToken(
                        process.Handle,
                        TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_IMPERSONATE,
                        out token))
                        continue;
                    WindowsIdentity identity;
                    try
                    {
                        identity = new WindowsIdentity(token);
                    }
                    finally
                    {
                        CloseHandle(token);
                    }
                    if (identity.User != null &&
                        identity.User.Equals(expectedSid))
                        return identity;
                    identity.Dispose();
                }
                catch
                {
                    // Other sessions and protected processes are ignored.
                }
            }
        }
        throw new InvalidOperationException("interactiveOperatorUnavailable");
    }
}
"@
$mutex = [Threading.Mutex]::new($false, "Global\Ligase.Host.Installer.v1")
$held = $false

function Write-Outcome(
  [string]$Code,
  [bool]$Success,
  [System.Collections.IDictionary]$Fields = [ordered]@{}
) {
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
      $document.installLayout -ne "structured-v1" -or
      $document.platform -ne "x64" -or
      $document.configuration -notin @("Debug", "Release") -or
      $document.installMode -ne "packaged" -or
      $document.releaseKind -notin @("UnsignedDev", "PublicRelease")) {
    throw "installManifestInvalid"
  }
  return $document
}

function Read-ValidBootstrap {
  if (-not (Test-Path -LiteralPath $bootstrapPath -PathType Leaf)) {
    return $null
  }
  $raw = [IO.File]::ReadAllBytes($bootstrapPath)
  try {
    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    $json = $utf8.GetString($raw)
    if ([regex]::Matches($json, '"schemaVersion"\s*:').Count -ne 1 -or
        [regex]::Matches($json, '"dataRoot"\s*:').Count -ne 1) {
      throw "bootstrapInvalid"
    }
    $bootstrap = $json | ConvertFrom-Json
    $properties = @($bootstrap.PSObject.Properties.Name)
    if ($properties.Count -ne 2 -or
        $properties -notcontains "schemaVersion" -or
        $properties -notcontains "dataRoot" -or
        $bootstrap.schemaVersion -ne 1 -or
        [string]::IsNullOrWhiteSpace([string]$bootstrap.dataRoot) -or
        -not [IO.Path]::IsPathRooted([string]$bootstrap.dataRoot)) {
      throw "bootstrapInvalid"
    }
    $canonical = [IO.Path]::GetFullPath([string]$bootstrap.dataRoot)
    if (-not (Test-Path -LiteralPath $canonical -PathType Container)) {
      throw "bootstrapDataRootUnavailable"
    }
    return [ordered]@{
      bytes = $raw
      dataRoot = $canonical
    }
  } catch {
    if ($_.Exception.Message -in @(
        "bootstrapInvalid", "bootstrapDataRootUnavailable")) {
      throw
    }
    throw "bootstrapInvalid"
  }
}

function Write-NewBootstrap([string]$RequestedDataRoot) {
  if ([string]::IsNullOrWhiteSpace($RequestedDataRoot)) {
    throw "dataRootRequired"
  }
  if (-not [IO.Path]::IsPathRooted($RequestedDataRoot)) {
    throw "dataRootNotAbsolute"
  }
  $root = [IO.Path]::GetFullPath($RequestedDataRoot)
  if (Test-Path -LiteralPath $root) {
    throw "freshDataRootAlreadyExists"
  }
  New-Item -ItemType Directory -Path $root | Out-Null
  $script:freshDataRootCreated = $root
  try {
    $operator = [LigaseInteractiveUser]::OpenIdentity()
    if ($null -eq $operator.User) {
      throw "interactiveOperatorUnavailable"
    }
    $operatorSid = $operator.User
    $systemSid = [Security.Principal.SecurityIdentifier]::new(
      [Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
    $administratorsSid = [Security.Principal.SecurityIdentifier]::new(
      [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
    $security = [Security.AccessControl.DirectorySecurity]::new()
    $security.SetAccessRuleProtection($true, $false)
    $security.SetOwner($administratorsSid)
    $inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
      [Security.AccessControl.InheritanceFlags]::ObjectInherit
    $propagation = [Security.AccessControl.PropagationFlags]::None
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
      $systemSid, "FullControl", $inheritance, $propagation, "Allow"))
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
      $administratorsSid, "FullControl", $inheritance, $propagation, "Allow"))
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
      $operatorSid, "Modify", $inheritance, $propagation, "Allow"))
    Set-Acl -LiteralPath $root -AclObject $security

    $readback = Get-Acl -LiteralPath $root
    $explicitRules = @($readback.Access | Where-Object { -not $_.IsInherited })
    $actualRules = @($explicitRules | ForEach-Object {
      $sid = $_.IdentityReference.Translate(
        [Security.Principal.SecurityIdentifier]).Value
      "$($_.AccessControlType)|$sid|$([int]$_.FileSystemRights)|$([int]$_.InheritanceFlags)|$([int]$_.PropagationFlags)"
    } | Sort-Object)
    $expectedRules = @(
      "Allow|$($systemSid.Value)|$([int][Security.AccessControl.FileSystemRights]::FullControl)|$([int]$inheritance)|$([int]$propagation)",
      "Allow|$($administratorsSid.Value)|$([int][Security.AccessControl.FileSystemRights]::FullControl)|$([int]$inheritance)|$([int]$propagation)",
      "Allow|$($operatorSid.Value)|$([int](
        [Security.AccessControl.FileSystemRights]::Modify -bor
        [Security.AccessControl.FileSystemRights]::Synchronize))|$([int]$inheritance)|$([int]$propagation)"
    ) | Sort-Object
    $owner = ([Security.Principal.NTAccount]$readback.Owner).Translate(
      [Security.Principal.SecurityIdentifier]).Value
    if (-not $readback.AreAccessRulesProtected -or
        $readback.AreAuditRulesProtected -or
        $owner -cne $administratorsSid.Value -or
        $actualRules.Count -ne $expectedRules.Count -or
        (Compare-Object $actualRules $expectedRules).Count -ne 0) {
      throw "dataRootAclMismatch"
    }

    $context = $operator.Impersonate()
    try {
      $probe = Join-Path $root (".ligase-access-" + [guid]::NewGuid().ToString("N"))
      $moved = "$probe.moved"
      [IO.File]::WriteAllText($probe, "probe", [Text.UTF8Encoding]::new($false))
      Move-Item -LiteralPath $probe -Destination $moved
      Remove-Item -LiteralPath $moved -Force
    } finally {
      $context.Dispose()
      $operator.Dispose()
    }
  } catch {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    $script:freshDataRootCreated = $null
    if ($_.Exception.Message -in @(
        "interactiveOperatorUnavailable", "dataRootAclMismatch")) {
      throw
    }
    throw "dataRootAclMismatch"
  }
  $temporary = $bootstrapPath + ".pending"
  try {
    [IO.File]::WriteAllText(
      $temporary,
      (@{ schemaVersion = 1; dataRoot = $root } | ConvertTo-Json -Compress),
      [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $bootstrapPath -Force
    $written = Read-ValidBootstrap
    if ($null -eq $written -or
        -not ([string]$written.dataRoot).Equals(
          $root,
          [StringComparison]::OrdinalIgnoreCase)) {
      throw "bootstrapDataRootMismatch"
    }
  } catch {
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $bootstrapPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    $script:freshDataRootCreated = $null
    if ($_.Exception.Message -eq "bootstrapDataRootMismatch") {
      throw
    }
    throw "bootstrapDataRootMismatch"
  }
  return $root
}

function Get-DataRootAccessState([string]$Root) {
  if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
    return "missing"
  }
  try {
    $identity = [LigaseInteractiveUser]::OpenIdentity()
    $operatorSid = $identity.User
    if ($null -eq $operatorSid) { return "inaccessible" }
  } catch {
    return "inaccessible"
  } finally {
    if ($null -ne $identity) { $identity.Dispose() }
  }
  try {
    $systemSid = [Security.Principal.SecurityIdentifier]::new(
      [Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
    $administratorsSid = [Security.Principal.SecurityIdentifier]::new(
      [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
    $security = Get-Acl -LiteralPath $Root
    $ownerSid = ([Security.Principal.NTAccount]$security.Owner).Translate(
      [Security.Principal.SecurityIdentifier])
    if (-not $security.AreAccessRulesProtected -or
        -not $ownerSid.Equals($administratorsSid)) {
      return "aclDrift"
    }
    $explicitRules = @($security.Access | Where-Object { -not $_.IsInherited })
    if ($explicitRules.Count -ne 3 -or
        @($explicitRules | Where-Object {
          $_.AccessControlType -ne "Allow"
        }).Count -ne 0) {
      return "aclDrift"
    }
    $actualRules = @($explicitRules | ForEach-Object {
      $sid = $_.IdentityReference.Translate(
        [Security.Principal.SecurityIdentifier]).Value
      "$sid,$([int]$_.FileSystemRights),$([int]$_.InheritanceFlags),$([int]$_.PropagationFlags)"
    })
    $inheritance = [int](
      [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
      [Security.AccessControl.InheritanceFlags]::ObjectInherit)
    $propagation = [int][Security.AccessControl.PropagationFlags]::None
    $actual = (@($actualRules | Sort-Object) -join "|")
    $expected = (@(
      "$($operatorSid.Value),$([int](
        [Security.AccessControl.FileSystemRights]::Modify -bor
        [Security.AccessControl.FileSystemRights]::Synchronize)),$inheritance,$propagation",
      "$($systemSid.Value),$([int][Security.AccessControl.FileSystemRights]::FullControl),$inheritance,$propagation",
      "$($administratorsSid.Value),$([int][Security.AccessControl.FileSystemRights]::FullControl),$inheritance,$propagation"
    ) | Sort-Object) -join "|"
    if ($actual -ceq $expected) { return "existing" }
    $operatorRule = @($explicitRules | Where-Object {
      $_.AccessControlType -eq "Allow" -and
      $_.IdentityReference.Translate(
        [Security.Principal.SecurityIdentifier]).Value -ceq $operatorSid.Value
    })
    if ($operatorRule.Count -eq 0) { return "wrongUser" }
    return "aclDrift"
  } catch {
    return "aclDrift"
  }
}

function Invoke-FirewallAction(
  [Parameter(Mandatory)][ValidateSet("Apply", "Remove", "Readback")]
  [string]$FirewallAction,
  [Parameter(Mandatory)]$Manifest
) {
  $script = Join-Path $installRoot $Manifest.firewall.script
  $arguments = @(
    "-NoProfile",
    "-NonInteractive",
    "-ExecutionPolicy", "Bypass",
    "-File", $script,
    "-Action", $FirewallAction,
    "-Manifest", (Join-Path $installRoot $Manifest.firewall.manifest),
    "-Program", (Join-Path $installRoot "Core/sunshine.exe"),
    "-BasePort", ([string]$Manifest.firewall.basePort)
  )
  $output = @(& powershell.exe @arguments 2>&1)
  $exitCode = $LASTEXITCODE
  if ($exitCode -ne 0) {
    throw "firewall$($FirewallAction)Failed"
  }
  try {
    $result = $output[-1] | ConvertFrom-Json
  } catch {
    throw "firewall$($FirewallAction)Failed"
  }
  if ($FirewallAction -in @("Apply", "Readback") -and
      (-not [bool]$result.configured -or
       [string]$result.code -cne "configured")) {
    throw "firewallReadbackMismatch"
  }
  if ($FirewallAction -eq "Remove" -and [bool]$result.configured) {
    throw "firewallRemoveFailed"
  }
  return $result
}

function Remove-LegacyFlatOwnedEntries($Manifest) {
  $removed = 0
  $candidateDirectories = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
  foreach ($relative in @($Manifest.legacyFlatOwnedEntries)) {
    if ([string]::IsNullOrWhiteSpace([string]$relative) -or
        [IO.Path]::IsPathRooted([string]$relative) -or
        ([string]$relative).Contains("..")) {
      throw "installManifestInvalid"
    }
    $path = [IO.Path]::GetFullPath((Join-Path $installRoot $relative))
    if (-not $path.StartsWith(
        $installRoot.TrimEnd("\") + "\",
        [StringComparison]::OrdinalIgnoreCase)) {
      throw "installManifestInvalid"
    }
    if (Test-Path -LiteralPath $path -PathType Leaf) {
      Remove-Item -LiteralPath $path -Force
      ++$removed
      $parent = [IO.Path]::GetDirectoryName($path)
      while (-not [string]::IsNullOrWhiteSpace($parent) -and
          -not [string]::Equals(
            $parent,
            $installRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        $null = $candidateDirectories.Add($parent)
        $parent = [IO.Path]::GetDirectoryName($parent)
      }
    }
  }
  foreach ($path in @($candidateDirectories) |
      Sort-Object { $_.Length } -Descending) {
    if ((Test-Path -LiteralPath $path -PathType Container) -and
        @(Get-ChildItem -LiteralPath $path -Force).Count -eq 0) {
      Remove-Item -LiteralPath $path -Force
    }
  }
  return $removed
}

function Test-Artifacts($Manifest) {
  $states = @()
  foreach ($artifact in $Manifest.artifacts) {
    if ($artifact.role -notin @("launcher", "desktop", "managedCore", "gameWatcher") -or
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
    $raw = @(& powershell.exe `
      -NoProfile `
      -NonInteractive `
      -ExecutionPolicy Bypass `
      -File $script `
      -Action Readback `
      -Manifest (Join-Path $installRoot $firewall.manifest) `
      -Program (Join-Path $installRoot "Core/sunshine.exe") `
      -BasePort $firewall.basePort 2>&1)
    if ($LASTEXITCODE -ne 0 -or $raw.Count -eq 0) {
      throw "firewallReadbackFailed"
    }
    $owned = $raw[-1] | ConvertFrom-Json
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
      (Join-Path $installRoot "Deployment/Drivers/sudovda/.ligase-driver-ownership.json"),
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
    $marker = Join-Path $installRoot "Deployment/Drivers/sudovda/.ligase-driver-ownership.json"
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
    $existingBootstrap = Read-ValidBootstrap
    Write-Outcome "dryRunReady" $true @{
      installMode = "packaged"
      artifacts = $artifacts
      virtualDisplay = $virtualDisplay
      driverTrust = $driverTrust
      firewall = $firewallReadback
      encoder = $encoder
      firewallAction = if ($ConfigureFirewall) { "applyOwnedExactRules" } else { "none" }
      dataRootAction = if ($null -ne $existingBootstrap) {
        "preserveExistingBootstrap"
      } elseif (-not [string]::IsNullOrWhiteSpace($DataRoot)) {
        "createExplicitDataRoot"
      } else {
        "createDefaultFreshInstance"
      }
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
    $existingBootstrap = Read-ValidBootstrap
    $bootstrapBytes = if ($null -ne $existingBootstrap) {
      [byte[]]$existingBootstrap.bytes
    } else { $null }
    $dataRoot = if ($null -ne $existingBootstrap) {
      [string]$existingBootstrap.dataRoot
    } else {
      Write-NewBootstrap $DataRoot
    }
    $legacyEntriesRemoved = Remove-LegacyFlatOwnedEntries $manifest
    if ($ConfigureFirewall) {
      $firewallApplyCompleted = $false
      try {
        $null = Invoke-FirewallAction -FirewallAction Apply -Manifest $manifest
        $firewallApplyCompleted = $true
        $firewallReadback = Get-FirewallReadback $manifest
        if ($firewallReadback.state -cne "configured" -or
            $firewallReadback.machineCode -cne "configured") {
          throw "firewallReadbackMismatch"
        }
      } catch {
        if ($firewallApplyCompleted) {
          try {
            $null = Invoke-FirewallAction `
              -FirewallAction Remove `
              -Manifest $manifest
          } catch {
            # The install still fails closed. The original machine outcome is
            # preserved while readback will expose any owned-rule residue.
          }
        }
        if ($null -eq $bootstrapBytes) {
          Remove-Item -LiteralPath $bootstrapPath -Force -ErrorAction SilentlyContinue
          if ($null -ne $script:freshDataRootCreated -and
              (Test-Path -LiteralPath $script:freshDataRootCreated)) {
            Remove-Item -LiteralPath $script:freshDataRootCreated `
              -Recurse -Force -ErrorAction SilentlyContinue
          }
        }
        throw
      }
    }
    if ($null -ne $bootstrapBytes -and
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($bootstrapPath)) -cne
          [Convert]::ToBase64String($bootstrapBytes)) {
      throw "bootstrapChangedDuringUpgrade"
    }
    Write-Outcome "installed" $true ([ordered]@{
      installMode = "packaged"
      dataRootState = if ($null -ne $existingBootstrap) { "existing" } else { "fresh" }
      dataRootAction = if ($null -ne $existingBootstrap) {
        "preservedExistingBootstrap"
      } else {
        "createdFreshBootstrap"
      }
      firewallState = [string]$firewallReadback.state
      firewallMachineCode = [string]$firewallReadback.machineCode
    })
    exit 0
  }

  if ($Action -eq "Uninstall") {
    if ($ConfigureFirewall) {
      $null = Invoke-FirewallAction -FirewallAction Remove -Manifest $manifest
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

  $bootstrapState = if (-not (Test-Path -LiteralPath $bootstrapPath)) {
    "fresh"
  } else {
    try {
      $bootstrap = Read-ValidBootstrap
      Get-DataRootAccessState ([string]$bootstrap.dataRoot)
    } catch {
      if ($_.Exception.Message -eq "bootstrapDataRootUnavailable") {
        "missing"
      } else {
        "aclDrift"
      }
    }
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
    "bootstrapInvalid",
    "bootstrapDataRootUnavailable",
    "bootstrapChangedDuringUpgrade",
    "bootstrapDataRootMismatch",
    "dataRootRequired",
    "dataRootNotAbsolute",
    "freshDataRootAlreadyExists",
    "interactiveOperatorUnavailable",
    "dataRootAclMismatch",
    "uninstallerSignatureInvalid",
    "firewallApplyFailed",
    "firewallReadbackMismatch",
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
