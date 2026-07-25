$ErrorActionPreference = "Stop"

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

public static class LigaseCommandLine
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string commandLine,
        out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static string[] Parse(string raw)
    {
        int count;
        var pointer = CommandLineToArgvW(raw, out count);
        if (pointer == IntPtr.Zero)
            throw new InvalidOperationException("installArgumentsInvalid");
        try
        {
            if (count < 1)
                throw new InvalidOperationException("installArgumentsInvalid");
            var result = new string[count - 1];
            for (var index = 1; index < count; index++)
            {
                var value = Marshal.ReadIntPtr(pointer, index * IntPtr.Size);
                var argument = Marshal.PtrToStringUni(value);
                if (argument == null)
                    throw new InvalidOperationException("installArgumentsInvalid");
                result[index - 1] = argument;
            }
            return result;
        }
        finally
        {
            LocalFree(pointer);
        }
    }
}

public static class LigaseInteractiveSession
{
    private enum WTS_INFO_CLASS { WTSUserName = 5, WTSDomainName = 7 }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(
        uint processId, out uint sessionId);

    [DllImport("Wtsapi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr server, int sessionId, WTS_INFO_CLASS infoClass,
        out IntPtr buffer, out int bytesReturned);

    [DllImport("Wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    private static string Read(int sessionId, WTS_INFO_CLASS infoClass)
    {
        IntPtr buffer;
        int bytes;
        if (!WTSQuerySessionInformation(
            IntPtr.Zero, sessionId, infoClass, out buffer, out bytes) ||
            buffer == IntPtr.Zero)
            throw new InvalidOperationException("interactiveOperatorUnavailable");
        try { return Marshal.PtrToStringUni(buffer) ?? ""; }
        finally { WTSFreeMemory(buffer); }
    }

    public static string GetSid()
    {
        uint sessionId;
        if (!ProcessIdToSessionId(
            unchecked((uint)System.Diagnostics.Process.GetCurrentProcess().Id),
            out sessionId) || sessionId == 0)
            throw new InvalidOperationException("interactiveOperatorUnavailable");
        var user = Read((int)sessionId, WTS_INFO_CLASS.WTSUserName);
        var domain = Read((int)sessionId, WTS_INFO_CLASS.WTSDomainName);
        if (String.IsNullOrWhiteSpace(user))
            throw new InvalidOperationException("interactiveOperatorUnavailable");
        var account = String.IsNullOrWhiteSpace(domain) ? user : domain + "\\" + user;
        return ((SecurityIdentifier)new NTAccount(account).Translate(
            typeof(SecurityIdentifier))).Value;
    }
}
"@

function Fail([int] $ExitCode) {
  [Console]::Error.Write("installerArgumentsInvalid")
  exit $ExitCode
}

function Resolve-LocalPath([string] $Candidate) {
  if ([string]::IsNullOrWhiteSpace($Candidate)) { Fail 14 }
  if ($Candidate -notmatch '^[A-Za-z]:\\') { Fail 15 }
  if ($Candidate.StartsWith("\\", [StringComparison]::Ordinal) -or
      $Candidate.StartsWith("\\?\", [StringComparison]::Ordinal) -or
      $Candidate.StartsWith("\\.\", [StringComparison]::Ordinal)) {
    Fail 15
  }

  $full = [IO.Path]::GetFullPath($Candidate)
  $root = [IO.Path]::GetPathRoot($full)
  if ([string]::IsNullOrWhiteSpace($root) -or
      $full.TrimEnd('\') -eq $root.TrimEnd('\')) {
    Fail 16
  }
  try {
    if ([IO.DriveInfo]::new($root).DriveType -ne [IO.DriveType]::Fixed) {
      Fail 15
    }
  } catch {
    Fail 15
  }
  if (-not $Candidate.Equals($full, [StringComparison]::OrdinalIgnoreCase)) {
    Fail 17
  }
  return $full
}

function Read-Option(
  [string[]] $Arguments,
  [string] $Name
) {
  $prefix = "/$Name="
  $matches = @()
  foreach ($argument in $Arguments) {
    if ($argument.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
      $matches += $argument.Substring($prefix.Length)
    } elseif ($argument.Equals(
        "/$Name",
        [StringComparison]::OrdinalIgnoreCase) -or
      $argument.StartsWith(
        "/$Name",
        [StringComparison]::OrdinalIgnoreCase)) {
      Fail 12
    }
  }
  if ($matches.Count -gt 1) { Fail 13 }
  return $matches
}

function Assert-KnownArguments([string[]] $Arguments) {
  foreach ($argument in $Arguments) {
    if ($argument.StartsWith(
        "/InstallDirectory=",
        [StringComparison]::OrdinalIgnoreCase) -or
      $argument.StartsWith(
        "/DataRoot=",
        [StringComparison]::OrdinalIgnoreCase) -or
      $argument.StartsWith(
        "/ResultFile=",
        [StringComparison]::OrdinalIgnoreCase) -or
      $argument.Equals("/S", [StringComparison]::OrdinalIgnoreCase) -or
      $argument.Equals("/NCRC", [StringComparison]::OrdinalIgnoreCase) -or
      $argument.StartsWith("/D=", [StringComparison]::OrdinalIgnoreCase) -or
      $argument.StartsWith("_?=", [StringComparison]::OrdinalIgnoreCase)) {
      continue
    }
    Fail 12
  }
}

function Read-ExistingDataRoot([string] $BootstrapPath) {
  if ([string]::IsNullOrWhiteSpace($BootstrapPath) -or
      -not (Test-Path -LiteralPath $BootstrapPath -PathType Leaf)) {
    return $null
  }
  try {
    $bytes = [IO.File]::ReadAllBytes($BootstrapPath)
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    if ([regex]::Matches($text, '"schemaVersion"\s*:').Count -ne 1 -or
        [regex]::Matches($text, '"dataRoot"\s*:').Count -ne 1) {
      Fail 18
    }
    $document = $text | ConvertFrom-Json
    $properties = @($document.PSObject.Properties.Name)
    if ($properties.Count -ne 2 -or
        $properties -notcontains "schemaVersion" -or
        $properties -notcontains "dataRoot" -or
        $document.schemaVersion -ne 1) {
      Fail 18
    }
    $resolved = Resolve-LocalPath ([string]$document.dataRoot)
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
      Fail 18
    }
    return $resolved
  } catch {
    Fail 18
  }
}

function Get-InteractiveOperatorLocalAppData {
  try {
    $sid = [LigaseInteractiveSession]::GetSid()
    $profile = Get-ItemPropertyValue -LiteralPath (
      "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid") `
      -Name ProfileImagePath
    $profilePath = [Environment]::ExpandEnvironmentVariables([string]$profile)
    return Resolve-LocalPath (Join-Path ([IO.Path]::GetFullPath($profilePath)) "AppData\Local")
  } catch {
    Fail 18
  }
}

function Test-LegacyMigrationEligible([string] $Root) {
  try {
    $operatorLocalAppData = Get-InteractiveOperatorLocalAppData
    $legacyBase = Resolve-LocalPath (Join-Path $operatorLocalAppData "Ligase Host\Instances")
    $prefix = $legacyBase.TrimEnd('\') + '\'
    if (-not $Root.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
      return $false
    }
    $relative = $Root.Substring($prefix.Length)
    $id = [guid]::Empty
    if ($relative.Contains([IO.Path]::DirectorySeparatorChar) -or
        -not [guid]::TryParseExact($relative, "D", [ref]$id)) {
      return $false
    }
    $item = Get-Item -LiteralPath $Root -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      return $false
    }
    $security = Get-Acl -LiteralPath $Root
    $operatorSid = [LigaseInteractiveSession]::GetSid()
    $operatorRules = @($security.Access | Where-Object {
      $_.AccessControlType -eq "Allow" -and
      $_.IdentityReference.Translate(
        [Security.Principal.SecurityIdentifier]).Value -ceq $operatorSid
    })
    return -not $security.AreAccessRulesProtected -and
      $operatorRules.Count -gt 0
  } catch {
    return $false
  }
}

function Test-ExactExistingDataRoot([string] $Root) {
  try {
    $item = Get-Item -LiteralPath $Root -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      return $false
    }
    $operatorSid = [LigaseInteractiveSession]::GetSid()
    $systemSid = [Security.Principal.SecurityIdentifier]::new(
      [Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
    $administratorsSid = [Security.Principal.SecurityIdentifier]::new(
      [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
    $security = Get-Acl -LiteralPath $Root
    $owner = ([Security.Principal.NTAccount]$security.Owner).Translate(
      [Security.Principal.SecurityIdentifier]).Value
    $rules = @($security.Access | Where-Object { -not $_.IsInherited })
    if (-not $security.AreAccessRulesProtected -or
        $owner -cne $administratorsSid.Value -or
        $rules.Count -ne 3 -or
        @($rules | Where-Object {
          $_.AccessControlType -ne "Allow"
        }).Count -ne 0) {
      return $false
    }
    $inheritance = [int](
      [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
      [Security.AccessControl.InheritanceFlags]::ObjectInherit)
    $propagation = [int][Security.AccessControl.PropagationFlags]::None
    $actual = @($rules | ForEach-Object {
      $sid = $_.IdentityReference.Translate(
        [Security.Principal.SecurityIdentifier]).Value
      "$sid,$([int]$_.FileSystemRights),$([int]$_.InheritanceFlags),$([int]$_.PropagationFlags)"
    } | Sort-Object)
    $expected = @(
      "$operatorSid,$([int](
        [Security.AccessControl.FileSystemRights]::Modify -bor
        [Security.AccessControl.FileSystemRights]::Synchronize)),$inheritance,$propagation",
      "$($systemSid.Value),$([int][Security.AccessControl.FileSystemRights]::FullControl),$inheritance,$propagation",
      "$($administratorsSid.Value),$([int][Security.AccessControl.FileSystemRights]::FullControl),$inheritance,$propagation"
    ) | Sort-Object
    return $actual.Count -eq $expected.Count -and
      (Compare-Object $actual $expected).Count -eq 0
  } catch {
    return $false
  }
}

try {
  $raw = [Environment]::GetEnvironmentVariable(
    "LIGASE_INSTALL_RAW_PARAMETERS",
    [EnvironmentVariableTarget]::Process)
  $registered = [Environment]::GetEnvironmentVariable(
    "LIGASE_INSTALL_REGISTERED_LOCATION",
    [EnvironmentVariableTarget]::Process)
  $default = [Environment]::GetEnvironmentVariable(
    "LIGASE_INSTALL_DEFAULT_LOCATION",
    [EnvironmentVariableTarget]::Process)
  $resultPath = [Environment]::GetEnvironmentVariable(
    "LIGASE_INSTALL_ARGUMENT_RESULT",
    [EnvironmentVariableTarget]::Process)
  $programData = [Environment]::GetEnvironmentVariable(
    "LIGASE_INSTALL_PROGRAM_DATA",
    [EnvironmentVariableTarget]::Process)
  if ([string]::IsNullOrWhiteSpace($raw)) { $raw = "" }
  if ([string]::IsNullOrWhiteSpace($resultPath)) { Fail 18 }

  $arguments = [LigaseCommandLine]::Parse($raw)
  Assert-KnownArguments $arguments
  $installOptions = @(Read-Option $arguments "InstallDirectory")
  $dataOptions = @(Read-Option $arguments "DataRoot")

  $installCandidate = if ($installOptions.Count -eq 1) {
    $installOptions[0]
  } elseif (-not [string]::IsNullOrWhiteSpace($registered)) {
    $registered
  } else {
    $default
  }
  $installDirectory = Resolve-LocalPath $installCandidate
  $bootstrapPath = Join-Path $installDirectory "ligase-bootstrap.json"
  $existingDataRoot = Read-ExistingDataRoot $bootstrapPath
  $dataRootMode = "explicit"
  $dataRootSource = ""
  $dataRoot = if ($null -ne $existingDataRoot) {
    if (Test-LegacyMigrationEligible $existingDataRoot) {
      if ([string]::IsNullOrWhiteSpace($programData)) { Fail 18 }
      $dataRootSource = $existingDataRoot
      $standardBase = Resolve-LocalPath (
        Join-Path ([IO.Path]::GetFullPath($programData)) "Ligase Host\Instances")
      if ($dataOptions.Count -eq 1) {
        $requested = Resolve-LocalPath $dataOptions[0]
        $prefix = $standardBase.TrimEnd('\') + '\'
        if (-not $requested.StartsWith(
            $prefix, [StringComparison]::OrdinalIgnoreCase)) {
          Fail 18
        }
        $relative = $requested.Substring($prefix.Length)
        $id = [guid]::Empty
        if ($relative.Contains([IO.Path]::DirectorySeparatorChar) -or
            -not [guid]::TryParseExact($relative, "D", [ref]$id)) {
          Fail 18
        }
        $requested
      } else {
        Resolve-LocalPath (
          Join-Path $standardBase ([guid]::NewGuid().ToString("D")))
      }
      $dataRootMode = "migration"
    } elseif (Test-ExactExistingDataRoot $existingDataRoot) {
      if ($dataOptions.Count -eq 1) {
        $requested = Resolve-LocalPath $dataOptions[0]
        if (-not $requested.Equals(
            $existingDataRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
          Fail 18
        }
      }
      $dataRootMode = "existing"
      $existingDataRoot
    } else {
      Fail 18
    }
  } elseif ($dataOptions.Count -eq 1) {
    Resolve-LocalPath $dataOptions[0]
  } else {
    if ([string]::IsNullOrWhiteSpace($programData)) { Fail 18 }
    $base = Resolve-LocalPath (
      Join-Path ([IO.Path]::GetFullPath($programData)) "Ligase Host")
    $dataRootMode = "freshDefault"
    Resolve-LocalPath (
      Join-Path $base ("Instances\" + [guid]::NewGuid().ToString("D")))
  }

  $parent = Split-Path -Parent $resultPath
  if (-not (Test-Path -LiteralPath $parent -PathType Container)) { Fail 18 }
  $pending = "$resultPath.pending"
  [IO.File]::WriteAllLines(
    $pending,
    @(
      $installDirectory,
      $dataRoot,
      ($installOptions.Count -eq 1).ToString().ToLowerInvariant(),
      ($dataOptions.Count -eq 1).ToString().ToLowerInvariant(),
      $dataRootMode,
      $dataRootSource
    ),
    [Text.Encoding]::Unicode)
  Move-Item -LiteralPath $pending -Destination $resultPath -Force
  [Console]::Out.Write("installerArgumentsResolved")
  exit 0
} catch {
  Fail 18
}
