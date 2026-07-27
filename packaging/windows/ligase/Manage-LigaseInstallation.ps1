[CmdletBinding()]
param(
  [ValidateSet(
    "DryRun",
    "Install",
    "FinalizeInstall",
    "ReconcileShortcuts",
    "ValidateInstallTransaction",
    "PreflightInstallTransaction",
    "RecordEvidence",
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
  [switch]$MigrateDataRoot,
  [switch]$RecoverOrphanDataRoot,
  [string]$RecoveryDataRootSource,
  [switch]$ConfirmLegacyTrustCleanup,
  [ValidateSet(
    "initialized",
    "confirmed",
    "integrating",
    "finalReadback",
    "succeeded",
    "failed",
    "cancelled")]
  [string]$EvidencePhase = "initialized",
  [ValidateSet("unknown", "true", "false")]
  [string]$EvidenceSuccess = "unknown",
  [string]$EvidenceResultCode = "notStarted",
  [ValidateSet(
    "none",
    "createFresh",
    "preserveExisting",
    "migrateToStandard",
    "recoverOrphanLegacyDataRoot")]
  [string]$EvidenceDataRootAction = "none",
  [string]$EvidenceDataRootSource,
  [int]$EvidenceHelperExit = -1,
  [ValidateSet("notRequired", "completed", "failed", "unknown")]
  [string]$EvidenceRollback = "notRequired",
  [ValidateSet("notChecked", "configured", "failed", "residual", "unknown")]
  [string]$EvidenceFirewall = "notChecked",
  [ValidateSet(
    "none",
    "artifacts",
    "bootstrap",
    "dataRoot",
    "installTransaction",
    "arp",
    "startMenu",
    "desktop",
    "firewall",
    "virtualDisplay")]
  [string]$EvidenceFailedField = "none",
  [int]$EvidenceTransactionHelperNativeExit = -1,
  [ValidateSet(
    "none",
    "resolveProgramData",
    "rejectReparse",
    "createSegment",
    "openHandle",
    "verifyIdentity",
    "resolveFinalPath",
    "applyAcl",
    "assertAcl",
    "createTemp",
    "atomicReplace",
    "finalReadback",
    "read",
    "delete",
    "inputValidation",
    "processTimeout")]
  [string]$EvidenceTransactionHelperStage = "none",
  [ValidateSet(
    "none",
    "fileNotFound",
    "pathNotFound",
    "accessDenied",
    "invalidHandle",
    "busy",
    "invalidParameter",
    "privilegeNotHeld",
    "invalidOwner",
    "invalidAcl",
    "notSupported",
    "identityChanged",
    "bindingMismatch",
    "unknown")]
  [string]$EvidenceTransactionHelperNativeCategory = "none",
  [int]$EvidenceTransactionHelperNativeCode = 0,
  [ValidateSet(
    "none",
    "trustedRootInvalid",
    "volumeMismatch",
    "segmentMismatch",
    "fileIdentityMismatch")]
  [string]$EvidenceTransactionBindingReason = "none",
  [ValidateSet(
    "none", "dosDrive", "volumeGuid", "device", "unc", "unknown")]
  [string]$EvidenceTransactionBindingRootKind = "none",
  [ValidateRange(0, 32)]
  [int]$EvidenceTransactionBindingSegmentCount = 0,
  [switch]$EvidenceTransactionBindingPrefixMatched,
  [switch]$EvidenceTransactionBindingVolumeMatched,
  [switch]$EvidenceTransactionBindingFileIdentityMatched,
  [switch]$EvidenceTransactionAclMutationOccurred,
  [ValidateSet("notRequired", "completed", "failed")]
  [string]$EvidenceTransactionAclRollback = "notRequired",
  [ValidateSet("none", "recoverEmptyAdminRoot")]
  [string]$EvidenceTransactionRecoveryAction = "none",
  [ValidateSet("unknown", "absent", "empty", "nonEmpty")]
  [string]$EvidenceInstallResidue = "unknown",
  [ValidateSet("unknown", "absent", "empty", "nonEmpty")]
  [string]$EvidenceDataRootResidue = "unknown",
  [string]$EvidenceManifestPath,
  [switch]$DesktopShortcutSelected,
  [string]$ShortcutCommonProgramsRoot,
  [string]$ShortcutCommonDesktopRoot,
  [string]$ShortcutLegacyProgramsRoot,
  [string]$ShortcutLegacyDesktopRoot,
  [string]$InstallTransactionRoot,
  [switch]$VirtualDisplaySelected,
  [ValidateSet("notSelected", "installed", "failed", "declined", "unknown")]
  [string]$VirtualDisplayOutcome = "unknown"
)

$ErrorActionPreference = "Stop"
$installRoot = [IO.Path]::GetFullPath($InstallDirectory)
$manifestPath = Join-Path $installRoot "ligase-install-manifest.json"
$bootstrapPath = Join-Path $installRoot "ligase-bootstrap.json"
$script:freshDataRootCreated = $null
$script:migrationRollback = $null
$script:shortcutRollback = $null
$script:firewallAppliedByTransaction = $false
$script:firewallWasConfigured = $false
$script:installTransactionId = $null
$script:installTransactionCreatedUtc = $null
$script:rollbackResult = "notRequired"
$script:shortcutRollbackResult = "notRequired"
$script:firewallRollbackResult = "notRequired"
$script:transactionCleanupResult = "notCreated"
$script:transactionHelperNativeExit = -1
$script:transactionHelperStage = "none"
$script:transactionHelperNativeCategory = "none"
$script:transactionHelperNativeCode = 0
$script:transactionBindingReason = "none"
$script:transactionBindingRootKind = "none"
$script:transactionBindingSegmentCount = 0
$script:transactionBindingPrefixMatched = $false
$script:transactionBindingVolumeMatched = $false
$script:transactionBindingFileIdentityMatched = $false
$script:transactionAclMutationOccurred = $false
$script:transactionAclRollback = "notRequired"
$script:transactionRecoveryAction = "none"
$script:transactionCreated = $false
$script:finalFailedField = "none"
$script:finalComponents = [ordered]@{
  artifacts = "pending"
  bootstrap = "pending"
  dataRoot = "pending"
  installTransaction = "pending"
  arp = "pending"
  startMenu = "pending"
  desktop = "pending"
  firewall = "pending"
  virtualDisplay = "pending"
}

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

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class LigaseFileIdentity
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle, out BY_HANDLE_FILE_INFORMATION information);

    public static uint GetLinkCount(string path)
    {
        using (var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            BY_HANDLE_FILE_INFORMATION information;
            if (!GetFileInformationByHandle(stream.SafeFileHandle, out information))
                throw new IOException("dataRootEnumerationFailed");
            return information.NumberOfLinks;
        }
    }
}
"@

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public static class LigaseStrictJson
{
    public static bool HasUniqueProperties(string value)
    {
        try
        {
            var parser = new Parser(value);
            parser.ParseValue();
            parser.SkipWhitespace();
            return parser.AtEnd;
        }
        catch
        {
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string _value;
        private int _offset;

        public Parser(string value)
        {
            if (value == null) throw new ArgumentNullException("value");
            _value = value;
        }

        public bool AtEnd
        {
            get { return _offset == _value.Length; }
        }

        public void SkipWhitespace()
        {
            while (_offset < _value.Length &&
                   (_value[_offset] == ' ' || _value[_offset] == '\t' ||
                    _value[_offset] == '\r' || _value[_offset] == '\n'))
                _offset++;
        }

        public void ParseValue()
        {
            SkipWhitespace();
            if (_offset >= _value.Length) throw new FormatException();
            switch (_value[_offset])
            {
                case '{': ParseObject(); return;
                case '[': ParseArray(); return;
                case '"': ParseString(); return;
                case 't': ParseLiteral("true"); return;
                case 'f': ParseLiteral("false"); return;
                case 'n': ParseLiteral("null"); return;
                default: ParseNumber(); return;
            }
        }

        private void ParseObject()
        {
            _offset++;
            SkipWhitespace();
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (Consume('}')) return;
            while (true)
            {
                SkipWhitespace();
                var name = ParseString();
                if (!names.Add(name)) throw new FormatException();
                SkipWhitespace();
                Require(':');
                ParseValue();
                SkipWhitespace();
                if (Consume('}')) return;
                Require(',');
            }
        }

        private void ParseArray()
        {
            _offset++;
            SkipWhitespace();
            if (Consume(']')) return;
            while (true)
            {
                ParseValue();
                SkipWhitespace();
                if (Consume(']')) return;
                Require(',');
            }
        }

        private string ParseString()
        {
            Require('"');
            var result = new StringBuilder();
            while (_offset < _value.Length)
            {
                var current = _value[_offset++];
                if (current == '"') return result.ToString();
                if (current < 0x20) throw new FormatException();
                if (current != '\\')
                {
                    result.Append(current);
                    continue;
                }
                if (_offset >= _value.Length) throw new FormatException();
                var escaped = _value[_offset++];
                switch (escaped)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (_offset + 4 > _value.Length)
                            throw new FormatException();
                        result.Append((char)Int32.Parse(
                            _value.Substring(_offset, 4),
                            NumberStyles.AllowHexSpecifier,
                            CultureInfo.InvariantCulture));
                        _offset += 4;
                        break;
                    default: throw new FormatException();
                }
            }
            throw new FormatException();
        }

        private void ParseNumber()
        {
            var start = _offset;
            if (Consume('-')) { }
            if (Consume('0'))
            {
                if (_offset < _value.Length &&
                    Char.IsDigit(_value[_offset]))
                    throw new FormatException();
            }
            else
            {
                if (_offset >= _value.Length ||
                    _value[_offset] < '1' || _value[_offset] > '9')
                    throw new FormatException();
                while (_offset < _value.Length &&
                       Char.IsDigit(_value[_offset])) _offset++;
            }
            if (Consume('.'))
            {
                var fraction = _offset;
                while (_offset < _value.Length &&
                       Char.IsDigit(_value[_offset])) _offset++;
                if (_offset == fraction) throw new FormatException();
            }
            if (_offset < _value.Length &&
                (_value[_offset] == 'e' || _value[_offset] == 'E'))
            {
                _offset++;
                if (_offset < _value.Length &&
                    (_value[_offset] == '+' || _value[_offset] == '-'))
                    _offset++;
                var exponent = _offset;
                while (_offset < _value.Length &&
                       Char.IsDigit(_value[_offset])) _offset++;
                if (_offset == exponent) throw new FormatException();
            }
            if (_offset == start) throw new FormatException();
        }

        private void ParseLiteral(string expected)
        {
            if (_offset + expected.Length > _value.Length ||
                String.CompareOrdinal(
                    _value, _offset, expected, 0, expected.Length) != 0)
                throw new FormatException();
            _offset += expected.Length;
        }

        private bool Consume(char expected)
        {
            if (_offset >= _value.Length || _value[_offset] != expected)
                return false;
            _offset++;
            return true;
        }

        private void Require(char expected)
        {
            if (!Consume(expected)) throw new FormatException();
        }
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

function Get-SafePathProjection([string]$Path) {
  if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
  $full = [IO.Path]::GetFullPath($Path)
  $root = [IO.Path]::GetPathRoot($full)
  $leaf = Split-Path -Leaf $full.TrimEnd('\')
  return [ordered]@{
    volume = $root.TrimEnd('\')
    leaf = $leaf
    sha256 = Get-ByteSha256 (
      [Text.Encoding]::UTF8.GetBytes($full.ToUpperInvariant()))
  }
}

function Get-EvidenceSourceHead {
  $candidate = if (-not [string]::IsNullOrWhiteSpace($EvidenceManifestPath)) {
    [IO.Path]::GetFullPath($EvidenceManifestPath)
  } else {
    $manifestPath
  }
  if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
    throw "installManifestMissing"
  }
  $document = [IO.File]::ReadAllText($candidate) | ConvertFrom-Json
  $sourceHead = [string]$document.sourceHead
  if ($sourceHead -notmatch '^[0-9a-f]{40}$') {
    throw "installManifestInvalid"
  }
  return $sourceHead
}

function Get-InstallerEvidencePath {
  $common = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::CommonApplicationData)
  if ([string]::IsNullOrWhiteSpace($common)) {
    throw "installerEvidenceUnavailable"
  }
  return Join-Path $common "Ligase Host\Installer\last-outcome.json"
}

function Get-InstallTransactionHelperPath {
  $path = Join-Path $installRoot (
    "Deployment\Ligase.Installation.TransactionHelper.exe")
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "installTransactionUnavailable"
  }
  $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
  $matches = @($manifest.privilegedHelpers | Where-Object {
    [string]$_.relativePath -ceq (
      "Deployment/Ligase.Installation.TransactionHelper.exe")
  })
  $expectedHelperHash = if ($matches.Count -eq 1) {
    [string]$matches[0].signedArtifactSha256
  } else { "" }
  if ($matches.Count -ne 1 -or
      $expectedHelperHash -notmatch '^[0-9a-f]{64}$' -or
      (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -cne
        $expectedHelperHash.ToUpperInvariant()) {
    throw "installTransactionUnavailable"
  }
  return $path
}

function Invoke-InstallTransactionHelper(
  [ValidateSet("write", "read", "delete", "validate", "preflight")]
  [string]$HelperAction,
  [string]$InputValue = ""
) {
  if ($HelperAction -eq "write" -and
      [Text.Encoding]::UTF8.GetByteCount($InputValue) -gt
        (4 * 1024 * 1024 + 64 * 1024)) {
    $script:transactionHelperNativeExit = 18
    $script:transactionHelperStage = "inputValidation"
    throw "installTransactionInvalid"
  }
  $isValidationOverride =
    -not [string]::IsNullOrWhiteSpace($InstallTransactionRoot)
  if ($isValidationOverride -and
      $env:LIGASE_INSTALL_VALIDATION_HARNESS -cne "1") {
    throw "installTransactionTestOverrideRejected"
  }
  $start = [Diagnostics.ProcessStartInfo]::new()
  $start.FileName = Get-InstallTransactionHelperPath
  $start.Arguments = $HelperAction
  if ($isValidationOverride) {
    $start.EnvironmentVariables[
      "LIGASE_TRANSACTION_TEST_ROOT"] = [IO.Path]::GetFullPath(
        $InstallTransactionRoot)
  }
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.RedirectStandardInput = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $start
  if (-not $process.Start()) { throw "installTransactionUnavailable" }
  try {
    $stdoutBuffer = [char[]]::new(256)
    $stderrBuffer = [char[]]::new(256)
    $stdoutBuilder = [Text.StringBuilder]::new()
    $stderrBuilder = [Text.StringBuilder]::new()
    $stdoutOverflow = $false
    $stderrOverflow = $false
    $stdoutClosed = $false
    $stderrClosed = $false
    $stdoutTask = $process.StandardOutput.ReadAsync(
      $stdoutBuffer, 0, $stdoutBuffer.Length)
    $stderrTask = $process.StandardError.ReadAsync(
      $stderrBuffer, 0, $stderrBuffer.Length)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $stdinClosed = $HelperAction -ne "write"
    $stdinFlushing = $false
    $stdinTask = if ($HelperAction -eq "write") {
      $process.StandardInput.WriteAsync($InputValue)
    } else {
      $process.StandardInput.Close()
      $null
    }
    $timedOut = $false
    $pipeFault = $false
    while (-not ($process.HasExited -and $stdinClosed -and
        $stdoutClosed -and $stderrClosed)) {
      if (-not $stdinClosed -and $stdinTask.IsCompleted) {
        try {
          $null = $stdinTask.GetAwaiter().GetResult()
          if (-not $stdinFlushing) {
            $stdinTask = $process.StandardInput.FlushAsync()
            $stdinFlushing = $true
          } else {
            $process.StandardInput.Close()
            $stdinClosed = $true
          }
        } catch {
          $script:transactionHelperNativeExit = 18
          $script:transactionHelperStage = "processTimeout"
          $pipeFault = $true
          $timedOut = $true
          break
        }
      }
      if ($stdoutTask.IsCompleted) {
        try {
          $count = $stdoutTask.GetAwaiter().GetResult()
        } catch {
          $pipeFault = $true
          $timedOut = $true
          break
        }
        if ($count -eq 0) {
          $stdoutClosed = $true
        } else {
          if ($stdoutBuilder.Length + $count -le 4096) {
            $null = $stdoutBuilder.Append($stdoutBuffer, 0, $count)
          } else {
            $stdoutOverflow = $true
          }
          $stdoutTask = $process.StandardOutput.ReadAsync(
            $stdoutBuffer, 0, $stdoutBuffer.Length)
        }
      }
      if ($stderrTask.IsCompleted) {
        try {
          $count = $stderrTask.GetAwaiter().GetResult()
        } catch {
          $pipeFault = $true
          $timedOut = $true
          break
        }
        if ($count -eq 0) {
          $stderrClosed = $true
        } else {
          if ($stderrBuilder.Length + $count -le 4096) {
            $null = $stderrBuilder.Append($stderrBuffer, 0, $count)
          } else {
            $stderrOverflow = $true
          }
          $stderrTask = $process.StandardError.ReadAsync(
            $stderrBuffer, 0, $stderrBuffer.Length)
        }
      }
      if ($clock.ElapsedMilliseconds -ge 15000) {
        $timedOut = $true
        break
      }
      if (-not ($process.HasExited -and $stdinClosed -and
          $stdoutClosed -and $stderrClosed)) {
        Start-Sleep -Milliseconds 20
      }
    }
    if ($timedOut) {
      $script:transactionHelperNativeExit = 18
      $script:transactionHelperStage = "processTimeout"
      try {
        $taskKillStart = [Diagnostics.ProcessStartInfo]::new()
        $taskKillStart.FileName = Join-Path $env:SystemRoot (
          "System32\taskkill.exe")
        $taskKillStart.Arguments = "/PID $($process.Id) /T /F"
        $taskKillStart.UseShellExecute = $false
        $taskKillStart.CreateNoWindow = $true
        $taskKillStart.RedirectStandardOutput = $true
        $taskKillStart.RedirectStandardError = $true
        $taskKill = [Diagnostics.Process]::Start($taskKillStart)
        if ($null -ne $taskKill) {
          $null = $taskKill.StandardOutput.ReadToEndAsync()
          $null = $taskKill.StandardError.ReadToEndAsync()
          if (-not $taskKill.WaitForExit(2000)) {
            try { $taskKill.Kill() } catch {}
          }
          $taskKill.Dispose()
        }
      } catch {}
      if (-not $process.HasExited) {
        try { $process.Kill() } catch {}
      }
      $killClock = [Diagnostics.Stopwatch]::StartNew()
      while (-not $process.HasExited -and
          $killClock.ElapsedMilliseconds -lt 2000) {
        Start-Sleep -Milliseconds 20
      }
      if ($pipeFault) {
        throw "installTransactionInvalid"
      }
      throw "installTransactionUnavailable"
    }
    if ($stdoutOverflow -or $stderrOverflow) {
      $script:transactionHelperNativeExit = 18
      $script:transactionHelperStage = "processTimeout"
      throw "installTransactionInvalid"
    }
    $stdout = $stdoutBuilder.ToString()
    $stderr = $stderrBuilder.ToString()
    $script:transactionHelperNativeExit = $process.ExitCode
    if ($process.ExitCode -ne 0) {
      try {
        if ([Text.Encoding]::UTF8.GetByteCount($stderr) -gt 512) {
          throw "installTransactionInvalid"
        }
        if (-not [LigaseStrictJson]::HasUniqueProperties($stderr)) {
          throw "installTransactionInvalid"
        }
        $failure = $stderr | ConvertFrom-Json
        $properties = @($failure.PSObject.Properties.Name)
        if ($properties.Count -ne 12 -or
            $properties -notcontains "code" -or
            $properties -notcontains "stage" -or
            $properties -notcontains "nativeCategory" -or
            $properties -notcontains "nativeCode" -or
            $properties -notcontains "bindingReason" -or
            $properties -notcontains "bindingRootKind" -or
            $properties -notcontains "bindingSegmentCount" -or
            $properties -notcontains "bindingPrefixMatched" -or
            $properties -notcontains "bindingVolumeMatched" -or
            $properties -notcontains "bindingFileIdentityMatched" -or
            $properties -notcontains "aclMutationOccurred" -or
            $properties -notcontains "aclRollback" -or
            [string]$failure.code -notin @(
              "installTransactionAclInvalid",
              "installTransactionUnavailable",
              "installTransactionInvalid") -or
            [string]$failure.stage -notin @(
              "resolveProgramData", "rejectReparse", "createSegment",
              "openHandle", "verifyIdentity", "resolveFinalPath",
              "applyAcl", "assertAcl", "createTemp",
              "atomicReplace", "finalReadback", "read", "delete",
              "inputValidation", "processTimeout")) {
          throw "installTransactionInvalid"
        }
        $nativeCategory = [string]$failure.nativeCategory
        $nativeCode = [int]$failure.nativeCode
        $allowedNativeCodes = @{
          none = @(0)
          fileNotFound = @(2)
          pathNotFound = @(3)
          accessDenied = @(5)
          invalidHandle = @(6)
          busy = @(32)
          invalidParameter = @(87)
          privilegeNotHeld = @(1314)
          invalidOwner = @(1307)
          invalidAcl = @(1336)
          notSupported = @(50)
          identityChanged = @(0)
          bindingMismatch = @(0)
          unknown = @(0)
        }
        $nativeCodeValid = if ($nativeCategory -ceq "unknown") {
          $nativeCode -ge 1 -and $nativeCode -le 65535
        } else {
          $allowedNativeCodes.ContainsKey($nativeCategory) -and
            $nativeCode -in $allowedNativeCodes[$nativeCategory]
        }
        if (-not $nativeCodeValid) {
          throw "installTransactionInvalid"
        }
        $bindingReason = [string]$failure.bindingReason
        $bindingRootKind = [string]$failure.bindingRootKind
        $bindingSegmentCount = [int]$failure.bindingSegmentCount
        $bindingReasonValid = $bindingReason -in @(
          "none", "trustedRootInvalid", "volumeMismatch",
          "segmentMismatch", "fileIdentityMismatch")
        $bindingRootKindValid = $bindingRootKind -in @(
          "none", "dosDrive", "volumeGuid", "device", "unc", "unknown")
        if (-not $bindingReasonValid -or
            -not $bindingRootKindValid -or
            $bindingSegmentCount -lt 0 -or
            $bindingSegmentCount -gt 32 -or
            $failure.bindingPrefixMatched -isnot [bool] -or
            $failure.bindingVolumeMatched -isnot [bool] -or
            $failure.bindingFileIdentityMatched -isnot [bool] -or
            (($nativeCategory -ceq "bindingMismatch") -ne
              ($bindingReason -cne "none"))) {
          throw "installTransactionInvalid"
        }
        if ($failure.aclMutationOccurred -isnot [bool] -or
            [string]$failure.aclRollback -notin @(
              "notRequired", "completed", "failed") -or
            (-not [bool]$failure.aclMutationOccurred -and
              [string]$failure.aclRollback -ne "notRequired")) {
          throw "installTransactionInvalid"
        }
        $script:transactionHelperStage = [string]$failure.stage
        $script:transactionHelperNativeCategory = $nativeCategory
        $script:transactionHelperNativeCode = $nativeCode
        $script:transactionBindingReason = $bindingReason
        $script:transactionBindingRootKind = $bindingRootKind
        $script:transactionBindingSegmentCount = $bindingSegmentCount
        $script:transactionBindingPrefixMatched =
          [bool]$failure.bindingPrefixMatched
        $script:transactionBindingVolumeMatched =
          [bool]$failure.bindingVolumeMatched
        $script:transactionBindingFileIdentityMatched =
          [bool]$failure.bindingFileIdentityMatched
        $script:transactionAclMutationOccurred =
          [bool]$failure.aclMutationOccurred
        $script:transactionAclRollback = [string]$failure.aclRollback
        throw [string]$failure.code
      } catch {
        if ($_.Exception.Message -in @(
            "installTransactionAclInvalid",
            "installTransactionUnavailable",
            "installTransactionInvalid")) {
          throw
        }
        throw "installTransactionInvalid"
      }
    }
    $script:transactionHelperStage = if ($HelperAction -eq "preflight") {
      "finalReadback"
    } elseif ($HelperAction -in @("read", "delete")) {
      $HelperAction
    } else { "finalReadback" }
    return $stdout
  } finally {
    $process.Dispose()
  }
}

function Invoke-InstallTransactionPreflight {
  $result = Invoke-InstallTransactionHelper "preflight"
  try {
    $document = $result | ConvertFrom-Json
    $properties = @($document.PSObject.Properties.Name)
  } catch {
    throw "installTransactionInvalid"
  }
  if ($properties.Count -ne 3 -or
      $properties -notcontains "code" -or
      $properties -notcontains "stage" -or
      $properties -notcontains "recoveryAction" -or
      [string]$document.code -cne "installTransactionPreflightReady" -or
      [string]$document.stage -cne "finalReadback" -or
      [string]$document.recoveryAction -notin @(
        "none", "recoverEmptyAdminRoot")) {
    throw "installTransactionInvalid"
  }
  $script:transactionRecoveryAction = [string]$document.recoveryAction
  $script:finalComponents.installTransaction = "verified"
  $script:transactionCleanupResult = "notCreated"
}

function Get-ExpectedShortcutEntries {
  $roots = Get-ShortcutRoots
  return @(
    [ordered]@{
      field = "startMenu"
      path = Join-Path ([string]$roots.commonPrograms) (
        "Ligase Host\Ligase Host.lnk")
    },
    [ordered]@{
      field = "desktop"
      path = Join-Path ([string]$roots.commonDesktop) "Ligase Host.lnk"
    },
    [ordered]@{
      field = "startMenu"
      path = Join-Path ([string]$roots.legacyPrograms) (
        "Ligase Host\Ligase Host.lnk")
    },
    [ordered]@{
      field = "desktop"
      path = Join-Path ([string]$roots.legacyDesktop) "Ligase Host.lnk"
    })
}

function Assert-ClosedProperties($Object, [string[]]$Expected) {
  $actual = @($Object.PSObject.Properties.Name)
  if ($actual.Count -ne $Expected.Count) {
    throw "installTransactionInvalid"
  }
  foreach ($name in $Expected) {
    if ($actual -cnotcontains $name) { throw "installTransactionInvalid" }
  }
}

function Assert-TransactionRawShape([string]$Raw) {
  $top = @(
    "schemaVersion", "transactionId", "createdUtc", "manifestSourceHead",
    "manifestSha256", "installLayout", "installDirectory", "launcher",
    "desktopSelected", "virtualDisplaySelected", "configureFirewall",
    "shortcuts", "firewallApplied", "firewallWasConfigured")
  foreach ($name in $top) {
    if ([regex]::Matches($Raw, '"' + [regex]::Escape($name) + '"\s*:').Count -ne
        1) {
      throw "installTransactionInvalid"
    }
  }
  foreach ($name in @("field", "path", "existed", "bytes")) {
    if ([regex]::Matches($Raw, '"' + $name + '"\s*:').Count -ne 4) {
      throw "installTransactionInvalid"
    }
  }
}

function Save-InstallTransaction {
  if ($null -eq $script:shortcutRollback) { return }
  if ([string]::IsNullOrWhiteSpace($script:installTransactionId)) {
    $random = [byte[]]::new(32)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($random) } finally { $generator.Dispose() }
    $script:installTransactionId = [Convert]::ToBase64String($random)
    $script:installTransactionCreatedUtc = [DateTime]::UtcNow
  }
  $document = [ordered]@{
    schemaVersion = 1
    transactionId = $script:installTransactionId
    createdUtc = $script:installTransactionCreatedUtc.ToString(
      "O", [Globalization.CultureInfo]::InvariantCulture)
    manifestSourceHead = Get-EvidenceSourceHead
    manifestSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestPath).Hash
    installLayout = "structured-v1"
    installDirectory = $installRoot
    launcher = [string]$script:shortcutRollback.launcher
    desktopSelected = [bool]$DesktopShortcutSelected
    virtualDisplaySelected = [bool]$VirtualDisplaySelected
    configureFirewall = [bool]$ConfigureFirewall
    shortcuts = @($script:shortcutRollback.snapshot)
    firewallApplied = [bool]$script:firewallAppliedByTransaction
    firewallWasConfigured = [bool]$script:firewallWasConfigured
  }
  $raw = $document | ConvertTo-Json -Depth 6 -Compress
  $result = Invoke-InstallTransactionHelper "write" $raw
  if ($result -cne
      '{"code":"installTransactionWritten","success":true}') {
    throw "installTransactionInvalid"
  }
  $script:transactionCreated = $true
  $script:transactionCleanupResult = "pending"
  $script:finalComponents.installTransaction = "verified"
}

function Load-InstallTransaction {
  $raw = Invoke-InstallTransactionHelper "read"
  Assert-TransactionRawShape $raw
  $document = $raw | ConvertFrom-Json
  Assert-ClosedProperties $document @(
    "schemaVersion", "transactionId", "createdUtc", "manifestSourceHead",
    "manifestSha256", "installLayout", "installDirectory", "launcher",
    "desktopSelected", "virtualDisplaySelected", "configureFirewall",
    "shortcuts", "firewallApplied", "firewallWasConfigured")
  $created = [DateTime]::MinValue
  if (-not [DateTime]::TryParseExact(
      [string]$document.createdUtc,
      "O",
      [Globalization.CultureInfo]::InvariantCulture,
      [Globalization.DateTimeStyles]::RoundtripKind,
      [ref]$created) -or
      $created.Kind -ne [DateTimeKind]::Utc -or
      $created -gt [DateTime]::UtcNow.AddMinutes(5) -or
      $created -lt [DateTime]::UtcNow.AddHours(-2)) {
    throw "installTransactionStale"
  }
  $transactionBytes = [byte[]]$null
  try { $transactionBytes = [Convert]::FromBase64String(
      [string]$document.transactionId) } catch {
    throw "installTransactionInvalid"
  }
  $expectedLauncher = [IO.Path]::GetFullPath(
    (Join-Path $installRoot "Ligase Host.exe"))
  $expectedEntries = @(Get-ExpectedShortcutEntries)
  $actualEntries = @($document.shortcuts)
  if ($document.schemaVersion -ne 1 -or
      $transactionBytes.Count -ne 32 -or
      [string]$document.manifestSourceHead -cne (Get-EvidenceSourceHead) -or
      [string]$document.manifestSha256 -cne
        (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestPath).Hash -or
      [string]$document.installLayout -cne "structured-v1" -or
      [string]$document.installDirectory -cne $installRoot -or
      [string]$document.launcher -cne $expectedLauncher -or
      $document.desktopSelected -isnot [bool] -or
      [bool]$document.desktopSelected -ne [bool]$DesktopShortcutSelected -or
      $document.virtualDisplaySelected -isnot [bool] -or
      [bool]$document.virtualDisplaySelected -ne [bool]$VirtualDisplaySelected -or
      $document.configureFirewall -isnot [bool] -or
      [bool]$document.configureFirewall -ne [bool]$ConfigureFirewall -or
      $document.firewallApplied -isnot [bool] -or
      $document.firewallWasConfigured -isnot [bool] -or
      $actualEntries.Count -ne 4) {
    throw "installTransactionInvalid"
  }
  $validated = @()
  $totalBytes = 0
  for ($index = 0; $index -lt 4; $index++) {
    $entry = $actualEntries[$index]
    $expected = $expectedEntries[$index]
    Assert-ClosedProperties $entry @("field", "path", "existed", "bytes")
    if ([string]$entry.field -cne [string]$expected.field -or
        [string]$entry.path -cne [IO.Path]::GetFullPath([string]$expected.path) -or
        $entry.existed -isnot [bool]) {
      throw "installTransactionInvalid"
    }
    $bytes = $null
    if ([bool]$entry.existed) {
      if ($entry.bytes -isnot [string]) { throw "installTransactionInvalid" }
      try { $bytes = [Convert]::FromBase64String([string]$entry.bytes) } catch {
        throw "installTransactionInvalid"
      }
      if ($bytes.Count -gt 1048576) { throw "installTransactionInvalid" }
      $totalBytes += $bytes.Count
    } elseif ($null -ne $entry.bytes) {
      throw "installTransactionInvalid"
    }
    $validated += [ordered]@{
      field = [string]$expected.field
      path = [IO.Path]::GetFullPath([string]$expected.path)
      existed = [bool]$entry.existed
      bytes = if ($null -eq $bytes) {
        $null
      } else {
        [Convert]::ToBase64String($bytes)
      }
    }
  }
  if ($totalBytes -gt 4194304) { throw "installTransactionInvalid" }
  $script:shortcutRollback = [ordered]@{
    launcher = $expectedLauncher
    snapshot = $validated
  }
  $script:firewallAppliedByTransaction = [bool]$document.firewallApplied
  $script:firewallWasConfigured = [bool]$document.firewallWasConfigured
  $script:transactionCreated = $true
  $script:transactionCleanupResult = "pending"
  $script:finalComponents.installTransaction = "verified"
  return $true
}

function Remove-InstallTransaction {
  if (-not $script:transactionCreated) {
    $script:transactionCleanupResult = "notCreated"
    return
  }
  $result = Invoke-InstallTransactionHelper "delete"
  if ($result -cne
      '{"code":"installTransactionDeleted","success":true}') {
    throw "installTransactionInvalid"
  }
  $script:transactionCreated = $false
  $script:transactionCleanupResult = "completed"
}

function Write-InstallerEvidence {
  $evidencePath = Get-InstallerEvidencePath
  $evidenceDirectory = Split-Path -Parent $evidencePath
  if (-not (Test-Path -LiteralPath $evidenceDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
  }
  Set-SecureDataRootAcl $evidenceDirectory
  $document = [ordered]@{
    schemaVersion = 1
    candidateSourceHead = Get-EvidenceSourceHead
    phase = $EvidencePhase
    success = if ($EvidenceSuccess -eq "unknown") {
      $null
    } else {
      $EvidenceSuccess -eq "true"
    }
    resultCode = $EvidenceResultCode
    installDirectory = Get-SafePathProjection $installRoot
    dataRoot = [ordered]@{
      action = $EvidenceDataRootAction
      source = Get-SafePathProjection $EvidenceDataRootSource
      target = Get-SafePathProjection $DataRoot
    }
    helper = [ordered]@{
      exitCode = $EvidenceHelperExit
      resultCode = $EvidenceResultCode
    }
    transactionHelper = [ordered]@{
      nativeExitCode = if ($script:transactionHelperNativeExit -ge 0) {
        $script:transactionHelperNativeExit
      } else { $EvidenceTransactionHelperNativeExit }
      stage = if ($script:transactionHelperStage -ne "none") {
        $script:transactionHelperStage
      } else { $EvidenceTransactionHelperStage }
      nativeCategory = if (
        $script:transactionHelperNativeCategory -ne "none") {
        $script:transactionHelperNativeCategory
      } else { $EvidenceTransactionHelperNativeCategory }
      nativeCode = if ($script:transactionHelperNativeCode -ne 0) {
        $script:transactionHelperNativeCode
      } else { $EvidenceTransactionHelperNativeCode }
      bindingReason = if ($script:transactionBindingReason -ne "none") {
        $script:transactionBindingReason
      } else { $EvidenceTransactionBindingReason }
      bindingRootKind = if ($script:transactionBindingRootKind -ne "none") {
        $script:transactionBindingRootKind
      } else { $EvidenceTransactionBindingRootKind }
      bindingSegmentCount = if (
        $script:transactionBindingSegmentCount -ne 0) {
        $script:transactionBindingSegmentCount
      } else { $EvidenceTransactionBindingSegmentCount }
      bindingPrefixMatched = if ($script:transactionBindingPrefixMatched) {
        $true
      } else { [bool]$EvidenceTransactionBindingPrefixMatched }
      bindingVolumeMatched = if ($script:transactionBindingVolumeMatched) {
        $true
      } else { [bool]$EvidenceTransactionBindingVolumeMatched }
      bindingFileIdentityMatched = if (
        $script:transactionBindingFileIdentityMatched) {
        $true
      } else { [bool]$EvidenceTransactionBindingFileIdentityMatched }
      aclMutationOccurred = if ($script:transactionAclMutationOccurred) {
        $true
      } else { [bool]$EvidenceTransactionAclMutationOccurred }
      aclRollback = if ($script:transactionAclRollback -ne "notRequired") {
        $script:transactionAclRollback
      } else { $EvidenceTransactionAclRollback }
      recoveryAction = if ($script:transactionRecoveryAction -ne "none") {
        $script:transactionRecoveryAction
      } else { $EvidenceTransactionRecoveryAction }
    }
    failedField = if ($EvidenceFailedField -eq "none") {
      $null
    } else {
      $EvidenceFailedField
    }
    components = $script:finalComponents
    rollback = [ordered]@{
      state = $EvidenceRollback
      shortcut = $script:shortcutRollbackResult
      firewall = $script:firewallRollbackResult
      transactionCleanup = $script:transactionCleanupResult
    }
    firewall = [ordered]@{ state = $EvidenceFirewall }
    residuals = [ordered]@{
      installDirectory = $EvidenceInstallResidue
      dataRoot = $EvidenceDataRootResidue
    }
    timestampUtc = [DateTime]::UtcNow.ToString(
      "yyyy-MM-ddTHH:mm:ss.fffZ",
      [Globalization.CultureInfo]::InvariantCulture)
  }
  $temporary = "$evidencePath.tmp"
  try {
    [IO.File]::WriteAllText(
      $temporary,
      ($document | ConvertTo-Json -Depth 8 -Compress),
      [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $evidencePath -Force
  } finally {
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
  }
  return $document
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

function Assert-CanonicalLocalDataRoot([string]$Candidate) {
  if ([string]::IsNullOrWhiteSpace($Candidate) -or
      $Candidate -notmatch '^[A-Za-z]:\\' -or
      $Candidate.StartsWith("\\", [StringComparison]::Ordinal) -or
      $Candidate.StartsWith("\\?\", [StringComparison]::Ordinal) -or
      $Candidate.StartsWith("\\.\", [StringComparison]::Ordinal)) {
    throw "dataRootMigrationPathInvalid"
  }
  $full = [IO.Path]::GetFullPath($Candidate)
  $root = [IO.Path]::GetPathRoot($full)
  if ($full.TrimEnd('\') -eq $root.TrimEnd('\') -or
      -not $Candidate.Equals($full, [StringComparison]::OrdinalIgnoreCase)) {
    throw "dataRootMigrationPathInvalid"
  }
  try {
    if ([IO.DriveInfo]::new($root).DriveType -ne [IO.DriveType]::Fixed) {
      throw "dataRootMigrationPathInvalid"
    }
  } catch {
    throw "dataRootMigrationPathInvalid"
  }
  return $full
}

function Get-ByteSha256([byte[]]$Bytes) {
  $algorithm = [Security.Cryptography.SHA256]::Create()
  try {
    return ([BitConverter]::ToString(
      $algorithm.ComputeHash($Bytes))).Replace("-", "").ToLowerInvariant()
  } finally {
    $algorithm.Dispose()
  }
}

function Get-InteractiveOperatorProfile {
  $identity = [LigaseInteractiveUser]::OpenIdentity()
  try {
    if ($null -eq $identity.User) { throw "interactiveOperatorUnavailable" }
    $sid = $identity.User.Value
    $profile = Get-ItemPropertyValue -LiteralPath (
      "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid") `
      -Name ProfileImagePath
    $profilePath = [Environment]::ExpandEnvironmentVariables([string]$profile)
    $profilePath = [IO.Path]::GetFullPath($profilePath)
    $roaming = Join-Path $profilePath "AppData\Roaming"
    $shellKey = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"
    $programsValue = Get-ItemPropertyValue -LiteralPath $shellKey -Name Programs
    $desktopValue = Get-ItemPropertyValue -LiteralPath $shellKey -Name Desktop
    $expandShellPath = {
      param([string]$Value)
      $expanded = $Value
      $expanded = $expanded.Replace("%USERPROFILE%", $profilePath)
      $expanded = $expanded.Replace("%AppData%", $roaming)
      $expanded = $expanded.Replace("%APPDATA%", $roaming)
      $expanded = $expanded.Replace(
        "%LOCALAPPDATA%", (Join-Path $profilePath "AppData\Local"))
      if ($expanded -match '%[^%]+%') {
        throw "shortcutLocationUnavailable"
      }
      return [IO.Path]::GetFullPath($expanded)
    }
    return [ordered]@{
      sid = $sid
      profilePath = $profilePath
      programsPath = & $expandShellPath ([string]$programsValue)
      desktopPath = & $expandShellPath ([string]$desktopValue)
      localAppData = Assert-CanonicalLocalDataRoot (
        Join-Path $profilePath "AppData\Local")
    }
  } finally {
    $identity.Dispose()
  }
}

function Set-SecureDataRootAcl([string]$Root) {
  $operator = [LigaseInteractiveUser]::OpenIdentity()
  try {
    if ($null -eq $operator.User) { throw "interactiveOperatorUnavailable" }
    $operatorSid = $operator.User
    $systemSid = [Security.Principal.SecurityIdentifier]::new(
      [Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
    $administratorsSid = [Security.Principal.SecurityIdentifier]::new(
      [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
    $inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
      [Security.AccessControl.InheritanceFlags]::ObjectInherit
    $propagation = [Security.AccessControl.PropagationFlags]::None
    $security = [Security.AccessControl.DirectorySecurity]::new()
    $security.SetAccessRuleProtection($true, $false)
    $security.SetOwner($administratorsSid)
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
      $systemSid, "FullControl", $inheritance, $propagation, "Allow"))
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
      $administratorsSid, "FullControl", $inheritance, $propagation, "Allow"))
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
      $operatorSid, "Modify", $inheritance, $propagation, "Allow"))
    Set-Acl -LiteralPath $Root -AclObject $security
    if ((Get-DataRootAccessState $Root) -cne "existing") {
      throw "dataRootAclMismatch"
    }
    $context = $operator.Impersonate()
    try {
      $probe = Join-Path $Root (".ligase-access-" + [guid]::NewGuid().ToString("N"))
      $moved = "$probe.moved"
      [IO.File]::WriteAllText($probe, "probe", [Text.UTF8Encoding]::new($false))
      Move-Item -LiteralPath $probe -Destination $moved
      Remove-Item -LiteralPath $moved -Force
    } finally {
      $context.Dispose()
    }
  } finally {
    $operator.Dispose()
  }
}

function Get-DataRootSnapshot(
  [string]$Root,
  [string]$ExcludedRelativePath = "",
  [switch]$RequireAuthorityDocuments
) {
  $rootPath = (Assert-CanonicalLocalDataRoot $Root).TrimEnd('\')
  $rootItem = Get-Item -LiteralPath $rootPath -Force
  if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "dataRootMigrationReparsePoint"
  }
  $entries = [Collections.Generic.List[object]]::new()
  $totalBytes = [int64]0
  foreach ($item in @(Get-ChildItem -LiteralPath $rootPath -Recurse -Force)) {
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      throw "dataRootMigrationReparsePoint"
    }
    $prefix = $rootPath + '\'
    if (-not $item.FullName.StartsWith(
        $prefix, [StringComparison]::OrdinalIgnoreCase)) {
      throw "dataRootMigrationEnumerationFailed"
    }
    $relative = $item.FullName.Substring($prefix.Length)
    if (-not [string]::IsNullOrWhiteSpace($ExcludedRelativePath) -and
        $relative -ceq $ExcludedRelativePath) {
      continue
    }
    if ($item.PSIsContainer) {
      $entries.Add([ordered]@{ path = $relative; kind = "directory"; size = 0; sha256 = "" })
    } else {
      if ([LigaseFileIdentity]::GetLinkCount($item.FullName) -ne 1) {
        throw "dataRootMigrationHardLink"
      }
      $totalBytes += [int64]$item.Length
      $entries.Add([ordered]@{
        path = $relative
        kind = "file"
        size = [int64]$item.Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $item.FullName).Hash
      })
    }
    if ($entries.Count -gt 100000 -or $totalBytes -gt 10737418240) {
      throw "dataRootMigrationTooLarge"
    }
  }
  foreach ($relative in @("ligase-authority.json", "library.json", "ligase-sync.json")) {
    $path = Join-Path $rootPath $relative
    if ($RequireAuthorityDocuments -and
        -not (Test-Path -LiteralPath $path -PathType Leaf)) {
      throw "orphanLegacyDataRootInvalid"
    }
    if (Test-Path -LiteralPath $path -PathType Leaf) {
      try {
        $raw = [IO.File]::ReadAllText($path, [Text.UTF8Encoding]::new($false, $true))
        $null = $raw | ConvertFrom-Json
      } catch {
        throw "dataRootMigrationJsonInvalid"
      }
    }
  }
  $canonical = @($entries | Sort-Object kind, path) |
    ConvertTo-Json -Depth 4 -Compress
  return [ordered]@{
    entries = $entries
    count = $entries.Count
    bytes = $totalBytes
    fingerprint = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes($canonical))
  }
}

function Get-OperatorDataRootSnapshot(
  [string]$Root,
  [switch]$RequireAuthorityDocuments
) {
  $operator = [LigaseInteractiveUser]::OpenIdentity()
  try {
    $context = $operator.Impersonate()
    try {
      return Get-DataRootSnapshot $Root `
        -RequireAuthorityDocuments:$RequireAuthorityDocuments
    } finally {
      $context.Dispose()
    }
  } catch {
    if ($_.Exception.Message -like "dataRootMigration*" -or
        $_.Exception.Message -like "orphanLegacy*") { throw }
    throw "dataRootMigrationNotEligible"
  } finally {
    $operator.Dispose()
  }
}

function Assert-DataRootSnapshotEqual($Expected, $Actual) {
  if ($Expected.count -ne $Actual.count -or
      $Expected.bytes -ne $Actual.bytes -or
      $Expected.fingerprint -cne $Actual.fingerprint) {
    throw "dataRootMigrationSourceChanged"
  }
}

function Assert-ProductsStopped {
  foreach ($name in @(
      "Ligase Host", "Ligase.Host.Desktop", "sunshine", "Ligase.GameWatcher")) {
    if (@(Get-Process -Name $name -ErrorAction SilentlyContinue).Count -gt 0) {
      throw "dataRootMigrationProcessRunning"
    }
  }
}

function Remove-OwnedMigrationDirectory(
  [string]$Directory,
  [string]$MarkerName,
  [string]$MarkerValue
) {
  if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { return }
  $marker = Join-Path $Directory $MarkerName
  if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or
      [IO.File]::ReadAllText($marker) -cne $MarkerValue) {
    throw "dataRootMigrationOwnershipMismatch"
  }
  Remove-Item -LiteralPath $Directory -Recurse -Force
}

function Restore-Migration {
  if ($null -eq $script:migrationRollback) {
    $script:rollbackResult = "notRequired"
    return $script:rollbackResult
  }
  $temporary = "$bootstrapPath.rollback"
  try {
    if ($script:migrationRollback.bootstrapWasAbsent) {
      if (Test-Path -LiteralPath $bootstrapPath -PathType Leaf) {
        $actual = Get-ByteSha256 ([IO.File]::ReadAllBytes($bootstrapPath))
        if ($actual -cne $script:migrationRollback.createdBootstrapHash) {
          throw "dataRootMigrationOwnershipMismatch"
        }
        Remove-Item -LiteralPath $bootstrapPath -Force
      }
    } else {
      [IO.File]::WriteAllBytes(
        $temporary, [byte[]]$script:migrationRollback.bootstrapBytes)
      Move-Item -LiteralPath $temporary -Destination $bootstrapPath -Force
    }
    if ($script:migrationRollback.targetCreated -and
        (Test-Path -LiteralPath $script:migrationRollback.target)) {
      Remove-OwnedMigrationDirectory `
        $script:migrationRollback.target `
        $script:migrationRollback.markerName `
        $script:migrationRollback.markerValue
    }
    foreach ($directory in @($script:migrationRollback.createdParents |
        Sort-Object { $_.Length } -Descending)) {
      if ((Test-Path -LiteralPath $directory -PathType Container) -and
          @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $directory -Force
      }
    }
    $script:rollbackResult = "completed"
    return $script:rollbackResult
  } catch {
    $script:rollbackResult = "failed"
    throw
  } finally {
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    $script:migrationRollback = $null
  }
}

function Invoke-DataRootMigration(
  $ExistingBootstrap,
  [string]$RequestedTarget,
  [string]$RecoverySource = ""
) {
  Assert-ProductsStopped
  $isRecovery = -not [string]::IsNullOrWhiteSpace($RecoverySource)
  if ($isRecovery -and $null -ne $ExistingBootstrap) {
    throw "orphanLegacyBootstrapExists"
  }
  $source = Assert-CanonicalLocalDataRoot $(if ($isRecovery) {
    $RecoverySource
  } else {
    [string]$ExistingBootstrap.dataRoot
  })
  $target = Assert-CanonicalLocalDataRoot $RequestedTarget
  $profile = Get-InteractiveOperatorProfile
  $legacyBase = Join-Path ([string]$profile.localAppData) "Ligase Host\Instances"
  $legacyPrefix = [IO.Path]::GetFullPath($legacyBase).TrimEnd('\') + '\'
  if (-not $source.StartsWith(
      $legacyPrefix, [StringComparison]::OrdinalIgnoreCase) -or
      (Get-DataRootAccessState $source) -cne "aclDrift") {
    throw "dataRootMigrationNotEligible"
  }
  $sourceAcl = Get-Acl -LiteralPath $source
  $operatorRules = @($sourceAcl.Access | Where-Object {
    $_.AccessControlType -eq "Allow" -and
    $_.IdentityReference.Translate(
      [Security.Principal.SecurityIdentifier]).Value -ceq [string]$profile.sid
  })
  if ($operatorRules.Count -eq 0) {
    throw "dataRootMigrationNotEligible"
  }
  $sourceAclSddl = $sourceAcl.GetSecurityDescriptorSddlForm("All")
  $sourceId = [guid]::Empty
  if (-not [guid]::TryParseExact(
      $source.Substring($legacyPrefix.Length), "D", [ref]$sourceId)) {
    throw "dataRootMigrationNotEligible"
  }
  $programDataBase = Join-Path (
    [Environment]::GetFolderPath("CommonApplicationData")) "Ligase Host\Instances"
  $targetPrefix = [IO.Path]::GetFullPath($programDataBase).TrimEnd('\') + '\'
  $targetId = [guid]::Empty
  if (-not $target.StartsWith(
      $targetPrefix, [StringComparison]::OrdinalIgnoreCase) -or
      -not [guid]::TryParseExact(
        $target.Substring($targetPrefix.Length), "D", [ref]$targetId) -or
      (Test-Path -LiteralPath $target)) {
    throw "dataRootMigrationTargetInvalid"
  }
  $bootstrapHash = if ($isRecovery) { "" } else {
    Get-ByteSha256 ([byte[]]$ExistingBootstrap.bytes)
  }
  $before = Get-OperatorDataRootSnapshot $source `
    -RequireAuthorityDocuments:$isRecovery
  $transactionId = [guid]::NewGuid().ToString("N")
  $markerName = ".ligase-migration-$transactionId"
  $markerValue = [guid]::NewGuid().ToString("N")
  $pending = "$target.pending-$transactionId"
  $targetCreated = $false
  $createdBootstrapHash = ""
  $createdParents = [Collections.Generic.List[string]]::new()
  try {
    $parent = Split-Path -Parent $target
    $missing = [Collections.Generic.Stack[string]]::new()
    while (-not (Test-Path -LiteralPath $parent)) {
      $missing.Push($parent)
      $parent = Split-Path -Parent $parent
    }
    foreach ($directory in $missing) {
      $null = New-Item -ItemType Directory -Path $directory
      $createdParents.Add($directory)
    }
    New-Item -ItemType Directory -Path $pending | Out-Null
    [IO.File]::WriteAllText(
      (Join-Path $pending $markerName),
      $markerValue,
      [Text.UTF8Encoding]::new($false))
    Set-SecureDataRootAcl $pending
    foreach ($entry in @($before.entries | Where-Object { $_.kind -eq "directory" } |
        Sort-Object { $_.path.Length })) {
      $null = New-Item -ItemType Directory -Path (Join-Path $pending $entry.path)
    }
    foreach ($entry in @($before.entries | Where-Object { $_.kind -eq "file" })) {
      $destination = Join-Path $pending $entry.path
      $parent = Split-Path -Parent $destination
      if (-not (Test-Path -LiteralPath $parent)) {
        $null = New-Item -ItemType Directory -Path $parent
      }
      Copy-Item -LiteralPath (Join-Path $source $entry.path) -Destination $destination
    }
    $markerPath = Join-Path $pending $markerName
    Remove-Item -LiteralPath $markerPath -Force
    try {
      Assert-DataRootSnapshotEqual $before (Get-DataRootSnapshot $pending)
    } finally {
      if (Test-Path -LiteralPath $pending -PathType Container) {
        [IO.File]::WriteAllText(
          $markerPath,
          $markerValue,
          [Text.UTF8Encoding]::new($false))
      }
    }
    Assert-DataRootSnapshotEqual $before (Get-OperatorDataRootSnapshot $source)
    if ((Get-Acl -LiteralPath $source).GetSecurityDescriptorSddlForm("All") -cne
        $sourceAclSddl) {
      throw "dataRootMigrationSourceChanged"
    }
    if ($isRecovery) {
      if (Test-Path -LiteralPath $bootstrapPath) {
        throw "orphanLegacyBootstrapExists"
      }
    } else {
      $currentBootstrapHash = Get-ByteSha256 (
        [IO.File]::ReadAllBytes($bootstrapPath))
      if ($currentBootstrapHash -cne $bootstrapHash) {
        throw "bootstrapChangedDuringMigration"
      }
    }
    Assert-ProductsStopped
    Move-Item -LiteralPath $pending -Destination $target
    $targetCreated = $true
    $temporary = "$bootstrapPath.migration"
    [IO.File]::WriteAllText(
      $temporary,
      (@{ schemaVersion = 1; dataRoot = $target } | ConvertTo-Json -Compress),
      [Text.UTF8Encoding]::new($false))
    if ($isRecovery) {
      if (Test-Path -LiteralPath $bootstrapPath) {
        throw "orphanLegacyBootstrapExists"
      }
      [IO.File]::Move($temporary, $bootstrapPath)
    } else {
      Move-Item -LiteralPath $temporary -Destination $bootstrapPath -Force
    }
    if ($isRecovery) {
      $createdBootstrapHash = Get-ByteSha256 (
        [IO.File]::ReadAllBytes($bootstrapPath))
    }
    $readback = Read-ValidBootstrap
    if ($readback.dataRoot -cne $target -or
        (Get-DataRootAccessState $target) -cne "existing") {
      throw "dataRootMigrationReadbackFailed"
    }
    Assert-DataRootSnapshotEqual $before (Get-OperatorDataRootSnapshot $source)
    if ((Get-Acl -LiteralPath $source).GetSecurityDescriptorSddlForm("All") -cne
        $sourceAclSddl) {
      throw "dataRootMigrationSourceChanged"
    }
    $script:migrationRollback = [ordered]@{
      bootstrapWasAbsent = $isRecovery
      bootstrapBytes = if ($isRecovery) { $null } else {
        [byte[]]$ExistingBootstrap.bytes
      }
      createdBootstrapHash = if ($isRecovery) {
        $createdBootstrapHash
      } else { "" }
      target = $target
      targetCreated = $true
      createdParents = @($createdParents)
      markerName = $markerName
      markerValue = $markerValue
      snapshot = $before
    }
    return $target
  } catch {
    $cleanupFailed = $false
    if (Test-Path -LiteralPath $pending -PathType Container) {
      try {
        Remove-OwnedMigrationDirectory $pending $markerName $markerValue
      } catch {
        $cleanupFailed = $true
      }
    }
    if ($targetCreated -and (Test-Path -LiteralPath $target)) {
      try {
        Remove-OwnedMigrationDirectory $target $markerName $markerValue
      } catch {
        $cleanupFailed = $true
      }
    }
    foreach ($directory in @($createdParents | Sort-Object { $_.Length } -Descending)) {
      if ((Test-Path -LiteralPath $directory -PathType Container) -and
          @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $directory -Force -ErrorAction SilentlyContinue
      }
    }
    $temporary = "$bootstrapPath.rollback"
    if ($isRecovery) {
      if (-not [string]::IsNullOrWhiteSpace($createdBootstrapHash) -and
          (Test-Path -LiteralPath $bootstrapPath -PathType Leaf) -and
          (Get-ByteSha256 ([IO.File]::ReadAllBytes($bootstrapPath))) -ceq
            $createdBootstrapHash) {
        Remove-Item -LiteralPath $bootstrapPath -Force
      } elseif (Test-Path -LiteralPath $bootstrapPath) {
        $cleanupFailed = $true
      }
    } else {
      [IO.File]::WriteAllBytes($temporary, [byte[]]$ExistingBootstrap.bytes)
      Move-Item -LiteralPath $temporary -Destination $bootstrapPath -Force
    }
    if ($cleanupFailed) {
      throw "dataRootMigrationOwnershipMismatch"
    }
    throw
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

function Get-ShortcutDetails([string]$ShortcutPath) {
  if (-not (Test-Path -LiteralPath $ShortcutPath -PathType Leaf)) {
    return $null
  }
  $shell = New-Object -ComObject WScript.Shell
  try {
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    return [ordered]@{
      target = [string]$shortcut.TargetPath
      arguments = [string]$shortcut.Arguments
      workingDirectory = [string]$shortcut.WorkingDirectory
    }
  } finally {
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
  }
}

function Test-OwnedShortcut(
  [string]$ShortcutPath,
  [string]$ExpectedTarget
) {
  $details = Get-ShortcutDetails $ShortcutPath
  if ($null -eq $details) { return $false }
  try {
    $target = [IO.Path]::GetFullPath([string]$details.target)
    $workingDirectory = [IO.Path]::GetFullPath(
      [string]$details.workingDirectory)
  } catch {
    return $false
  }
  return (
    $target.Equals(
      [IO.Path]::GetFullPath($ExpectedTarget),
      [StringComparison]::OrdinalIgnoreCase) -and
    [string]::IsNullOrEmpty([string]$details.arguments) -and
    $workingDirectory.Equals(
      $installRoot,
      [StringComparison]::OrdinalIgnoreCase))
}

function New-OwnedShortcut(
  [string]$ShortcutPath,
  [string]$ExpectedTarget
) {
  $parent = Split-Path -Parent $ShortcutPath
  if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
  }
  $shell = New-Object -ComObject WScript.Shell
  try {
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $ExpectedTarget
    $shortcut.Arguments = ""
    $shortcut.WorkingDirectory = $installRoot
    $shortcut.IconLocation = "$ExpectedTarget,0"
    $shortcut.Save()
  } finally {
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
  }
  if (-not (Test-OwnedShortcut $ShortcutPath $ExpectedTarget)) {
    throw "shortcutWriteFailed"
  }
}

function Remove-ShortcutIfOwned(
  [string]$ShortcutPath,
  [string]$ExpectedTarget
) {
  if (Test-OwnedShortcut $ShortcutPath $ExpectedTarget) {
    Remove-Item -LiteralPath $ShortcutPath -Force
    return $true
  }
  return $false
}

function Fail-ShortcutIntegration([string]$Field, [string]$Code) {
  $script:finalFailedField = $Field
  $script:finalComponents[$Field] = "failed"
  throw $Code
}

function Get-ShortcutSnapshot([array]$Entries) {
  $snapshot = @()
  foreach ($entry in $Entries) {
    $path = [string]$entry.path
    $exists = Test-Path -LiteralPath $path -PathType Leaf
    $bytes = if ($exists) {
      $item = Get-Item -LiteralPath $path -Force
      if ($item.Length -gt 1048576) { throw "shortcutSnapshotTooLarge" }
      [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))
    } else { $null }
    $snapshot += [ordered]@{
      field = [string]$entry.field
      path = $path
      existed = $exists
      bytes = $bytes
    }
  }
  return @($snapshot)
}

function Assert-ShortcutSnapshot([array]$Snapshot) {
  foreach ($entry in $Snapshot) {
    $exists = Test-Path -LiteralPath ([string]$entry.path) -PathType Leaf
    if ($exists -ne [bool]$entry.existed) { throw "shortcutRollbackFailed" }
    if ($exists -and
        [Convert]::ToBase64String(
          [IO.File]::ReadAllBytes([string]$entry.path)) -cne
          [string]$entry.bytes) {
      throw "shortcutRollbackFailed"
    }
  }
}

function Restore-ShortcutTransaction {
  if ($null -eq $script:shortcutRollback) { return }
  try {
    foreach ($entry in $script:shortcutRollback.snapshot) {
      $path = [string]$entry.path
      if ([bool]$entry.existed) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
          $current = [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))
          if ($current -ceq [string]$entry.bytes) { continue }
          if (-not (Test-OwnedShortcut $path $script:shortcutRollback.launcher)) {
            throw "shortcutRollbackConflict"
          }
        }
        $parent = Split-Path -Parent $path
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
          New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        $temporary = "$path.ligase-rollback"
        [IO.File]::WriteAllBytes(
          $temporary, [Convert]::FromBase64String([string]$entry.bytes))
        Move-Item -LiteralPath $temporary -Destination $path -Force
      } elseif (Test-Path -LiteralPath $path -PathType Leaf) {
        if (-not (Test-OwnedShortcut $path $script:shortcutRollback.launcher)) {
          throw "shortcutRollbackConflict"
        }
        Remove-Item -LiteralPath $path -Force
      }
    }
    Assert-ShortcutSnapshot $script:shortcutRollback.snapshot
    $script:shortcutRollback = $null
    $script:shortcutRollbackResult = "completed"
    if ($script:transactionCreated) {
      try {
        Remove-InstallTransaction
      } catch {
        $script:transactionCleanupResult = "failed"
        throw
      }
    } else {
      $script:transactionCleanupResult = "notCreated"
    }
    if ($script:rollbackResult -ne "failed") {
      $script:rollbackResult = "completed"
    }
  } catch {
    if ($script:shortcutRollbackResult -ne "completed") {
      $script:shortcutRollbackResult = "failed"
    }
    $script:rollbackResult = "failed"
    throw "shortcutRollbackFailed"
  }
}

function Test-ShortcutParentWritable([string]$ShortcutPath) {
  $candidate = Split-Path -Parent $ShortcutPath
  while (-not [string]::IsNullOrWhiteSpace($candidate) -and
         -not (Test-Path -LiteralPath $candidate -PathType Container)) {
    $next = Split-Path -Parent $candidate
    if ($next -eq $candidate) { break }
    $candidate = $next
  }
  if ([string]::IsNullOrWhiteSpace($candidate) -or
      -not (Test-Path -LiteralPath $candidate -PathType Container)) {
    return $false
  }
  try {
    $acl = Get-Acl -LiteralPath $candidate
    return $null -ne $acl
  } catch {
    return $false
  }
}

function Get-ShortcutRoots {
  $overrides = @(
    $ShortcutCommonProgramsRoot,
    $ShortcutCommonDesktopRoot,
    $ShortcutLegacyProgramsRoot,
    $ShortcutLegacyDesktopRoot)
  $overrideCount = @($overrides | Where-Object {
    -not [string]::IsNullOrWhiteSpace($_)
  }).Count
  if ($overrideCount -ne 0) {
    if ($overrideCount -ne 4 -or
        $env:LIGASE_INSTALL_VALIDATION_HARNESS -cne "1") {
      throw "shortcutTestOverrideRejected"
    }
    return [ordered]@{
      commonPrograms = [IO.Path]::GetFullPath($ShortcutCommonProgramsRoot)
      commonDesktop = [IO.Path]::GetFullPath($ShortcutCommonDesktopRoot)
      legacyPrograms = [IO.Path]::GetFullPath($ShortcutLegacyProgramsRoot)
      legacyDesktop = [IO.Path]::GetFullPath($ShortcutLegacyDesktopRoot)
    }
  }
  $operator = Get-InteractiveOperatorProfile
  return [ordered]@{
    commonPrograms = [Environment]::GetFolderPath(
      [Environment+SpecialFolder]::CommonPrograms)
    commonDesktop = [Environment]::GetFolderPath(
      [Environment+SpecialFolder]::CommonDesktopDirectory)
    legacyPrograms = [string]$operator.programsPath
    legacyDesktop = [string]$operator.desktopPath
  }
}

function Sync-OwnedShortcuts([bool]$DesktopSelected) {
  $launcher = Join-Path $installRoot "Ligase Host.exe"
  if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
    Fail-ShortcutIntegration "artifacts" "shortcutTargetUnavailable"
  }
  $roots = Get-ShortcutRoots
  $commonPrograms = [string]$roots.commonPrograms
  $commonDesktop = [string]$roots.commonDesktop
  if ([string]::IsNullOrWhiteSpace($commonPrograms) -or
      [string]::IsNullOrWhiteSpace($commonDesktop)) {
    Fail-ShortcutIntegration "startMenu" "shortcutLocationUnavailable"
  }
  $startMenu = Join-Path $commonPrograms "Ligase Host\Ligase Host.lnk"
  $desktop = Join-Path $commonDesktop "Ligase Host.lnk"

  $legacyStartMenu = Join-Path ([string]$roots.legacyPrograms) (
    "Ligase Host\Ligase Host.lnk")
  $legacyDesktop = Join-Path ([string]$roots.legacyDesktop) "Ligase Host.lnk"
  $entries = @(
    [ordered]@{ field = "startMenu"; path = $startMenu },
    [ordered]@{ field = "desktop"; path = $desktop },
    [ordered]@{ field = "startMenu"; path = $legacyStartMenu },
    [ordered]@{ field = "desktop"; path = $legacyDesktop })

  # Preflight all four locations before the first filesystem mutation.
  foreach ($entry in $entries) {
    if (-not (Test-ShortcutParentWritable ([string]$entry.path))) {
      Fail-ShortcutIntegration ([string]$entry.field) "shortcutLocationUnavailable"
    }
    $null = Get-ShortcutDetails ([string]$entry.path)
  }
  if ((Test-Path -LiteralPath $startMenu -PathType Leaf) -and
      -not (Test-OwnedShortcut $startMenu $launcher)) {
    Fail-ShortcutIntegration "startMenu" "startMenuShortcutConflict"
  }
  if ($DesktopSelected -and
      (Test-Path -LiteralPath $desktop -PathType Leaf) -and
      -not (Test-OwnedShortcut $desktop $launcher)) {
    Fail-ShortcutIntegration "desktop" "desktopShortcutConflict"
  }

    $script:shortcutRollback = [ordered]@{
    launcher = $launcher
    snapshot = Get-ShortcutSnapshot $entries
  }
  try {
    $null = Remove-ShortcutIfOwned $legacyStartMenu $launcher
    $null = Remove-ShortcutIfOwned $legacyDesktop $launcher
    if (-not (Test-Path -LiteralPath $startMenu -PathType Leaf)) {
      New-OwnedShortcut $startMenu $launcher
    }
    if ($DesktopSelected) {
      if (-not (Test-Path -LiteralPath $desktop -PathType Leaf)) {
        New-OwnedShortcut $desktop $launcher
      }
    } else {
      $null = Remove-ShortcutIfOwned $desktop $launcher
    }
    if ($env:LIGASE_INSTALL_VALIDATION_HARNESS -ceq "1" -and
        $env:LIGASE_SHORTCUT_FAILURE_STAGE -ceq "write") {
      Fail-ShortcutIntegration "desktop" "shortcutWriteFailed"
    }
    if (-not (Test-OwnedShortcut $startMenu $launcher)) {
      Fail-ShortcutIntegration "startMenu" "shortcutWriteFailed"
    }
    if ($env:LIGASE_INSTALL_VALIDATION_HARNESS -ceq "1" -and
        $env:LIGASE_SHORTCUT_FAILURE_STAGE -ceq "readback") {
      Fail-ShortcutIntegration "desktop" "shortcutWriteFailed"
    }
    if ($DesktopSelected -and -not (Test-OwnedShortcut $desktop $launcher)) {
      Fail-ShortcutIntegration "desktop" "shortcutWriteFailed"
    }
    if (-not $DesktopSelected -and
        (Test-OwnedShortcut $desktop $launcher)) {
      Fail-ShortcutIntegration "desktop" "shortcutWriteFailed"
    }
  } catch {
    $original = [string]$_.Exception.Message
    try { Restore-ShortcutTransaction } catch { throw }
    throw $original
  }
}

function Remove-OwnedShortcuts {
  $launcher = Join-Path $installRoot "Ligase Host.exe"
  $roots = Get-ShortcutRoots
  $paths = @(
    (Join-Path ([string]$roots.commonPrograms) "Ligase Host\Ligase Host.lnk"),
    (Join-Path ([string]$roots.commonDesktop) "Ligase Host.lnk"),
    (Join-Path ([string]$roots.legacyPrograms) "Ligase Host\Ligase Host.lnk"),
    (Join-Path ([string]$roots.legacyDesktop) "Ligase Host.lnk")
  )
  foreach ($path in $paths) {
    $null = Remove-ShortcutIfOwned $path $launcher
  }
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

function Fail-FinalInstallReadback([string]$Field) {
  if (-not $script:finalComponents.Contains($Field)) {
    $Field = "artifacts"
  }
  $script:finalFailedField = $Field
  $script:finalComponents[$Field] = "failed"
  throw "installationFinalReadbackFailed"
}

function Assert-FinalInstallReadback($Manifest) {
  try {
    $null = Test-Artifacts $Manifest
    $script:finalComponents.artifacts = "verified"
  } catch {
    Fail-FinalInstallReadback "artifacts"
  }
  try {
    $bootstrap = Read-ValidBootstrap
    $script:finalComponents.bootstrap = "verified"
  } catch {
    Fail-FinalInstallReadback "bootstrap"
  }
  if ($null -eq $bootstrap -or
      (-not [string]::IsNullOrWhiteSpace($DataRoot) -and
       -not ([string]$bootstrap.dataRoot).Equals(
         [IO.Path]::GetFullPath($DataRoot),
         [StringComparison]::OrdinalIgnoreCase)) -or
      (Get-DataRootAccessState ([string]$bootstrap.dataRoot)) -cne "existing") {
    Fail-FinalInstallReadback "dataRoot"
  }
  $script:finalComponents.dataRoot = "verified"
  $uninstallKey =
    "Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\Ligase Host"
  try {
    $registration = Get-ItemProperty -LiteralPath $uninstallKey
  } catch {
    Fail-FinalInstallReadback "arp"
  }
  $launcher = Join-Path $installRoot "Ligase Host.exe"
  if (
    -not ([string]$registration.InstallLocation).Equals(
      $installRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not ([string]$registration.DisplayIcon).Equals(
      $launcher, [StringComparison]::OrdinalIgnoreCase) -or
    -not ([string]$registration.UninstallString).Equals(
      ('"' + (Join-Path $installRoot "Uninstall.exe") + '"'),
      [StringComparison]::Ordinal)
  ) {
    Fail-FinalInstallReadback "arp"
  }
  $script:finalComponents.arp = "verified"
  $commonPrograms = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::CommonPrograms)
  if (-not (Test-OwnedShortcut (
      Join-Path $commonPrograms "Ligase Host\Ligase Host.lnk") $launcher)) {
    Fail-FinalInstallReadback "startMenu"
  }
  $script:finalComponents.startMenu = "verified"
  $desktop = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::CommonDesktopDirectory)
  $desktopShortcut = Join-Path $desktop "Ligase Host.lnk"
  if ($DesktopShortcutSelected) {
    if (-not (Test-OwnedShortcut $desktopShortcut $launcher)) {
      Fail-FinalInstallReadback "desktop"
    }
  } elseif ((Test-Path -LiteralPath $desktopShortcut) -and
      (Test-OwnedShortcut $desktopShortcut $launcher)) {
    Fail-FinalInstallReadback "desktop"
  }
  $script:finalComponents.desktop = "verified"
  $firewall = Get-FirewallReadback $Manifest
  if ($firewall.state -cne "configured" -or
      $firewall.machineCode -cne "configured") {
    Fail-FinalInstallReadback "firewall"
  }
  $script:finalComponents.firewall = "verified"
  if ($VirtualDisplaySelected -and $VirtualDisplayOutcome -cne "installed") {
    Fail-FinalInstallReadback "virtualDisplay"
  }
  if ($VirtualDisplaySelected) {
    $display = Get-VirtualDisplay
    if ($display.state -notin @("available", "rebootRequired")) {
      Fail-FinalInstallReadback "virtualDisplay"
    }
    $script:finalComponents.virtualDisplay = "verified"
  } else {
    $script:finalComponents.virtualDisplay = "notSelected"
  }
  return [ordered]@{
    dataRoot = [string]$bootstrap.dataRoot
    firewall = $firewall
  }
}

try {
  $held = $mutex.WaitOne([TimeSpan]::FromSeconds(2))
  if (-not $held) {
    Write-Outcome "installerBusy" $false
    exit 20
  }
  if ($Action -eq "RecordEvidence") {
    if ($EvidenceResultCode -notmatch '^[a-z][A-Za-z0-9]{0,63}$') {
      throw "installerEvidenceInvalid"
    }
    $written = Write-InstallerEvidence
    Write-Outcome "installerEvidenceRecorded" $true @{
      phase = [string]$written.phase
      resultCode = [string]$written.resultCode
    }
    exit 0
  }
  if ($Action -eq "ReconcileShortcuts") {
    if (-not (Test-Path -LiteralPath (
        Join-Path $installRoot "Ligase Host.exe") -PathType Leaf)) {
      throw "artifactReadbackFailed"
    }
    Sync-OwnedShortcuts ([bool]$DesktopShortcutSelected)
    if (-not [string]::IsNullOrWhiteSpace($InstallTransactionRoot)) {
      Save-InstallTransaction
    } else {
      $script:shortcutRollback = $null
    }
    Write-Outcome "shortcutsReconciled" $true @{
      desktopSelected = [bool]$DesktopShortcutSelected
    }
    exit 0
  }
  if ($Action -eq "ValidateInstallTransaction") {
    if (-not (Load-InstallTransaction)) {
      throw "installTransactionInvalid"
    }
    Write-Outcome "installTransactionValidated" $true @{
      shortcutCount = @($script:shortcutRollback.snapshot).Count
    }
    exit 0
  }
  if ($Action -eq "PreflightInstallTransaction") {
    $validationBehavior = if (
        $env:LIGASE_INSTALL_VALIDATION_HARNESS -ceq "1") {
      [string]$env:LIGASE_TRANSACTION_TEST_BEHAVIOR
    } else { "" }
    if ($validationBehavior -in @(
        "hangBeforeStdinRead", "delayedStdinRead", "oversizeInput")) {
      $payloadSize = if ($validationBehavior -eq "oversizeInput") {
        4 * 1024 * 1024 + 64 * 1024 + 1
      } else { 256 * 1024 }
      $payload = '{"payload":"' + ('a' * ($payloadSize - 14)) + '"}'
      $result = Invoke-InstallTransactionHelper "write" $payload
      if ($result -cne
          '{"code":"installTransactionWritten","success":true}') {
        throw "installTransactionInvalid"
      }
      if ($validationBehavior -eq "delayedStdinRead") {
        $script:transactionHelperStage = "finalReadback"
      } else {
        $result = Invoke-InstallTransactionHelper "delete"
        if ($result -cne
            '{"code":"installTransactionDeleted","success":true}') {
          throw "installTransactionInvalid"
        }
      }
    } else {
      Invoke-InstallTransactionPreflight
    }
    Write-Outcome "installTransactionPreflightReady" $true @{
      stage = [string]$script:transactionHelperStage
    }
    exit 0
  }
  $manifest = Read-Manifest
  $artifacts = Test-Artifacts $manifest
  $script:finalComponents.artifacts = "verified"
  $virtualDisplay = Get-VirtualDisplay
  $driverTrust = Get-DriverTrust $manifest
  $firewallReadback = Get-FirewallReadback $manifest
  $encoder = [ordered]@{
    state = "requiresRuntimeProbe"
    machineCode = "encoderProbePendingFirstLaunch"
    requiredForInstall = $false
    requiredForStreaming = $true
  }

  if ($Action -eq "FinalizeInstall") {
    $EvidencePhase = "finalReadback"
    $EvidenceResultCode = "installationFinalReadbackFailed"
    $EvidenceHelperExit = 0
    $EvidenceRollback = "notRequired"
    try {
      if (-not (Load-InstallTransaction)) {
        throw "installTransactionInvalid"
      }
      $final = Assert-FinalInstallReadback $manifest
      $EvidencePhase = "succeeded"
      $EvidenceSuccess = "true"
      $EvidenceResultCode = "installed"
      $EvidenceFirewall = "configured"
      $EvidenceInstallResidue = "nonEmpty"
      $EvidenceDataRootResidue = "nonEmpty"
      $script:shortcutRollback = $null
      Remove-InstallTransaction
      $null = Write-InstallerEvidence
      Write-Outcome "installationFinalized" $true @{
        dataRootState = "existing"
        firewallState = [string]$final.firewall.state
      }
      exit 0
    } catch {
      if ($script:firewallAppliedByTransaction -and
          -not $script:firewallWasConfigured) {
        try {
          $null = Invoke-FirewallAction -FirewallAction Remove -Manifest $manifest
          $script:firewallAppliedByTransaction = $false
          $script:firewallRollbackResult = "completed"
        } catch {
          $script:firewallRollbackResult = "failed"
          $script:rollbackResult = "failed"
        }
      }
      if ($null -ne $script:shortcutRollback) {
        try { Restore-ShortcutTransaction } catch {
          $script:rollbackResult = "failed"
        }
      }
      $EvidenceRollback = $script:rollbackResult
      $EvidencePhase = "failed"
      $EvidenceSuccess = "false"
      $EvidenceFailedField = $script:finalFailedField
      $EvidenceFirewall = if (
        $script:finalComponents.firewall -eq "verified") {
        "configured"
      } elseif ($script:finalComponents.firewall -eq "failed") {
        "failed"
      } else {
        $EvidenceFirewall
      }
      $EvidenceInstallResidue = if (
        Test-Path -LiteralPath $installRoot -PathType Container) {
        if (@(Get-ChildItem -LiteralPath $installRoot -Force).Count -eq 0) {
          "empty"
        } else { "nonEmpty" }
      } else { "absent" }
      $EvidenceDataRootResidue = if (
        -not [string]::IsNullOrWhiteSpace($DataRoot) -and
        (Test-Path -LiteralPath $DataRoot -PathType Container)) {
        if (@(Get-ChildItem -LiteralPath $DataRoot -Force).Count -eq 0) {
          "empty"
        } else { "nonEmpty" }
      } else { "absent" }
      $null = Write-InstallerEvidence
      Write-Outcome "installationFinalReadbackFailed" $false
      exit 10
    }
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
      dataRootAction = if ($MigrateDataRoot) {
        "migrateToStandardDataRoot"
      } elseif ($RecoverOrphanDataRoot) {
        "recoverOrphanLegacyDataRoot"
      } elseif ($null -ne $existingBootstrap) {
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
    if ($MigrateDataRoot -and $RecoverOrphanDataRoot) {
      throw "dataRootActionConflict"
    }
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
    if ($MigrateDataRoot -and $null -eq $existingBootstrap) {
      throw "dataRootMigrationNotEligible"
    }
    if ($RecoverOrphanDataRoot -and $null -ne $existingBootstrap) {
      throw "orphanLegacyBootstrapExists"
    }
    if (-not $MigrateDataRoot -and -not $RecoverOrphanDataRoot -and
        $null -ne $existingBootstrap -and
        (Get-DataRootAccessState ([string]$existingBootstrap.dataRoot)) -cne
          "existing") {
      throw "dataRootExistingUnsafe"
    }
    $dataRoot = if ($MigrateDataRoot) {
      Invoke-DataRootMigration $existingBootstrap $DataRoot
    } elseif ($RecoverOrphanDataRoot) {
      Invoke-DataRootMigration $null $DataRoot $RecoveryDataRootSource
    } elseif ($null -ne $existingBootstrap) {
      [string]$existingBootstrap.dataRoot
    } else {
      Write-NewBootstrap $DataRoot
    }
    $script:finalComponents.bootstrap = "verified"
    $script:finalComponents.dataRoot = "verified"
    Invoke-InstallTransactionPreflight
    $legacyEntriesRemoved = Remove-LegacyFlatOwnedEntries $manifest
    Sync-OwnedShortcuts ([bool]$DesktopShortcutSelected)
    $script:finalComponents.startMenu = "verified"
    $script:finalComponents.desktop = "verified"
    Save-InstallTransaction
    if ($ConfigureFirewall) {
      $firewallApplyCompleted = $false
      try {
        $firewallBefore = Get-FirewallReadback $manifest
        $script:firewallWasConfigured = (
          $firewallBefore.state -ceq "configured" -and
          $firewallBefore.machineCode -ceq "configured")
        $null = Invoke-FirewallAction -FirewallAction Apply -Manifest $manifest
        $firewallApplyCompleted = $true
        $script:firewallAppliedByTransaction = $true
        $firewallReadback = Get-FirewallReadback $manifest
        if ($firewallReadback.state -cne "configured" -or
            $firewallReadback.machineCode -cne "configured") {
          throw "firewallReadbackMismatch"
        }
        $script:finalComponents.firewall = "verified"
        Save-InstallTransaction
      } catch {
        if ($firewallApplyCompleted -and
            -not $script:firewallWasConfigured) {
          try {
            $null = Invoke-FirewallAction `
              -FirewallAction Remove `
              -Manifest $manifest
            $script:firewallAppliedByTransaction = $false
            $script:firewallRollbackResult = "completed"
          } catch {
            $script:firewallRollbackResult = "failed"
            # The install still fails closed. The original machine outcome is
            # preserved while readback will expose any owned-rule residue.
          }
        }
        if ($null -ne $script:migrationRollback) {
          Restore-Migration
        } elseif ($null -eq $bootstrapBytes) {
          Remove-Item -LiteralPath $bootstrapPath -Force -ErrorAction SilentlyContinue
          if ($null -ne $script:freshDataRootCreated -and
              (Test-Path -LiteralPath $script:freshDataRootCreated)) {
            Remove-Item -LiteralPath $script:freshDataRootCreated `
              -Recurse -Force -ErrorAction SilentlyContinue
          }
          $script:rollbackResult = if (
            (Test-Path -LiteralPath $bootstrapPath) -or
            ($null -ne $script:freshDataRootCreated -and
             (Test-Path -LiteralPath $script:freshDataRootCreated))) {
            "failed"
          } else {
            "completed"
          }
        }
        throw
      }
    }
    if ($MigrateDataRoot -or $RecoverOrphanDataRoot) {
      Assert-ProductsStopped
      $migrationReadback = Read-ValidBootstrap
      if ($migrationReadback.dataRoot -cne $dataRoot -or
          (Get-DataRootAccessState $dataRoot) -cne "existing") {
        Restore-Migration
        throw "dataRootMigrationReadbackFailed"
      }
      Assert-DataRootSnapshotEqual $script:migrationRollback.snapshot (
        Get-DataRootSnapshot $dataRoot $script:migrationRollback.markerName)
      $marker = Join-Path $dataRoot $script:migrationRollback.markerName
      if ([IO.File]::ReadAllText($marker) -cne
          $script:migrationRollback.markerValue) {
        Restore-Migration
        throw "dataRootMigrationOwnershipMismatch"
      }
      Remove-Item -LiteralPath $marker -Force
      $script:migrationRollback = $null
    } elseif ($null -ne $bootstrapBytes -and
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($bootstrapPath)) -cne
          [Convert]::ToBase64String($bootstrapBytes)) {
      throw "bootstrapChangedDuringUpgrade"
    }
    Write-Outcome "installed" $true ([ordered]@{
      installMode = "packaged"
      dataRootState = if ($MigrateDataRoot -or $null -ne $existingBootstrap) {
        "existing"
      } elseif ($RecoverOrphanDataRoot) {
        "existing"
      } else { "fresh" }
      dataRootAction = if ($MigrateDataRoot) {
        "migratedToStandardDataRoot"
      } elseif ($RecoverOrphanDataRoot) {
        "recoveredOrphanLegacyDataRoot"
      } elseif ($null -ne $existingBootstrap) {
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
    Remove-OwnedShortcuts
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
  $originalMessage = [string]$_.Exception.Message
  if ($Action -eq "Install") {
    if ($script:firewallAppliedByTransaction -and
        -not $script:firewallWasConfigured) {
      try {
        $null = Invoke-FirewallAction -FirewallAction Remove -Manifest $manifest
        $script:firewallAppliedByTransaction = $false
        $script:firewallRollbackResult = "completed"
      } catch {
        $script:firewallRollbackResult = "failed"
        $script:rollbackResult = "failed"
      }
    }
    if ($null -ne $script:shortcutRollback) {
      try { Restore-ShortcutTransaction } catch {
        $script:rollbackResult = "failed"
      }
    }
    if ($null -eq $script:migrationRollback -and
        $null -eq $bootstrapBytes -and
        $null -ne $script:freshDataRootCreated) {
      Remove-Item -LiteralPath $bootstrapPath -Force -ErrorAction SilentlyContinue
      Remove-Item -LiteralPath $script:freshDataRootCreated -Recurse -Force `
        -ErrorAction SilentlyContinue
      if ((Test-Path -LiteralPath $bootstrapPath) -or
          (Test-Path -LiteralPath $script:freshDataRootCreated)) {
        $script:rollbackResult = "failed"
      } elseif ($script:rollbackResult -ne "failed") {
        $script:rollbackResult = "completed"
      }
    }
  }
  if ($null -ne $script:migrationRollback) {
    try { $null = Restore-Migration } catch { $script:rollbackResult = "failed" }
  }
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
    "dataRootExistingUnsafe",
    "dataRootMigrationPathInvalid",
    "dataRootMigrationNotEligible",
    "dataRootMigrationTargetInvalid",
    "dataRootMigrationReparsePoint",
    "dataRootMigrationHardLink",
    "dataRootMigrationTooLarge",
    "dataRootMigrationEnumerationFailed",
    "dataRootMigrationJsonInvalid",
    "dataRootMigrationSourceChanged",
    "dataRootMigrationProcessRunning",
    "dataRootMigrationReadbackFailed",
    "dataRootMigrationOwnershipMismatch",
    "bootstrapChangedDuringMigration",
    "dataRootActionConflict",
    "orphanLegacyBootstrapExists",
    "orphanLegacyDataRootInvalid",
    "uninstallerSignatureInvalid",
    "firewallApplyFailed",
    "firewallReadbackMismatch",
    "firewallRemoveFailed",
    "virtualDisplayInstallFailed",
    "virtualDisplayUninstallFailed",
    "shortcutLocationUnavailable",
    "shortcutWriteFailed",
    "shortcutSnapshotTooLarge",
    "shortcutRollbackFailed",
    "shortcutTargetUnavailable",
    "installTransactionInvalid",
    "installTransactionAclInvalid",
    "installTransactionStale",
    "installTransactionUnavailable",
    "installTransactionTestOverrideRejected",
    "startMenuShortcutConflict",
    "desktopShortcutConflict",
    "shortcutTestOverrideRejected",
    "installationFinalReadbackFailed",
    "installerEvidenceInvalid",
    "installerEvidenceUnavailable")
  $code = if ($knownCodes -contains $originalMessage) {
    $originalMessage
  } else {
    "installationActionFailed"
  }
  if ($Action -eq "Install") {
    $EvidencePhase = "failed"
    $EvidenceSuccess = "false"
    $EvidenceResultCode = $code
    $EvidenceFailedField = if ($script:finalFailedField -ne "none") {
      $script:finalFailedField
    } elseif ($code -eq "startMenuShortcutConflict") {
      "startMenu"
    } elseif ($code -eq "desktopShortcutConflict") {
      "desktop"
    } elseif ($code -in @("firewallApplyFailed", "firewallReadbackMismatch")) {
      "firewall"
    } elseif ($code -like "installTransaction*") {
      "installTransaction"
    } elseif ($code -like "dataRoot*" -or $code -like "bootstrap*") {
      "dataRoot"
    } elseif ($code -like "artifact*" -or $code -like "helperSignature*") {
      "artifacts"
    } else {
      "none"
    }
    if ($EvidenceFailedField -ne "none") {
      $script:finalFailedField = $EvidenceFailedField
      $script:finalComponents[$EvidenceFailedField] = "failed"
    }
    $EvidenceHelperExit = 10
    $EvidenceRollback = $script:rollbackResult
    $EvidenceFirewall = if ($script:firewallAppliedByTransaction -or
        $script:firewallRollbackResult -ne "notRequired") {
      try {
        $state = Get-FirewallReadback $manifest
        if ($state.state -eq "configured") { "residual" } else { "failed" }
      } catch { "unknown" }
    } else { "notChecked" }
    $EvidenceDataRootAction = if ($MigrateDataRoot) {
      "migrateToStandard"
    } elseif ($RecoverOrphanDataRoot) {
      "recoverOrphanLegacyDataRoot"
    } elseif ($null -ne $existingBootstrap) {
      "preserveExisting"
    } else {
      "createFresh"
    }
    $EvidenceDataRootSource = if ($RecoverOrphanDataRoot) {
      $RecoveryDataRootSource
    } elseif ($null -ne $existingBootstrap) {
      [string]$existingBootstrap.dataRoot
    } else { "" }
    $EvidenceInstallResidue = if (
      Test-Path -LiteralPath $installRoot -PathType Container) {
      if (@(Get-ChildItem -LiteralPath $installRoot -Force).Count -eq 0) {
        "empty"
      } else { "nonEmpty" }
    } else { "absent" }
    $EvidenceDataRootResidue = if (
      -not [string]::IsNullOrWhiteSpace($DataRoot) -and
      (Test-Path -LiteralPath $DataRoot -PathType Container)) {
      if (@(Get-ChildItem -LiteralPath $DataRoot -Force).Count -eq 0) {
        "empty"
      } else { "nonEmpty" }
    } else { "absent" }
    try { $null = Write-InstallerEvidence } catch {
      # Wire output remains a stable machine code even if evidence persistence
      # itself fails. NSIS records the secondary failure before showing UI.
    }
  }
  if ($Action -eq "PreflightInstallTransaction") {
    Write-Outcome $code $false @{
      failedField = "installTransaction"
      transactionHelperNativeExit =
        [int]$script:transactionHelperNativeExit
      transactionHelperStage = [string]$script:transactionHelperStage
      transactionHelperNativeCategory =
        [string]$script:transactionHelperNativeCategory
      transactionHelperNativeCode =
        [int]$script:transactionHelperNativeCode
      transactionBindingReason =
        [string]$script:transactionBindingReason
      transactionBindingRootKind =
        [string]$script:transactionBindingRootKind
      transactionBindingSegmentCount =
        [int]$script:transactionBindingSegmentCount
      transactionBindingPrefixMatched =
        [bool]$script:transactionBindingPrefixMatched
      transactionBindingVolumeMatched =
        [bool]$script:transactionBindingVolumeMatched
      transactionBindingFileIdentityMatched =
        [bool]$script:transactionBindingFileIdentityMatched
    }
    exit 10
  }
  Write-Outcome $code $false
  exit 10
} finally {
  if ($held) { $mutex.ReleaseMutex() }
  $mutex.Dispose()
}
