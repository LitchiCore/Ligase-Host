[CmdletBinding()]
param(
  [ValidateSet(
    "DryRun",
    "Install",
    "FinalizeInstall",
    "ReconcileShortcuts",
    "ValidateInstallTransaction",
    "PreflightInstallTransaction",
    "QueryRunningProduct",
    "EvaluateLegacyForceEligibility",
    "CloseRunningProduct",
    "RecordEvidence",
    "Readback",
    "Uninstall",
    "ValidateVirtualDisplayResultContract",
    "InstallVirtualDisplay",
    "UninstallVirtualDisplay")]
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
  [ValidateSet(
    "initialized",
    "confirmed",
    "integrating",
    "finalReadback",
    "succeeded",
    "failed",
    "cancelled",
    "uninstalling",
    "uninstalled")]
  [string]$EvidencePhase = "initialized",
  [ValidateSet("unknown", "true", "false")]
  [string]$EvidenceSuccess = "unknown",
  [string]$EvidenceResultCode = "notStarted",
  [string]$VirtualDisplayResultPath,
  [ValidateSet(
    "none",
    "createFresh",
    "preserveExisting",
    "migrateToStandard",
    "recoverOrphanLegacyDataRoot")]
  [string]$EvidenceDataRootAction = "none",
  [string]$EvidenceDataRootSource,
  [ValidateSet("none", "Preserve", "Quarantine")]
  [string]$EvidenceUninstallDisposition = "none",
  [ValidateSet("notAttempted", "pending", "preserved", "quarantined")]
  [string]$EvidenceUninstallState = "notAttempted",
  [string]$ValidationRoot,
  [string]$ShutdownEvidenceRoot,
  [switch]$UserConfirmedClose,
  [ValidateSet("none", "simulateGraceful351", "simulateGraceful351Force351",
    "simulateGraceful351ForceTimeout", "simulateGraceful351ForcePermission",
    "simulateGraceful351ForceCompleted")]
  [string]$ShutdownValidationBehavior = "none",
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
    "canonicalRoot",
    "inspectAcl",
    "readSecurityDescriptor",
    "descriptorLength",
    "descriptorCopy",
    "descriptorParse",
    "buildSecurityDescriptor",
    "compareSecurityDescriptor",
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
    "managedFailure",
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
    "none",
    "exact",
    "notExact",
    "canonicalRootInspectionFailed",
    "securityDescriptorReadFailed",
    "descriptorLengthInvalid",
    "descriptorCopyFailed",
    "descriptorParseFailed",
    "expectedDescriptorBuildFailed",
    "descriptorCompareFailed")]
  [string]$EvidenceTransactionAclInspectionReason = "none",
  [ValidateSet(
    "none", "empty", "ownerNotAdministrators", "childEntryPresent",
    "namedDataStreamPresent", "streamMetadataInvalid")]
  [string]$EvidenceTransactionEmptyRootInspectionReason = "none",
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
$script:transactionReadbackStage = "none"
$script:transactionReadbackReason = "none"
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
$script:transactionAclInspectionReason = "none"
$script:transactionEmptyRootInspectionReason = "none"
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

$shutdownOnlyAction = $Action -in @(
  "QueryRunningProduct", "EvaluateLegacyForceEligibility", "CloseRunningProduct")

if (-not $shutdownOnlyAction) {
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
}

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Web.Script.Serialization;

public sealed class LigaseProductShutdownOutcome
{
    public string Code { get; set; }
    public bool Connected { get; set; }
    public bool RequestSent { get; set; }
    public bool AckReceived { get; set; }
    public bool DesktopTerminalReceived { get; set; }
    public string DesktopTerminalState { get; set; }
    public string DesktopTerminalCode { get; set; }
    public string DesktopCleanupState { get; set; }
    public string DesktopCoreStopCode { get; set; }
    public bool? DesktopCoreProcessStillAlive { get; set; }
}

public static class LigaseProductShutdownClient
{
    public static LigaseProductShutdownOutcome Request(string installRoot, int desktopProcessId,
        string requestId, int timeoutMilliseconds)
    {
        var result = new LigaseProductShutdownOutcome { Code = "pipeConnectFailed" };
        var deadline = Stopwatch.StartNew();
        var pipeName = GetPipeName(installRoot);
        try
        {
            using (var pipe = new NamedPipeClientStream(".", pipeName,
                       PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                pipe.Connect(Remaining(deadline, timeoutMilliseconds, 2000));
                result.Connected = true;
                uint serverPid;
                if (!GetNamedPipeServerProcessId(
                        pipe.SafePipeHandle.DangerousGetHandle(), out serverPid) ||
                    serverPid != unchecked((uint)desktopProcessId))
                { result.Code = "shutdownServerIdentityMismatch"; return result; }
                using (var reader = new StreamReader(pipe,
                           new UTF8Encoding(false, true), false, 1024, true))
                using (var writer = new StreamWriter(pipe,
                           new UTF8Encoding(false), 1024, true))
                {
                    writer.NewLine = "\n";
                    writer.AutoFlush = true;
                    writer.WriteLine("{\"schemaVersion\":1,\"command\":\"shutdown\",\"requestId\":\"" +
                        requestId + "\"}");
                    result.RequestSent = true;
                    var acceptedTask = reader.ReadLineAsync();
                    if (!acceptedTask.Wait(Remaining(deadline, timeoutMilliseconds, 2500)))
                    { result.Code = "shutdownAckTimeout"; return result; }
                    var accepted = "{\"schemaVersion\":1,\"requestId\":\"" + requestId +
                        "\",\"state\":\"accepted\"}";
                    if (!String.Equals(acceptedTask.Result, accepted, StringComparison.Ordinal))
                    { result.Code = "shutdownAckInvalid"; return result; }
                    result.AckReceived = true;
                    var terminalTask = reader.ReadLineAsync();
                    if (!terminalTask.Wait(Remaining(deadline, timeoutMilliseconds,
                            timeoutMilliseconds)))
                    { result.Code = "shutdownTerminalTimeout"; return result; }
                    if (!TryReadTerminal(terminalTask.Result, requestId, result))
                    { result.Code = "shutdownTerminalInvalid"; return result; }
                    result.DesktopTerminalReceived = true;
                    result.Code = result.DesktopTerminalState == "completed" &&
                        (result.DesktopTerminalCode == "exitCommitted" ||
                         result.DesktopTerminalCode == "exitAlreadyCommitted" ||
                         (result.DesktopTerminalCode == "coreStopped" &&
                          result.DesktopCoreProcessStillAlive == false))
                        ? "shutdownAcknowledged" : "shutdownTerminalFailed";
                    return result;
                }
            }
        }
        catch (TimeoutException)
        {
            result.Code = result.AckReceived ? "shutdownTerminalTimeout" :
                result.RequestSent ? "shutdownAckTimeout" : "pipeConnectFailed";
            return result;
        }
        catch
        {
            result.Code = result.RequestSent ? "shutdownTransportFailed" :
                "pipeConnectFailed";
            return result;
        }
    }

    private static bool TryReadTerminal(string json, string requestId,
        LigaseProductShutdownOutcome result)
    {
        if (String.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > 2048)
            return false;
        try
        {
            var value = new JavaScriptSerializer().DeserializeObject(json)
                as Dictionary<string, object>;
            if (value == null ||
                !value.ContainsKey("schemaVersion") ||
                !value.ContainsKey("requestId") || !value.ContainsKey("state") ||
                !value.ContainsKey("code") ||
                !value.ContainsKey("coreProcessStillAlive") ||
                !String.Equals(value["requestId"] as string, requestId,
                    StringComparison.Ordinal) ||
                !(value["coreProcessStillAlive"] is bool)) return false;
            var schema = Convert.ToInt32(value["schemaVersion"]);
            if (schema == 1 && value.Count != 5) return false;
            if (schema == 2 && (value.Count != 6 ||
                !value.ContainsKey("shutdownProtocolVersion") ||
                Convert.ToInt32(value["shutdownProtocolVersion"]) != 2)) return false;
            if (schema == 3 && (value.Count != 8 ||
                !value.ContainsKey("shutdownProtocolVersion") ||
                Convert.ToInt32(value["shutdownProtocolVersion"]) != 3 ||
                !value.ContainsKey("cleanupState") ||
                !value.ContainsKey("coreStopCode") ||
                !(value["cleanupState"] is string) ||
                !(value["coreStopCode"] is string))) return false;
            if (schema != 1 && schema != 2 && schema != 3) return false;
            result.DesktopTerminalState = value["state"] as string;
            result.DesktopTerminalCode = value["code"] as string;
            result.DesktopCoreProcessStillAlive =
                (bool)value["coreProcessStillAlive"];
            result.DesktopCleanupState = schema == 3
                ? value["cleanupState"] as string : null;
            result.DesktopCoreStopCode = schema == 3
                ? value["coreStopCode"] as string : null;
            return (result.DesktopTerminalState == "completed" ||
                    result.DesktopTerminalState == "failed") &&
                !String.IsNullOrEmpty(result.DesktopTerminalCode);
        }
        catch { return false; }
    }

    private static int Remaining(
        Stopwatch deadline, int totalMilliseconds, int stageMaximum)
    {
        var remaining = totalMilliseconds - (int)deadline.ElapsedMilliseconds;
        if (remaining <= 0) throw new TimeoutException("shutdownDeadlineExceeded");
        return Math.Min(remaining, stageMaximum);
    }

    public static string GetPipeName(string installRoot)
    {
        var normalized = Path.GetFullPath(installRoot)
            .TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        using (var sha = SHA256.Create())
        {
            var hash = BitConverter.ToString(sha.ComputeHash(
                Encoding.UTF8.GetBytes(normalized))).Replace("-", "")
                .ToLowerInvariant();
            return "ligase-host-shutdown-v1-" + hash;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(
        IntPtr pipe, out uint serverProcessId);
}
"@ -ReferencedAssemblies @("System.dll", "System.Core.dll", "System.Web.Extensions.dll")

# Restart Manager is the Windows authority for applications holding files that
# an installer needs to replace.  Ligase's named pipe only requests its custom
# graceful cleanup; it does not replace this standard occupancy readback.
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

public sealed class LigaseRestartManagerResult
{
    public LigaseRestartManagerProcessInfo[] Processes { get; set; }
    public uint RebootReason { get; set; }
}

public sealed class LigaseRestartManagerProcessInfo
{
    public int ProcessId { get; set; }
    public long ProcessStartFileTimeUtc { get; set; }
    public uint AppStatus { get; set; }
    public uint SessionId { get; set; }
}

public sealed class LigaseRestartManagerShutdownResult
{
    public int NativeCode { get; set; }
    public bool Cancelled { get; set; }
    public bool ForceUsed { get; set; }
}

public static class LigaseRestartManager
{
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_MORE_DATA = 234;
    private const int CCH_RM_SESSION_KEY = 32;
    private const int CCH_RM_MAX_APP_NAME = 255;
    private const int CCH_RM_MAX_SVC_NAME = 63;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int ProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
        public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
        public string ServiceShortName;
        public uint ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }

    public static LigaseRestartManagerResult GetLockingProcesses(string[] paths)
    {
        uint handle;
        var key = new System.Text.StringBuilder(CCH_RM_SESSION_KEY + 1);
        var code = RmStartSession(out handle, 0, key);
        if (code != ERROR_SUCCESS) throw new Win32Exception(code);
        try
        {
            code = RmRegisterResources(handle, (uint)paths.Length, paths,
                0, null, 0, null);
            if (code != ERROR_SUCCESS) throw new Win32Exception(code);
            uint needed = 0, count = 0, rebootReason = 0;
            code = RmGetList(handle, out needed, ref count, null, ref rebootReason);
            if (code == ERROR_SUCCESS)
                return new LigaseRestartManagerResult {
                    Processes = new LigaseRestartManagerProcessInfo[0],
                    RebootReason = rebootReason };
            if (code != ERROR_MORE_DATA) throw new Win32Exception(code);
            var values = new RM_PROCESS_INFO[needed];
            count = needed;
            code = RmGetList(handle, out needed, ref count, values, ref rebootReason);
            if (code != ERROR_SUCCESS) throw new Win32Exception(code);
            var processes = new List<LigaseRestartManagerProcessInfo>();
            for (var index = 0; index < count; index++)
            {
                long start = ((long)values[index].Process.ProcessStartTime.dwHighDateTime << 32) |
                    unchecked((uint)values[index].Process.ProcessStartTime.dwLowDateTime);
                processes.Add(new LigaseRestartManagerProcessInfo {
                    ProcessId = values[index].Process.ProcessId,
                    ProcessStartFileTimeUtc = start,
                    AppStatus = values[index].AppStatus,
                    SessionId = values[index].TSSessionId });
            }
            processes.Sort((left, right) => left.ProcessId.CompareTo(right.ProcessId));
            return new LigaseRestartManagerResult {
                Processes = processes.ToArray(), RebootReason = rebootReason };
        }
        finally { RmEndSession(handle); }
    }

    public static LigaseRestartManagerShutdownResult ShutdownLockingProcesses(
        string[] paths, int[] processIds, string[] processStartedUtc,
        int timeoutMilliseconds, bool force)
    {
        if (processIds == null || processStartedUtc == null ||
            processIds.Length == 0 || processIds.Length != processStartedUtc.Length ||
            timeoutMilliseconds <= 0)
            throw new ArgumentException("restartManagerShutdownInputInvalid");
        if (paths == null) paths = new string[0];
        var applications = new RM_UNIQUE_PROCESS[processIds.Length];
        for (var index = 0; index < processIds.Length; index++)
        {
            long start = DateTime.Parse(
                processStartedUtc[index],
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime().ToFileTimeUtc();
            applications[index].ProcessId = processIds[index];
            applications[index].ProcessStartTime.dwLowDateTime = unchecked((int)(start & 0xffffffff));
            applications[index].ProcessStartTime.dwHighDateTime = unchecked((int)(start >> 32));
        }
        uint handle;
        var key = new System.Text.StringBuilder(CCH_RM_SESSION_KEY + 1);
        var code = RmStartSession(out handle, 0, key);
        if (code != ERROR_SUCCESS) throw new Win32Exception(code);
        try
        {
            var registeredPaths = paths.Length == 0 ? null : paths;
            code = RmRegisterResources(handle, (uint)paths.Length, registeredPaths,
                (uint)applications.Length, applications, 0, null);
            if (code != ERROR_SUCCESS) throw new Win32Exception(code);
            var task = System.Threading.Tasks.Task.Run(() =>
                RmShutdown(handle, force ? 1u : 0u, IntPtr.Zero));
            if (!task.Wait(timeoutMilliseconds))
            {
                RmCancelCurrentTask(handle);
                task.Wait(1000);
                return new LigaseRestartManagerShutdownResult {
                    NativeCode = task.IsCompleted ? task.Result : 1460,
                    Cancelled = true,
                    ForceUsed = force };
            }
            return new LigaseRestartManagerShutdownResult {
                NativeCode = task.Result,
                Cancelled = false,
                ForceUsed = force };
        }
        finally { RmEndSession(handle); }
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(
        out uint sessionHandle, int sessionFlags,
        System.Text.StringBuilder sessionKey);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle, uint fileCount,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] files,
        uint applicationCount, RM_UNIQUE_PROCESS[] applications,
        uint serviceCount, string[] services);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint sessionHandle, out uint needed, ref uint count,
        [In, Out] RM_PROCESS_INFO[] affectedApps, ref uint rebootReason);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmShutdown(
        uint sessionHandle, uint actionFlags, IntPtr statusCallback);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmCancelCurrentTask(uint sessionHandle);
}
"@

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class LigaseFileIdentity
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string path, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetSystemDirectoryW(
        System.Text.StringBuilder buffer, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle handle, System.Text.StringBuilder path,
        uint length, uint flags);

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

    public static FileStream OpenStableRead(string path)
    {
        var handle = CreateFileW(
            Path.GetFullPath(path), GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero,
            OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var code = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException("legacyImageOpenFailed", new System.ComponentModel.Win32Exception(code));
        }
        BY_HANDLE_FILE_INFORMATION information;
        if (!GetFileInformationByHandle(handle, out information) ||
            (information.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0)
        {
            handle.Dispose();
            throw new IOException("legacyImageIdentityInvalid");
        }
        return new FileStream(handle, FileAccess.Read, 4096, false);
    }

    public static string GetIdentitySha256(SafeFileHandle handle)
    {
        if (handle == null || handle.IsInvalid || handle.IsClosed)
            throw new IOException("virtualDisplaySetupTransportInvalid");
        BY_HANDLE_FILE_INFORMATION information;
        if (!GetFileInformationByHandle(handle, out information))
            throw new IOException("virtualDisplaySetupTransportInvalid");
        string authority = information.VolumeSerialNumber.ToString(
            System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            information.FileIndexHigh.ToString(
                System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            information.FileIndexLow.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            return BitConverter.ToString(sha.ComputeHash(
                System.Text.Encoding.ASCII.GetBytes(authority)))
                .Replace("-", "").ToLowerInvariant();
        }
    }

    public static string GetTrustedWindowsPowerShellPath()
    {
        var buffer = new System.Text.StringBuilder(32768);
        uint length = GetSystemDirectoryW(buffer, (uint)buffer.Capacity);
        if (length == 0 || length >= buffer.Capacity)
            throw new IOException("virtualDisplayReadbackFailed");
        string systemDirectory = Path.GetFullPath(buffer.ToString());
        string shellDirectory = Path.GetFullPath(Path.Combine(
            systemDirectory, "WindowsPowerShell", "v1.0"));
        foreach (string directoryPath in new[] {
            systemDirectory,
            Path.GetFullPath(Path.Combine(systemDirectory, "WindowsPowerShell")),
            shellDirectory
        })
        {
            var directory = new DirectoryInfo(directoryPath);
            if (!directory.Exists ||
                (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("virtualDisplayReadbackFailed");
        }
        string candidate = Path.GetFullPath(
            Path.Combine(shellDirectory, "powershell.exe"));
        var file = new FileInfo(candidate);
        if (!file.Exists || (file.Attributes & FileAttributes.Directory) != 0 ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("virtualDisplayReadbackFailed");
        using (var stream = new FileStream(
            candidate, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete))
        {
            var final = new System.Text.StringBuilder(32768);
            uint finalLength = GetFinalPathNameByHandleW(
                stream.SafeFileHandle, final, (uint)final.Capacity, 0);
            if (finalLength == 0 || finalLength >= final.Capacity)
                throw new IOException("virtualDisplayReadbackFailed");
            string finalPath = final.ToString();
            if (finalPath.StartsWith(@"\\?\", StringComparison.Ordinal))
                finalPath = finalPath.Substring(4);
            if (!string.Equals(Path.GetFullPath(finalPath), candidate,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("virtualDisplayReadbackFailed");
            BY_HANDLE_FILE_INFORMATION information;
            if (!GetFileInformationByHandle(
                    stream.SafeFileHandle, out information) ||
                information.FileIndexHigh == 0 && information.FileIndexLow == 0)
                throw new IOException("virtualDisplayReadbackFailed");
        }
        return candidate;
    }
}
"@

function Get-VirtualDisplaySetupHelperPath($Manifest) {
  $relative = "Deployment/Ligase.VirtualDisplay.Setup.exe"
  if ([string]$Manifest.virtualDisplay.setupHelper -cne $relative) {
    throw "virtualDisplaySetupUnavailable"
  }
  $path = Join-Path $installRoot $relative
  $pins = @($Manifest.privilegedHelpers | Where-Object {
    [string]$_.relativePath -ceq $relative
  })
  if ($pins.Count -ne 1 -or
      [string]$pins[0].signedArtifactSha256 -notmatch '^[0-9a-f]{64}$' -or
      -not (Test-Path -LiteralPath $path -PathType Leaf) -or
      [bool]((Get-Item -LiteralPath $path -Force).Attributes -band
        [IO.FileAttributes]::ReparsePoint) -or
      (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -cne
        ([string]$pins[0].signedArtifactSha256).ToUpperInvariant()) {
    throw "virtualDisplaySetupUnavailable"
  }
  return [ordered]@{
    path = [IO.Path]::GetFullPath($path)
    sha256 = [string]$pins[0].signedArtifactSha256
  }
}

function New-VirtualDisplaySetupOperationDirectory {
  $root = if (Test-VirtualDisplaySetupRunnerValidationAllowed) {
    Join-Path $installRoot "runner-operations"
  } else {
    Join-Path ([Environment]::GetFolderPath(
      [Environment+SpecialFolder]::CommonApplicationData)) (
        "Ligase\Installer\VirtualDisplaySetup")
  }
  if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    $null = New-Item -ItemType Directory -Path $root -Force
  }
  $operation = Join-Path $root ([Guid]::NewGuid().ToString("N"))
  $null = New-Item -ItemType Directory -Path $operation
  foreach ($segment in @($root, $operation)) {
    if ([bool]((Get-Item -LiteralPath $segment -Force).Attributes -band
        [IO.FileAttributes]::ReparsePoint)) {
      throw "virtualDisplaySetupTransportInvalid"
    }
  }
  if (Test-VirtualDisplaySetupRunnerValidationAllowed) {
    return $operation
  }
  $systemSid = [Security.Principal.SecurityIdentifier]::new(
    [Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
  $adminsSid = [Security.Principal.SecurityIdentifier]::new(
    [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
  $operator = [LigaseInteractiveUser]::OpenIdentity()
  if ($null -eq $operator.User) {
    throw "virtualDisplaySetupTransportInvalid"
  }
  $callerSid = $operator.User
  $acl = [Security.AccessControl.DirectorySecurity]::new()
  $acl.SetAccessRuleProtection($true, $false)
  $acl.SetOwner($adminsSid)
  $flags = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
    [Security.AccessControl.InheritanceFlags]::ObjectInherit
  foreach ($sid in @($systemSid, $adminsSid, $callerSid)) {
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
      $sid, "FullControl", $flags,
      [Security.AccessControl.PropagationFlags]::None, "Allow"))
  }
  Set-Acl -LiteralPath $operation -AclObject $acl
  return $operation
}

function Get-JsonProperty($Value, [string]$Name) {
  if ($null -eq $Value) { return $null }
  return $Value.PSObject.Properties[$Name]
}

function Test-JsonSchemaValue($Value, $Schema, $RootSchema) {
  if ($Schema -is [bool]) { return [bool]$Schema }
  $reference = Get-JsonProperty $Schema '$ref'
  if ($null -ne $reference) {
    $text = [string]$reference.Value
    if (-not $text.StartsWith('#/$defs/')) { return $false }
    $name = $text.Substring(8).Replace('~1', '/').Replace('~0', '~')
    $definition = Get-JsonProperty $RootSchema.'$defs' $name
    if ($null -eq $definition) { return $false }
    if (-not (Test-JsonSchemaValue $Value $definition.Value $RootSchema)) {
      return $false
    }
  }
  $allOfProperty = Get-JsonProperty $Schema 'allOf'
  foreach ($entry in $(if ($null -eq $allOfProperty) { @() } else {
      @($allOfProperty.Value) })) {
    if (-not (Test-JsonSchemaValue $Value $entry $RootSchema)) { return $false }
  }
  $anyOfProperty = Get-JsonProperty $Schema 'anyOf'
  $anyOf = if ($null -eq $anyOfProperty) { @() } else { @($anyOfProperty.Value) }
  if ($anyOf.Count -gt 0) {
    $matched = @($anyOf | Where-Object {
      Test-JsonSchemaValue $Value $_ $RootSchema }).Count
    if ($matched -lt 1) { return $false }
  }
  $oneOfProperty = Get-JsonProperty $Schema 'oneOf'
  $oneOf = if ($null -eq $oneOfProperty) { @() } else { @($oneOfProperty.Value) }
  if ($oneOf.Count -gt 0) {
    $matched = @($oneOf | Where-Object {
      Test-JsonSchemaValue $Value $_ $RootSchema }).Count
    if ($matched -ne 1) { return $false }
  }
  $not = Get-JsonProperty $Schema 'not'
  if ($null -ne $not -and
      (Test-JsonSchemaValue $Value $not.Value $RootSchema)) { return $false }
  $condition = Get-JsonProperty $Schema 'if'
  if ($null -ne $condition) {
    $selected = if (Test-JsonSchemaValue $Value $condition.Value $RootSchema) {
      Get-JsonProperty $Schema 'then'
    } else { Get-JsonProperty $Schema 'else' }
    if ($null -ne $selected -and
        -not (Test-JsonSchemaValue $Value $selected.Value $RootSchema)) {
      return $false
    }
  }
  $type = Get-JsonProperty $Schema 'type'
  if ($null -ne $type) {
    $types = @($type.Value)
    $validType = $false
    foreach ($candidate in $types) {
      $candidateValid = switch ([string]$candidate) {
        'null' { $null -eq $Value }
        'boolean' { $Value -is [bool] }
        'string' { $Value -is [string] }
        'integer' { $Value -is [sbyte] -or $Value -is [byte] -or
          $Value -is [int16] -or $Value -is [uint16] -or
          $Value -is [int32] -or $Value -is [uint32] -or
          $Value -is [int64] -or $Value -is [uint64] }
        'number' { $Value -is [ValueType] -and $Value -isnot [bool] }
        'array' { $Value -is [array] }
        'object' { $null -ne $Value -and $Value -isnot [array] -and
          $Value -isnot [string] -and $Value -isnot [ValueType] }
        default { $false }
      }
      $validType = $validType -or [bool]$candidateValid
    }
    if (-not $validType) { return $false }
  }
  $constant = Get-JsonProperty $Schema 'const'
  if ($null -ne $constant -and
      ($Value | ConvertTo-Json -Depth 100 -Compress) -cne
      ($constant.Value | ConvertTo-Json -Depth 100 -Compress)) { return $false }
  $enumProperty = Get-JsonProperty $Schema 'enum'
  $enumeration = if ($null -eq $enumProperty) { @() } else { @($enumProperty.Value) }
  if ($enumeration.Count -gt 0) {
    $encoded = $Value | ConvertTo-Json -Depth 100 -Compress
    if (@($enumeration | Where-Object {
        ($_ | ConvertTo-Json -Depth 100 -Compress) -ceq $encoded }).Count -eq 0) {
      return $false
    }
  }
  if ($Value -is [string]) {
    $pattern = Get-JsonProperty $Schema 'pattern'
    if ($null -ne $pattern -and $Value -notmatch [string]$pattern.Value) {
      return $false
    }
  }
  if ($Value -is [ValueType] -and $Value -isnot [bool]) {
    $minimum = Get-JsonProperty $Schema 'minimum'
    $maximum = Get-JsonProperty $Schema 'maximum'
    if ($null -ne $minimum -and [decimal]$Value -lt [decimal]$minimum.Value) {
      return $false
    }
    if ($null -ne $maximum -and [decimal]$Value -gt [decimal]$maximum.Value) {
      return $false
    }
  }
  if ($Value -is [array]) {
    $minimum = Get-JsonProperty $Schema 'minItems'
    $maximum = Get-JsonProperty $Schema 'maxItems'
    if ($null -ne $minimum -and $Value.Count -lt [int]$minimum.Value) {
      return $false
    }
    if ($null -ne $maximum -and $Value.Count -gt [int]$maximum.Value) {
      return $false
    }
    $unique = Get-JsonProperty $Schema 'uniqueItems'
    if ($null -ne $unique -and [bool]$unique.Value) {
      $encoded = @($Value | ForEach-Object {
        $_ | ConvertTo-Json -Depth 100 -Compress })
      if (@($encoded | Select-Object -Unique).Count -ne $encoded.Count) {
        return $false
      }
    }
    $prefix = Get-JsonProperty $Schema 'prefixItems'
    $prefixCount = 0
    if ($null -ne $prefix) {
      $prefixValues = @($prefix.Value); $prefixCount = $prefixValues.Count
      if ($Value.Count -lt $prefixCount) { return $false }
      for ($index = 0; $index -lt $prefixCount; $index++) {
        if (-not (Test-JsonSchemaValue $Value[$index] $prefixValues[$index] $RootSchema)) {
          return $false
        }
      }
    }
    $items = Get-JsonProperty $Schema 'items'
    if ($null -ne $items) {
      if ($items.Value -is [bool] -and -not [bool]$items.Value -and
          $Value.Count -gt $prefixCount) { return $false }
      for ($index = $prefixCount; $index -lt $Value.Count; $index++) {
        if (-not (Test-JsonSchemaValue $Value[$index] $items.Value $RootSchema)) {
          return $false
        }
      }
    }
    $contains = Get-JsonProperty $Schema 'contains'
    if ($null -ne $contains -and @($Value | Where-Object {
        Test-JsonSchemaValue $_ $contains.Value $RootSchema }).Count -lt 1) {
      return $false
    }
  }
  if ($null -ne $Value -and $Value -isnot [array] -and
      $Value -isnot [string] -and $Value -isnot [ValueType]) {
    $requiredProperty = Get-JsonProperty $Schema 'required'
    $required = if ($null -eq $requiredProperty) { @() } else {
      @($requiredProperty.Value)
    }
    foreach ($name in $required) {
      if ($null -eq (Get-JsonProperty $Value ([string]$name))) { return $false }
    }
    $properties = Get-JsonProperty $Schema 'properties'
    if ($null -ne $properties) {
      foreach ($property in $Value.PSObject.Properties) {
        $propertySchema = Get-JsonProperty $properties.Value $property.Name
        if ($null -eq $propertySchema) {
          $additional = Get-JsonProperty $Schema 'additionalProperties'
          if ($null -ne $additional -and $additional.Value -is [bool] -and
              -not [bool]$additional.Value) { return $false }
        } elseif (-not (Test-JsonSchemaValue $property.Value $propertySchema.Value $RootSchema)) {
          return $false
        }
      }
    }
  }
  return $true
}

function Assert-VirtualDisplaySetupResultContract($Result, $Manifest) {
  $relative = [string]$Manifest.virtualDisplay.resultSchema
  if ($relative -cne 'Deployment/virtual-display-setup-result-v1.schema.json') {
    throw 'virtualDisplaySetupResultInvalid'
  }
  $path = [IO.Path]::GetFullPath((Join-Path $installRoot $relative))
  $expected = [string]$Manifest.virtualDisplay.resultSchemaSha256
  if ($expected -notmatch '^[0-9a-f]{64}$' -or
      -not (Test-Path -LiteralPath $path -PathType Leaf) -or
      [bool]((Get-Item -LiteralPath $path -Force).Attributes -band
        [IO.FileAttributes]::ReparsePoint) -or
      (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne
        $expected.ToUpperInvariant()) { throw 'virtualDisplaySetupResultInvalid' }
  $schemaRaw = [IO.File]::ReadAllText($path)
  if (-not [LigaseStrictJson]::HasUniqueProperties($schemaRaw)) {
    throw 'virtualDisplaySetupResultInvalid'
  }
  $schema = $schemaRaw | ConvertFrom-Json
  if (-not (Test-JsonSchemaValue $Result $schema $schema)) {
    throw 'virtualDisplaySetupResultInvalid'
  }
}

function Test-VirtualDisplaySetupRunnerValidationAllowed {
  return $Action -in @("InstallVirtualDisplay", "UninstallVirtualDisplay") -and
    $env:LIGASE_INSTALL_VALIDATION_HARNESS -ceq "1" -and
    $env:LIGASE_VDISPLAY_RUNNER_VALIDATION -ceq "1" -and
    -not [string]::IsNullOrWhiteSpace($ValidationRoot) -and
    [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($ValidationRoot)) -ceq "D:\" -and
    [IO.Path]::GetFullPath($ValidationRoot) -ceq $installRoot
}

function Invoke-VirtualDisplaySetup($Manifest, [string]$Operation = "provision") {
  if ($Operation -notin @("provision", "uninstall")) {
    throw "virtualDisplaySetupRequestInvalid"
  }
  $helper = Get-VirtualDisplaySetupHelperPath $Manifest
  $operationDirectory = New-VirtualDisplaySetupOperationDirectory
  $requestPath = Join-Path $operationDirectory "request.json"
  $resultPath = Join-Path $operationDirectory "result.json"
  $requestWriter = $null
  $requestReader = $null
  $resultStream = $null
  $job = $null
  try {
    $requestWriter = [IO.FileStream]::new($requestPath,
      [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
      [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
    $resultStream = [IO.FileStream]::new($resultPath,
      [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite,
      [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
    $resultIdentity = [LigaseFileIdentity]::GetIdentitySha256(
      $resultStream.SafeFileHandle)
    $requestIdentity = [LigaseFileIdentity]::GetIdentitySha256(
      $requestWriter.SafeFileHandle)
    $operationId = Get-ByteSha256 ([Guid]::NewGuid().ToByteArray())
    $request = [ordered]@{
      schemaVersion = 1; operation = $Operation
      operationIdSha256 = $operationId.ToLowerInvariant()
      createdUtc = [DateTime]::UtcNow.ToString("o")
      sourceHead = [string]$Manifest.sourceHead
      helperSha256 = [string]$helper.sha256
      packageSha256 = [string]$Manifest.virtualDisplay.packageSha256
      installerToolSha256 = [string]$Manifest.virtualDisplay.installerToolSha256
      hardwareId = "ROOT\SUDOMAKER\SUDOVDA"
      hardCapMilliseconds = 120000; settleMilliseconds = 1000
      trustSelected = $Operation -ceq "provision"
      packageSelected = $Operation -ceq "provision"
      createSelected = $Operation -ceq "provision"
      legacyMarkerPolicy = "v1CertificateOnly"
      packageOwnershipPolicy = "provisionOperationProvenanceOnly"
      transport = [ordered]@{
        mode = "inheritedHandles"
        operationDirectoryAcl = "systemAdministratorsCallerOnly"
        operationDirectoryNonReparse = $true
        requestCreateNew = $true; requestFlushCompleted = $true
        requestWriteHandlesClosed = $true; requestReadOnlyHandle = $true
        requestWriteSharing = $false; resultCreateNew = $true
        resultExclusiveHandle = $true
        requestFileIdentitySha256 = $requestIdentity
        resultFileIdentitySha256 = $resultIdentity
      }
    }
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(
      ($request | ConvertTo-Json -Depth 8 -Compress))
    $requestWriter.Write($bytes, 0, $bytes.Length)
    $requestWriter.Flush($true)
    $requestWriter.Dispose(); $requestWriter = $null
    $requestReader = [IO.FileStream]::new($requestPath,
      [IO.FileMode]::Open, [IO.FileAccess]::Read,
      [IO.FileShare]::Read, 4096, [IO.FileOptions]::SequentialScan)
    if ([LigaseFileIdentity]::GetIdentitySha256(
        $requestReader.SafeFileHandle) -cne $requestIdentity) {
      throw "virtualDisplaySetupTransportInvalid"
    }
    $commandLine = '"' + [string]$helper.path + '" ' + $Operation +
      ' --request-handle ' +
      $requestReader.SafeFileHandle.DangerousGetHandle().ToInt64() +
      ' --result-handle ' +
      $resultStream.SafeFileHandle.DangerousGetHandle().ToInt64()
    $runnerValidation = Test-VirtualDisplaySetupRunnerValidationAllowed
    $runnerFault = if ($runnerValidation) {
      [string]$env:LIGASE_VDISPLAY_RUNNER_FAULT
    } else { "" }
    if ($runnerFault -notin @("", "startRetain", "timeout", "overflow",
        "dualPipePending", "schemaInvalidSuccess")) {
      throw "virtualDisplaySetupValidationRejected"
    }
    $workDeadline = if ($runnerValidation) { 500 } else { 110000 }
    $absoluteDeadline = if ($runnerValidation) { 2000 } else { 120000 }
    $startFault = if ($runnerFault -ceq "startRetain") { "retain" } else { "" }
    $runnerClock = [Diagnostics.Stopwatch]::StartNew()
    try {
      $job = [LigaseJobProcess]::StartExactWithHandles(
        [string]$helper.path, $commandLine, (Split-Path -Parent $helper.path),
        $startFault, 1000,
        $requestReader.SafeFileHandle.DangerousGetHandle().ToInt64(),
        $resultStream.SafeFileHandle.DangerousGetHandle().ToInt64())
    } catch {
      $remaining = [Math]::Max(1, [Math]::Min(5000,
        $absoluteDeadline - [int]$runnerClock.ElapsedMilliseconds))
      if (-not [LigaseJobProcess]::SecondaryContainment("", $remaining)) {
        throw "virtualDisplaySetupCleanupUnavailable"
      }
      throw "virtualDisplaySetupUnavailable"
    }
    $stdoutBuffer = [byte[]]::new(512)
    $stderrBuffer = [byte[]]::new(512)
    $stdout = [IO.MemoryStream]::new()
    $stderr = [IO.MemoryStream]::new()
    $stdoutTask = $job.StandardOutput.ReadAsync(
      $stdoutBuffer, 0, $stdoutBuffer.Length)
    $stderrTask = $job.StandardError.ReadAsync(
      $stderrBuffer, 0, $stderrBuffer.Length)
    $stdoutClosed = $false; $stderrClosed = $false
    $runnerFailure = ""
    $rootExited = $false; $jobEmpty = $false
    while ($runnerClock.ElapsedMilliseconds -lt $workDeadline) {
      $rootExited = $job.WaitForRoot(0)
      $jobEmpty = $rootExited -and $job.HasNoActiveProcesses()
      foreach ($streamName in @("stdout", "stderr")) {
        $task = if ($streamName -ceq "stdout") { $stdoutTask } else { $stderrTask }
        if ($null -ne $task -and $task.IsCompleted) {
          try { $count = $task.GetAwaiter().GetResult() }
          catch { $runnerFailure = "pipe"; continue }
          if ($count -eq 0) {
            if ($streamName -ceq "stdout") { $stdoutClosed = $true; $stdoutTask = $null }
            else { $stderrClosed = $true; $stderrTask = $null }
          } else {
            $target = if ($streamName -ceq "stdout") { $stdout } else { $stderr }
            $buffer = if ($streamName -ceq "stdout") { $stdoutBuffer } else { $stderrBuffer }
            if ($target.Length + $count -gt 4096) { $runnerFailure = "overflow" }
            else { $target.Write($buffer, 0, $count) }
            if ($streamName -ceq "stdout") {
              $stdoutTask = $job.StandardOutput.ReadAsync(
                $stdoutBuffer, 0, $stdoutBuffer.Length)
            } else {
              $stderrTask = $job.StandardError.ReadAsync(
                $stderrBuffer, 0, $stderrBuffer.Length)
            }
          }
        }
      }
      if ($rootExited -and $jobEmpty -and $stdoutClosed -and $stderrClosed) { break }
      if ($runnerFailure.Length -gt 0) { break }
      Start-Sleep -Milliseconds 10
    }
    if (-not ($rootExited -and $jobEmpty -and $stdoutClosed -and $stderrClosed)) {
      if ($runnerFailure.Length -eq 0) { $runnerFailure = "timeout" }
      $null = $job.Terminate()
      while ($runnerClock.ElapsedMilliseconds -lt $absoluteDeadline -and
          -not ($rootExited -and $jobEmpty -and $stdoutClosed -and $stderrClosed)) {
        $rootExited = $job.WaitForRoot(0)
        $jobEmpty = $rootExited -and $job.HasNoActiveProcesses()
        if ($null -ne $stdoutTask -and $stdoutTask.IsCompleted) {
          try { $count = $stdoutTask.GetAwaiter().GetResult() } catch { $count = -1 }
          if ($count -eq 0) { $stdoutClosed = $true; $stdoutTask = $null }
          elseif ($count -gt 0) { $stdoutTask = $job.StandardOutput.ReadAsync(
              $stdoutBuffer, 0, $stdoutBuffer.Length) }
        }
        if ($null -ne $stderrTask -and $stderrTask.IsCompleted) {
          try { $count = $stderrTask.GetAwaiter().GetResult() } catch { $count = -1 }
          if ($count -eq 0) { $stderrClosed = $true; $stderrTask = $null }
          elseif ($count -gt 0) { $stderrTask = $job.StandardError.ReadAsync(
              $stderrBuffer, 0, $stderrBuffer.Length) }
        }
        if (-not ($rootExited -and $jobEmpty -and $stdoutClosed -and $stderrClosed)) {
          Start-Sleep -Milliseconds 10
        }
      }
    }
    if (-not ($rootExited -and $jobEmpty -and $stdoutClosed -and $stderrClosed)) {
      throw "virtualDisplaySetupCleanupUnavailable"
    }
    if ($runnerFailure.Length -gt 0 -or $job.ExitCode -ne 0 -or
        $stdout.Length -ne 0 -or $stderr.Length -ne 0) {
      throw "virtualDisplaySetupUnavailable"
    }
    $resultStream.Flush($true)
    $resultStream.Position = 0
    if ($resultStream.Length -lt 2 -or $resultStream.Length -gt 65536) {
      throw "virtualDisplaySetupResultInvalid"
    }
    $resultBytes = [byte[]]::new([int]$resultStream.Length)
    $read = $resultStream.Read($resultBytes, 0, $resultBytes.Length)
    if ($read -ne $resultBytes.Length -or
        [LigaseFileIdentity]::GetIdentitySha256(
          $resultStream.SafeFileHandle) -cne $resultIdentity) {
      throw "virtualDisplaySetupResultInvalid"
    }
    $raw = [Text.UTF8Encoding]::new($false, $true).GetString($resultBytes)
    if (-not [LigaseStrictJson]::HasUniqueProperties($raw)) {
      throw "virtualDisplaySetupResultInvalid"
    }
    $result = $raw | ConvertFrom-Json
    Assert-VirtualDisplaySetupResultContract $result $Manifest
    $resultOperationIds = @($result.operationIdsSha256)
    if ($result.schemaVersion -ne 1 -or
        [string]$result.operation -cne $Operation -or
        $resultOperationIds.Count -lt 1 -or
        [string]$resultOperationIds[0] -cne
          $operationId.ToLowerInvariant() -or
        [string]$result.sourceHead -cne [string]$Manifest.sourceHead -or
        [string]$result.helperSha256 -cne [string]$helper.sha256 -or
        [string]$result.packageSha256 -cne
          [string]$Manifest.virtualDisplay.packageSha256 -or
        [string]$result.execution.resultFileState -cne "verified" -or
        [int]$result.execution.hardCapMilliseconds -ne 120000 -or
        [int]$result.execution.elapsedMilliseconds -lt 0 -or
        [int]$result.execution.elapsedMilliseconds -gt 120000) {
      throw "virtualDisplaySetupResultInvalid"
    }
    $script:virtualDisplayResultSha256 = Get-ByteSha256 $resultBytes
    $script:virtualDisplayResultIdentitySha256 = $resultIdentity
    return $result
  } finally {
    if ($stdout) { $stdout.Dispose() }
    if ($stderr) { $stderr.Dispose() }
    if ($job) { $job.Dispose() }
    if ($requestWriter) { $requestWriter.Dispose() }
    if ($requestReader) { $requestReader.Dispose() }
    if ($resultStream) { $resultStream.Dispose() }
  }
}

function Test-VirtualDisplaySetupEvidenceProjection($Projection) {
  if ($null -eq $Projection) { return $true }
  $names = @($Projection.PSObject.Properties.Name)
  $expected = @("operation", "operationIdSha256", "state", "code", "stage",
    "firstFailureFrozen", "writtenUtc", "resultFileIdentitySha256",
    "resultFileSha256", "failureDiagnostic")
  if ($names.Count -ne $expected.Count -or
      @($expected | Where-Object { $names -cnotcontains $_ }).Count -ne 0) {
    return $false
  }
  $diagnostic = $Projection.failureDiagnostic
  if ($null -eq $diagnostic) { return $false }
  $diagnosticNames = @($diagnostic.PSObject.Properties.Name)
  $expectedDiagnostic = @("state", "owner", "category", "reasonCode",
    "nativeCode", "nativeCodeHex", "logPath", "logPathState")
  if ($diagnosticNames.Count -ne $expectedDiagnostic.Count -or
      @($expectedDiagnostic | Where-Object {
        $diagnosticNames -cnotcontains $_ }).Count -ne 0) { return $false }
  $diagnosticValid = if ([string]$diagnostic.state -ceq "none") {
    [string]$diagnostic.owner -ceq "none" -and
    [string]$diagnostic.category -ceq "none" -and
    [string]$diagnostic.reasonCode -ceq "none" -and
    [int]$diagnostic.nativeCode -eq 0 -and
    [string]$diagnostic.nativeCodeHex -ceq "none" -and
    $null -eq $diagnostic.logPath -and
    [string]$diagnostic.logPathState -ceq "notApplicable"
  } else {
    [string]$diagnostic.state -ceq "captured" -and
    [string]$diagnostic.owner -in @("trust", "package") -and
    [string]$diagnostic.category -in @(
      "trustChain", "packageValidation", "setupApi", "nativeTool") -and
    [string]$diagnostic.reasonCode -match '^[a-z][A-Za-z0-9]{0,63}$' -and
    ([string]$diagnostic.nativeCodeHex -ceq "none" -or
      [string]$diagnostic.nativeCodeHex -match '^0x[0-9A-F]{8}$') -and
    [string]$diagnostic.logPathState -in @(
      "notApplicable", "available", "missing", "unavailable")
  }
  return $diagnosticValid -and
    [string]$Projection.operation -in @("provision", "uninstall") -and
    [string]$Projection.operationIdSha256 -match '^[0-9a-f]{64}$' -and
    [string]$Projection.state -in @("completed", "failed") -and
    [string]$Projection.code -match '^[a-z][A-Za-z0-9]{0,63}$' -and
    [string]$Projection.stage -match '^[a-z][A-Za-z0-9]{0,63}$' -and
    $Projection.firstFailureFrozen -is [bool] -and
    [string]$Projection.writtenUtc -match '^\d{4}-\d{2}-\d{2}T' -and
    [string]$Projection.resultFileIdentitySha256 -match '^[0-9a-f]{64}$' -and
    [string]$Projection.resultFileSha256 -match '^[0-9a-f]{64}$'
}

function Get-ExistingVirtualDisplaySetupEvidence {
  $path = Get-InstallerEvidencePath
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
  try {
    $raw = [IO.File]::ReadAllText($path)
    if (-not [LigaseStrictJson]::HasUniqueProperties($raw)) { return $null }
    $document = $raw | ConvertFrom-Json
    if ([int]$document.schemaVersion -ne 2 -or
        [string]$document.candidateSourceHead -cne (Get-EvidenceSourceHead) -or
        $null -eq $document.virtualDisplaySetup) { return $null }
    $setup = $document.virtualDisplaySetup
    $names = @($setup.PSObject.Properties.Name)
    if ($names.Count -ne 2 -or $names -cnotcontains "primary" -or
        $names -cnotcontains "recovery" -or
        -not (Test-VirtualDisplaySetupEvidenceProjection $setup.primary) -or
        -not (Test-VirtualDisplaySetupEvidenceProjection $setup.recovery)) {
      return $null
    }
    return $setup
  } catch { return $null }
}

function New-VirtualDisplaySetupEvidenceProjection($Result) {
  return [ordered]@{
    operation = [string]$Result.operation
    operationIdSha256 = [string]@($Result.operationIdsSha256)[0]
    state = [string]$Result.state
    code = [string]$Result.code
    stage = [string]$Result.stage
    firstFailureFrozen = [bool]$Result.firstFailureFrozen
    writtenUtc = [string]$Result.writtenUtc
    resultFileIdentitySha256 = [string]$script:virtualDisplayResultIdentitySha256
    resultFileSha256 = [string]$script:virtualDisplayResultSha256
    failureDiagnostic = [ordered]@{
      state = [string]$Result.failureDiagnostic.state
      owner = [string]$Result.failureDiagnostic.owner
      category = [string]$Result.failureDiagnostic.category
      reasonCode = [string]$Result.failureDiagnostic.reasonCode
      nativeCode = [int]$Result.failureDiagnostic.nativeCode
      nativeCodeHex = [string]$Result.failureDiagnostic.nativeCodeHex
      logPath = if ($null -eq $Result.failureDiagnostic.logPath) {
        $null
      } else { [string]$Result.failureDiagnostic.logPath }
      logPathState = [string]$Result.failureDiagnostic.logPathState
    }
  }
}

if (-not $shutdownOnlyAction) {
  Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public sealed class LigaseJobProcess : IDisposable
{
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint STARTF_USESTDHANDLES = 0x00000100;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const int JobObjectBasicAccountingInformation = 1;
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint WAIT_OBJECT_0 = 0;
    private static readonly IntPtr PROC_THREAD_ATTRIBUTE_HANDLE_LIST =
        new IntPtr(0x00020002);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out IntPtr readPipe, out IntPtr writePipe,
        ref SECURITY_ATTRIBUTES attributes, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(
        IntPtr handle, uint mask, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint creationFlags, IntPtr environment,
        string currentDirectory, ref STARTUPINFOEX startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList, int attributeCount, uint flags,
        ref UIntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList, uint flags, IntPtr attribute,
        IntPtr value, UIntPtr size, IntPtr previousValue,
        IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(
        IntPtr attributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateJobObject(
        IntPtr jobAttributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr job, int informationClass, IntPtr information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(
        IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(
        IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(
        IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(
        IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(
        IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(
        IntPtr job, int informationClass, IntPtr information,
        uint informationLength, out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public Process Process { get; private set; }
    public FileStream StandardOutput { get; private set; }
    public FileStream StandardError { get; private set; }
    private IntPtr _job;
    private IntPtr _processHandle;
    private static IntPtr _retainedJob;
    private static IntPtr _retainedProcess;
    private static IntPtr _retainedThread;
    public static int LastCleanupPid { get; private set; }
    public static bool LastCleanupProven { get; private set; }
    public static bool AuthorityRetained { get; private set; }
    public static bool SecondaryAttempted { get; private set; }
    public static bool SecondaryCompleted { get; private set; }

    private LigaseJobProcess() {}

    public static LigaseJobProcess Start(
        string commandInterpreter, string installerPath, string workingDirectory,
        string validationFault)
    {
        string commandLine =
            "\"" + commandInterpreter + "\" /d /s /c \"\"" +
            installerPath + "\"\"";
        return StartExact(
            commandInterpreter, commandLine, workingDirectory, validationFault);
    }

    public static LigaseJobProcess StartExact(
        string applicationPath, string exactCommandLine,
        string workingDirectory, string validationFault)
    {
        return StartExact(
            applicationPath, exactCommandLine, workingDirectory,
            validationFault, 5000);
    }

    public static LigaseJobProcess StartExact(
        string applicationPath, string exactCommandLine,
        string workingDirectory, string validationFault,
        int cleanupMilliseconds)
    {
        return StartExactCore(applicationPath, exactCommandLine,
            workingDirectory, validationFault, cleanupMilliseconds,
            new IntPtr[0]);
    }

    public static LigaseJobProcess StartExactWithHandles(
        string applicationPath, string exactCommandLine,
        string workingDirectory, string validationFault,
        int cleanupMilliseconds, long requestHandle, long resultHandle)
    {
        if (requestHandle <= 0 || resultHandle <= 0 ||
            requestHandle == resultHandle)
            throw new ArgumentOutOfRangeException("requestHandle");
        return StartExactCore(applicationPath, exactCommandLine,
            workingDirectory, validationFault, cleanupMilliseconds,
            new [] { new IntPtr(requestHandle), new IntPtr(resultHandle) });
    }

    private static LigaseJobProcess StartExactCore(
        string applicationPath, string exactCommandLine,
        string workingDirectory, string validationFault,
        int cleanupMilliseconds, IntPtr[] extraHandles)
    {
        if (cleanupMilliseconds < 1 || cleanupMilliseconds > 5000)
            throw new ArgumentOutOfRangeException("cleanupMilliseconds");
        IntPtr stdoutRead = IntPtr.Zero;
        IntPtr stdoutWrite = IntPtr.Zero;
        IntPtr stderrRead = IntPtr.Zero;
        IntPtr stderrWrite = IntPtr.Zero;
        IntPtr job = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr inheritedHandleList = IntPtr.Zero;
        PROCESS_INFORMATION pi = new PROCESS_INFORMATION();
        bool processCreated = false;
        bool assigned = false;
        bool resumed = false;
        Process managedProcess = null;
        FileStream output = null;
        FileStream error = null;
        try
        {
            LastCleanupPid = 0;
            LastCleanupProven = false;
            AuthorityRetained = false;
            SecondaryAttempted = false;
            SecondaryCompleted = false;
            foreach (IntPtr extraHandle in extraHandles)
                if (extraHandle == IntPtr.Zero || !SetHandleInformation(
                        extraHandle, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT))
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(), "processHandleUnavailable");
            var attributes = new SECURITY_ATTRIBUTES {
                nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
                bInheritHandle = true
            };
            if (!CreatePipe(out stdoutRead, out stdoutWrite, ref attributes, 0) ||
                !CreatePipe(out stderrRead, out stderrWrite, ref attributes, 0) ||
                !SetHandleInformation(
                    stdoutRead, HANDLE_FLAG_INHERIT, 0) ||
                !SetHandleInformation(
                    stderrRead, HANDLE_FLAG_INHERIT, 0))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "processPipeUnavailable");

            job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "processJobUnavailable");
            var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            limits.BasicLimitInformation.LimitFlags =
                JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            int limitsSize = Marshal.SizeOf(limits);
            IntPtr limitsPointer = Marshal.AllocHGlobal(limitsSize);
            try
            {
                Marshal.StructureToPtr(limits, limitsPointer, false);
                if (!SetInformationJobObject(
                    job, JobObjectExtendedLimitInformation,
                    limitsPointer, unchecked((uint)limitsSize)))
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "processJobUnavailable");
            }
            finally
            {
                Marshal.FreeHGlobal(limitsPointer);
            }

            UIntPtr attributeBytes = UIntPtr.Zero;
            InitializeProcThreadAttributeList(
                IntPtr.Zero, 1, 0, ref attributeBytes);
            attributeList = Marshal.AllocHGlobal(
                checked((int)attributeBytes.ToUInt64()));
            if (!InitializeProcThreadAttributeList(
                attributeList, 1, 0, ref attributeBytes))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "processAttributeUnavailable");
            int inheritedCount = checked(2 + extraHandles.Length);
            inheritedHandleList = Marshal.AllocHGlobal(
                IntPtr.Size * inheritedCount);
            Marshal.WriteIntPtr(inheritedHandleList, 0, stdoutWrite);
            Marshal.WriteIntPtr(
                inheritedHandleList, IntPtr.Size, stderrWrite);
            for (int index = 0; index < extraHandles.Length; index++)
                Marshal.WriteIntPtr(inheritedHandleList,
                    IntPtr.Size * (index + 2), extraHandles[index]);
            if (!UpdateProcThreadAttribute(
                attributeList, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                inheritedHandleList,
                new UIntPtr(unchecked((uint)(IntPtr.Size * inheritedCount))),
                IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "processAttributeUnavailable");

            var startup = new STARTUPINFOEX {
                StartupInfo = new STARTUPINFO {
                    cb = Marshal.SizeOf(typeof(STARTUPINFOEX)),
                    dwFlags = STARTF_USESTDHANDLES,
                    hStdInput = IntPtr.Zero,
                    hStdOutput = stdoutWrite,
                    hStdError = stderrWrite
                },
                lpAttributeList = attributeList
            };
            var commandLine = new StringBuilder(exactCommandLine);
            if (!CreateProcess(
                applicationPath, commandLine, IntPtr.Zero, IntPtr.Zero,
                true, CREATE_SUSPENDED | CREATE_NO_WINDOW |
                EXTENDED_STARTUPINFO_PRESENT, IntPtr.Zero,
                workingDirectory, ref startup, out pi))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "processCreateUnavailable");
            processCreated = true;
            foreach (IntPtr extraHandle in extraHandles)
                if (!SetHandleInformation(
                        extraHandle, HANDLE_FLAG_INHERIT, 0))
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(), "processHandleUnavailable");
            LastCleanupPid = unchecked((int)pi.dwProcessId);
            DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            attributeList = IntPtr.Zero;
            Marshal.FreeHGlobal(inheritedHandleList);
            inheritedHandleList = IntPtr.Zero;
            if (String.Equals(
                validationFault, "assign", StringComparison.Ordinal) ||
                String.Equals(
                    validationFault, "startTerminate",
                    StringComparison.Ordinal) ||
                String.Equals(
                    validationFault, "startWait",
                    StringComparison.Ordinal) ||
                String.Equals(
                    validationFault, "retain",
                    StringComparison.Ordinal) ||
                String.Equals(
                    validationFault, "secondaryTerminate",
                    StringComparison.Ordinal) ||
                String.Equals(
                    validationFault, "secondaryWait",
                    StringComparison.Ordinal) ||
                String.Equals(
                    validationFault, "secondaryAccounting",
                    StringComparison.Ordinal) ||
                !AssignProcessToJobObject(job, pi.hProcess))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "processJobAssignFailed");
            assigned = true;
            if (String.Equals(
                validationFault, "resume", StringComparison.Ordinal) ||
                String.Equals(
                    validationFault, "jobTerminate",
                    StringComparison.Ordinal) ||
                String.Equals(
                    validationFault, "jobAccounting",
                    StringComparison.Ordinal) ||
                ResumeThread(pi.hThread) == UInt32.MaxValue)
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "processResumeFailed");
            resumed = true;

            CloseHandle(stdoutWrite);
            stdoutWrite = IntPtr.Zero;
            CloseHandle(stderrWrite);
            stderrWrite = IntPtr.Zero;
            CloseHandle(pi.hThread);
            pi.hThread = IntPtr.Zero;

            managedProcess = Process.GetProcessById(
                unchecked((int)pi.dwProcessId));
            output = new FileStream(
                new SafeFileHandle(stdoutRead, true), FileAccess.Read,
                4096, false);
            stdoutRead = IntPtr.Zero;
            error = new FileStream(
                new SafeFileHandle(stderrRead, true), FileAccess.Read,
                4096, false);
            stderrRead = IntPtr.Zero;

            var value = new LigaseJobProcess();
            value._job = job;
            job = IntPtr.Zero;
            value._processHandle = pi.hProcess;
            pi.hProcess = IntPtr.Zero;
            value.Process = managedProcess;
            managedProcess = null;
            value.StandardOutput = output;
            output = null;
            value.StandardError = error;
            error = null;
            LastCleanupPid = 0;
            LastCleanupProven = true;
            return value;
        }
        catch
        {
            if (processCreated)
            {
                bool proven = false;
                bool forceRetain =
                    String.Equals(
                        validationFault, "retain",
                        StringComparison.Ordinal) ||
                    String.Equals(
                        validationFault, "secondaryTerminate",
                        StringComparison.Ordinal) ||
                    String.Equals(
                        validationFault, "secondaryWait",
                        StringComparison.Ordinal) ||
                    String.Equals(
                        validationFault, "secondaryAccounting",
                        StringComparison.Ordinal);
                var cleanupClock = Stopwatch.StartNew();
                if (!assigned)
                {
                    bool terminated = !forceRetain && !String.Equals(
                        validationFault, "startTerminate",
                        StringComparison.Ordinal) &&
                        TerminateProcess(pi.hProcess, 18);
                    if (!terminated && !forceRetain)
                        terminated = TerminateProcess(pi.hProcess, 18);
                    bool signaled = false;
                    if (terminated)
                    {
                        if (!String.Equals(
                            validationFault, "startWait",
                            StringComparison.Ordinal))
                            signaled = WaitForSingleObject(
                                pi.hProcess, 0) == WAIT_OBJECT_0;
                        while (!signaled &&
                            cleanupClock.ElapsedMilliseconds <
                                cleanupMilliseconds)
                        {
                            signaled = WaitForSingleObject(
                                pi.hProcess, 20) == WAIT_OBJECT_0;
                        }
                    }
                    proven = terminated && signaled;
                }
                else
                {
                    bool terminated = !String.Equals(
                        validationFault, "jobTerminate",
                        StringComparison.Ordinal) &&
                        TerminateJobObject(job, 18);
                    if (!terminated && !forceRetain)
                        terminated = TerminateJobObject(job, 18);
                    bool rootSignaled = false;
                    while (!rootSignaled &&
                        cleanupClock.ElapsedMilliseconds < cleanupMilliseconds)
                    {
                        rootSignaled = WaitForSingleObject(
                            pi.hProcess, 20) == WAIT_OBJECT_0;
                    }
                    bool jobEmpty = false;
                    if (terminated && rootSignaled)
                    {
                        if (!String.Equals(
                            validationFault, "jobAccounting",
                            StringComparison.Ordinal))
                            jobEmpty = IsJobEmpty(job);
                        if (!jobEmpty && !resumed)
                        {
                            // The only process is still suspended. Closing the
                            // kill-on-close Job is the independent fallback.
                            if (CloseHandle(job))
                            {
                                job = IntPtr.Zero;
                                jobEmpty = true;
                            }
                        }
                    }
                    proven = terminated && rootSignaled && jobEmpty;
                }
                if (!proven)
                {
                    _retainedJob = job;
                    job = IntPtr.Zero;
                    _retainedProcess = pi.hProcess;
                    pi.hProcess = IntPtr.Zero;
                    _retainedThread = pi.hThread;
                    pi.hThread = IntPtr.Zero;
                    AuthorityRetained = true;
                }
                else
                {
                    LastCleanupPid = 0;
                    LastCleanupProven = true;
                }
            }
            throw;
        }
        finally
        {
            foreach (IntPtr extraHandle in extraHandles)
                if (extraHandle != IntPtr.Zero)
                    SetHandleInformation(extraHandle, HANDLE_FLAG_INHERIT, 0);
            if (output != null) output.Dispose();
            if (error != null) error.Dispose();
            if (managedProcess != null) managedProcess.Dispose();
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            if (stdoutRead != IntPtr.Zero) CloseHandle(stdoutRead);
            if (stdoutWrite != IntPtr.Zero) CloseHandle(stdoutWrite);
            if (stderrRead != IntPtr.Zero) CloseHandle(stderrRead);
            if (stderrWrite != IntPtr.Zero) CloseHandle(stderrWrite);
            if (job != IntPtr.Zero) CloseHandle(job);
            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
            if (inheritedHandleList != IntPtr.Zero)
                Marshal.FreeHGlobal(inheritedHandleList);
        }
    }

    public bool Terminate()
    {
        return _job != IntPtr.Zero && TerminateJobObject(_job, 18);
    }

    public bool WaitForRoot(int milliseconds)
    {
        return _processHandle != IntPtr.Zero &&
            WaitForSingleObject(
                _processHandle, unchecked((uint)milliseconds)) ==
                WAIT_OBJECT_0;
    }

    public int ExitCode
    {
        get
        {
            uint code;
            if (_processHandle == IntPtr.Zero ||
                !GetExitCodeProcess(_processHandle, out code))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "processExitUnavailable");
            return unchecked((int)code);
        }
    }

    public bool HasNoActiveProcesses()
    {
        if (_job == IntPtr.Zero) return false;
        var accounting = new JOBOBJECT_BASIC_ACCOUNTING_INFORMATION();
        int size = Marshal.SizeOf(accounting);
        IntPtr pointer = Marshal.AllocHGlobal(size);
        try
        {
            uint returned;
            if (!QueryInformationJobObject(
                _job, JobObjectBasicAccountingInformation,
                pointer, unchecked((uint)size), out returned))
                return false;
            accounting = (JOBOBJECT_BASIC_ACCOUNTING_INFORMATION)
                Marshal.PtrToStructure(
                    pointer,
                    typeof(JOBOBJECT_BASIC_ACCOUNTING_INFORMATION));
            return accounting.ActiveProcesses == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    public static bool SecondaryContainment(string validationFault)
    {
        return SecondaryContainment(validationFault, 5000);
    }

    public static bool SecondaryContainment(
        string validationFault, int cleanupMilliseconds)
    {
        if (cleanupMilliseconds < 1 || cleanupMilliseconds > 5000)
            return false;
        SecondaryAttempted = true;
        SecondaryCompleted = false;
        if (_retainedProcess == IntPtr.Zero)
            return LastCleanupProven;
        if (_retainedJob != IntPtr.Zero &&
            !String.Equals(
                validationFault, "secondaryTerminate",
                StringComparison.Ordinal) &&
            !String.Equals(
                validationFault, "secondaryAccounting",
                StringComparison.Ordinal))
            TerminateJobObject(_retainedJob, 18);
        if (WaitForSingleObject(_retainedProcess, 0) != WAIT_OBJECT_0 &&
            !String.Equals(
                validationFault, "secondaryTerminate",
                StringComparison.Ordinal) &&
            !String.Equals(
                validationFault, "secondaryAccounting",
                StringComparison.Ordinal))
            TerminateProcess(_retainedProcess, 18);
        var clock = Stopwatch.StartNew();
        bool rootSignaled = false;
        while (!rootSignaled &&
            clock.ElapsedMilliseconds < cleanupMilliseconds)
            rootSignaled = !String.Equals(
                validationFault, "secondaryWait",
                StringComparison.Ordinal) &&
                WaitForSingleObject(
                    _retainedProcess, 20) == WAIT_OBJECT_0;
        bool jobEmpty = _retainedJob == IntPtr.Zero ||
            (!String.Equals(
                validationFault, "secondaryAccounting",
                StringComparison.Ordinal) &&
            IsJobEmpty(_retainedJob));
        if (!rootSignaled || !jobEmpty)
            return false;
        if (_retainedThread != IntPtr.Zero)
            CloseHandle(_retainedThread);
        CloseHandle(_retainedProcess);
        if (_retainedJob != IntPtr.Zero)
            CloseHandle(_retainedJob);
        _retainedThread = IntPtr.Zero;
        _retainedProcess = IntPtr.Zero;
        _retainedJob = IntPtr.Zero;
        LastCleanupPid = 0;
        LastCleanupProven = true;
        AuthorityRetained = false;
        SecondaryCompleted = true;
        return true;
    }

    private static bool IsJobEmpty(IntPtr job)
    {
        if (job == IntPtr.Zero) return false;
        var accounting = new JOBOBJECT_BASIC_ACCOUNTING_INFORMATION();
        int size = Marshal.SizeOf(accounting);
        IntPtr pointer = Marshal.AllocHGlobal(size);
        try
        {
            uint returned;
            if (!QueryInformationJobObject(
                job, JobObjectBasicAccountingInformation,
                pointer, unchecked((uint)size), out returned))
                return false;
            accounting = (JOBOBJECT_BASIC_ACCOUNTING_INFORMATION)
                Marshal.PtrToStructure(
                    pointer,
                    typeof(JOBOBJECT_BASIC_ACCOUNTING_INFORMATION));
            return accounting.ActiveProcesses == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    public void Dispose()
    {
        if (StandardOutput != null) StandardOutput.Dispose();
        if (StandardError != null) StandardError.Dispose();
        if (Process != null) Process.Dispose();
        if (_processHandle != IntPtr.Zero)
        {
            CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }
        if (_job != IntPtr.Zero)
        {
            CloseHandle(_job);
            _job = IntPtr.Zero;
        }
    }
}
"@
}

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
  if ($Action -in @("InstallVirtualDisplay", "UninstallVirtualDisplay") -and
      (Test-VirtualDisplaySetupRunnerValidationAllowed)) {
    return Join-Path $installRoot "last-outcome.json"
  }
  if ($Action -in @("RecordEvidence", "ValidateInstallTransaction") -and
      $env:LIGASE_INSTALL_VALIDATION_HARNESS -ceq "1" -and
      -not [string]::IsNullOrWhiteSpace($ValidationRoot) -and
      [IO.Path]::GetFullPath($ValidationRoot) -ceq $installRoot -and
      [IO.Path]::GetPathRoot($installRoot) -ceq "D:\") {
    return Join-Path $installRoot "last-outcome.json"
  }
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
        if ($properties.Count -ne 14 -or
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
            $properties -notcontains "aclInspectionReason" -or
            $properties -notcontains "emptyRootInspectionReason" -or
            $properties -notcontains "aclMutationOccurred" -or
            $properties -notcontains "aclRollback" -or
            [string]$failure.code -notin @(
              "installTransactionAclInvalid",
              "installTransactionUnavailable",
              "installTransactionInvalid") -or
            [string]$failure.stage -notin @(
              "resolveProgramData", "rejectReparse", "createSegment",
              "openHandle", "verifyIdentity", "resolveFinalPath",
              "canonicalRoot", "inspectAcl", "readSecurityDescriptor",
               "descriptorLength", "descriptorCopy", "descriptorParse",
               "buildSecurityDescriptor", "compareSecurityDescriptor",
               "inspectEmptyRootOwner", "inspectEmptyRootChildren",
               "queryEmptyRootStreams", "inspectEmptyRootStreams",
               "inspectEmptyRootStreamMetadata",
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
          managedFailure = @(
            20001, 20002, 20003, 20004, 20005, 20006, 20007,
            20008, 20009, 20010, 20011, 20015)
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
        $aclInspectionReason = [string]$failure.aclInspectionReason
        if ($aclInspectionReason -notin @(
            "none", "exact", "notExact",
            "canonicalRootInspectionFailed",
            "securityDescriptorReadFailed",
            "descriptorLengthInvalid",
            "descriptorCopyFailed",
            "descriptorParseFailed",
            "expectedDescriptorBuildFailed",
            "descriptorCompareFailed")) {
          throw "installTransactionInvalid"
        }
        $managedAclTuples = @{
          canonicalRoot = @(20001, "canonicalRootInspectionFailed")
          readSecurityDescriptor = @(20002, "securityDescriptorReadFailed")
          descriptorLength = @(20003, "descriptorLengthInvalid")
          descriptorCopy = @(20004, "descriptorCopyFailed")
          descriptorParse = @(20005, "descriptorParseFailed")
          buildSecurityDescriptor = @(
            20006, "expectedDescriptorBuildFailed")
          compareSecurityDescriptor = @(20007, "descriptorCompareFailed")
        }
        $managedAclReasons = @(
          "canonicalRootInspectionFailed",
          "securityDescriptorReadFailed",
          "descriptorLengthInvalid",
          "descriptorCopyFailed",
          "descriptorParseFailed",
          "expectedDescriptorBuildFailed",
          "descriptorCompareFailed")
        $managedAclCodes = 20001..20007
        $managedAclStage = [string]$failure.stage
        $managedAclTuple = $managedAclTuples[$managedAclStage]
        $isExactManagedAclTuple = (
          $nativeCategory -ceq "managedFailure" -and
          $null -ne $managedAclTuple -and
          $nativeCode -eq [int]$managedAclTuple[0] -and
          $aclInspectionReason -ceq [string]$managedAclTuple[1])
        $hasManagedAclField = (
          $nativeCategory -ceq "managedFailure" -or
          $nativeCode -in $managedAclCodes -or
          $aclInspectionReason -in $managedAclReasons)
        if ($hasManagedAclField -ne $isExactManagedAclTuple) {
          throw "installTransactionInvalid"
        }
        $emptyRootInspectionReason =
          [string]$failure.emptyRootInspectionReason
        $emptyRootTuples = @{
          inspectEmptyRootOwner = @(20008, "ownerNotAdministrators")
          inspectEmptyRootChildren = @(20009, "childEntryPresent")
          inspectEmptyRootStreams = @(20010, "namedDataStreamPresent")
          inspectEmptyRootStreamMetadata = @(20011, "streamMetadataInvalid")
          queryEmptyRootStreams = @(20015, "streamQueryFailed")
        }
        $emptyRootReasons = @(
          "ownerNotAdministrators", "childEntryPresent",
          "namedDataStreamPresent", "streamMetadataInvalid",
          "streamQueryFailed")
        if ($emptyRootInspectionReason -notin @(
            "none", "empty",
            "ownerNotAdministrators", "childEntryPresent",
            "namedDataStreamPresent", "streamMetadataInvalid",
            "streamQueryFailed")) {
          throw "installTransactionInvalid"
        }
        $emptyRootStage = [string]$failure.stage
        $emptyRootTuple = $emptyRootTuples[$emptyRootStage]
        $isExactEmptyRootTuple = (
          $nativeCategory -ceq "managedFailure" -and
          $null -ne $emptyRootTuple -and
          $nativeCode -eq [int]$emptyRootTuple[0] -and
          $emptyRootInspectionReason -ceq [string]$emptyRootTuple[1])
        $hasEmptyRootField = (
          $nativeCode -in @(20008, 20009, 20010, 20011, 20015) -or
          $emptyRootInspectionReason -in $emptyRootReasons)
        if ($hasEmptyRootField -ne $isExactEmptyRootTuple) {
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
        $script:transactionAclInspectionReason = $aclInspectionReason
        $script:transactionEmptyRootInspectionReason =
          $emptyRootInspectionReason
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

function Set-InstallTransactionReadbackFailure(
  [string]$Stage,
  [string]$Reason,
  [string]$Code = "installTransactionInvalid"
) {
  $script:transactionReadbackStage = $Stage
  $script:transactionReadbackReason = $Reason
  $script:finalFailedField = "installTransaction"
  $script:finalComponents.installTransaction = "failed"
  throw $Code
}

function Assert-ClosedProperties(
  $Object,
  [string[]]$Expected,
  [string]$Stage = "schemaValidation"
) {
  $actual = @($Object.PSObject.Properties.Name)
  foreach ($name in $Expected) {
    if ($actual -cnotcontains $name) {
      Set-InstallTransactionReadbackFailure $Stage "missingProperty"
    }
  }
  if ($actual.Count -ne $Expected.Count) {
    Set-InstallTransactionReadbackFailure $Stage "unknownProperty"
  }
}

function Assert-TransactionRawShape([string]$Raw) {
  $top = @(
    "schemaVersion", "transactionId", "createdUtc", "manifestSourceHead",
    "manifestSha256", "installLayout", "installDirectory", "launcher",
    "desktopSelected", "virtualDisplaySelected", "configureFirewall",
    "shortcuts", "firewallApplied", "firewallWasConfigured")
  foreach ($name in $top) {
    $count = [regex]::Matches(
      $Raw, '"' + [regex]::Escape($name) + '"\s*:').Count
    if ($count -eq 0) {
      Set-InstallTransactionReadbackFailure "rawShape" "missingProperty"
    }
    if ($count -ne 1) {
      Set-InstallTransactionReadbackFailure "rawShape" "duplicateProperty"
    }
  }
  foreach ($name in @("field", "path", "existed", "bytes")) {
    $count = [regex]::Matches($Raw, '"' + $name + '"\s*:').Count
    if ($count -lt 4) {
      Set-InstallTransactionReadbackFailure "rawShape" "missingProperty"
    }
    if ($count -ne 4) {
      Set-InstallTransactionReadbackFailure "rawShape" "duplicateProperty"
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
  $script:transactionReadbackStage = "rawRead"
  try {
    $raw = Invoke-InstallTransactionHelper "read"
  } catch {
    $reason = if (
      $script:transactionHelperStage -eq "read" -and
      $script:transactionHelperNativeCategory -in @(
        "fileNotFound", "pathNotFound")) {
      "missing"
    } else {
      "helperFailure"
    }
    Set-InstallTransactionReadbackFailure "rawRead" $reason (
      [string]$_.Exception.Message)
  }
  Assert-TransactionRawShape $raw
  $script:transactionReadbackStage = "jsonParse"
  try {
    $document = $raw | ConvertFrom-Json
  } catch {
    Set-InstallTransactionReadbackFailure "jsonParse" "malformedJson"
  }
  Assert-ClosedProperties $document @(
    "schemaVersion", "transactionId", "createdUtc", "manifestSourceHead",
    "manifestSha256", "installLayout", "installDirectory", "launcher",
    "desktopSelected", "virtualDisplaySelected", "configureFirewall",
    "shortcuts", "firewallApplied", "firewallWasConfigured") "schemaValidation"
  if ($document.schemaVersion -isnot [int] -or
      $document.transactionId -isnot [string] -or
      $document.createdUtc -isnot [string] -or
      $document.manifestSourceHead -isnot [string] -or
      $document.manifestSha256 -isnot [string] -or
      $document.installLayout -isnot [string] -or
      $document.installDirectory -isnot [string] -or
      $document.launcher -isnot [string] -or
      $document.desktopSelected -isnot [bool] -or
      $document.virtualDisplaySelected -isnot [bool] -or
      $document.configureFirewall -isnot [bool] -or
      $document.firewallApplied -isnot [bool] -or
      $document.firewallWasConfigured -isnot [bool] -or
      $document.shortcuts -isnot [array]) {
    Set-InstallTransactionReadbackFailure "schemaValidation" "wrongType"
  }
  $script:transactionReadbackStage = "freshnessValidation"
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
    Set-InstallTransactionReadbackFailure (
      "freshnessValidation") "stale" "installTransactionStale"
  }
  $script:transactionReadbackStage = "identityValidation"
  $transactionBytes = [byte[]]$null
  try { $transactionBytes = [Convert]::FromBase64String(
      [string]$document.transactionId) } catch {
    Set-InstallTransactionReadbackFailure "identityValidation" "wrongType"
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
    $reason = if (
      $document.desktopSelected -isnot [bool] -or
      $document.virtualDisplaySelected -isnot [bool] -or
      $document.configureFirewall -isnot [bool] -or
      $document.firewallApplied -isnot [bool] -or
      $document.firewallWasConfigured -isnot [bool]) {
      "wrongType"
    } else {
      "identityMismatch"
    }
    Set-InstallTransactionReadbackFailure "identityValidation" $reason
  }
  $script:transactionReadbackStage = "shortcutValidation"
  $validated = @()
  $totalBytes = 0
  for ($index = 0; $index -lt 4; $index++) {
    $entry = $actualEntries[$index]
    $expected = $expectedEntries[$index]
    Assert-ClosedProperties $entry @(
      "field", "path", "existed", "bytes") "shortcutValidation"
    if ([string]$entry.field -cne [string]$expected.field -or
        [string]$entry.path -cne [IO.Path]::GetFullPath([string]$expected.path) -or
        $entry.existed -isnot [bool]) {
      Set-InstallTransactionReadbackFailure (
        "shortcutValidation") "shortcutSnapshotInvalid"
    }
    $bytes = $null
    if ([bool]$entry.existed) {
      if ($entry.bytes -isnot [string]) {
        Set-InstallTransactionReadbackFailure "shortcutValidation" "wrongType"
      }
      try { $bytes = [Convert]::FromBase64String([string]$entry.bytes) } catch {
        Set-InstallTransactionReadbackFailure (
          "shortcutValidation") "shortcutSnapshotInvalid"
      }
      if ($bytes.Count -gt 1048576) {
        Set-InstallTransactionReadbackFailure (
          "shortcutValidation") "shortcutSnapshotInvalid"
      }
      $totalBytes += $bytes.Count
    } elseif ($null -ne $entry.bytes) {
      Set-InstallTransactionReadbackFailure (
        "shortcutValidation") "shortcutSnapshotInvalid"
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
  if ($totalBytes -gt 4194304) {
    Set-InstallTransactionReadbackFailure (
      "shortcutValidation") "shortcutSnapshotInvalid"
  }
  $script:shortcutRollback = [ordered]@{
    launcher = $expectedLauncher
    snapshot = $validated
  }
  $script:firewallAppliedByTransaction = [bool]$document.firewallApplied
  $script:firewallWasConfigured = [bool]$document.firewallWasConfigured
  $script:transactionCreated = $true
  $script:transactionCleanupResult = "pending"
  $script:finalComponents.installTransaction = "verified"
  $script:transactionReadbackStage = "completed"
  $script:transactionReadbackReason = "none"
  return $true
}

function Remove-InstallTransaction {
  if (-not $script:transactionCreated) {
    $script:transactionCleanupResult = "notCreated"
    return
  }
  $script:transactionReadbackStage = "cleanup"
  try {
    $result = Invoke-InstallTransactionHelper "delete"
  } catch {
    Set-InstallTransactionReadbackFailure (
      "cleanup") "cleanupFailed" ([string]$_.Exception.Message)
  }
  if ($result -cne
      '{"code":"installTransactionDeleted","success":true}') {
    Set-InstallTransactionReadbackFailure "cleanup" "cleanupFailed"
  }
  $script:transactionCreated = $false
  $script:transactionCleanupResult = "completed"
  $script:transactionReadbackStage = "completed"
  $script:transactionReadbackReason = "none"
}

function Write-InstallerEvidence {
  $evidencePath = Get-InstallerEvidencePath
  $directory = Split-Path -Parent $evidencePath
  if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
    $null = New-Item -ItemType Directory -Path $directory -Force
  }
  if ($Action -notin @("RecordEvidence", "ValidateInstallTransaction") -and
      -not (Test-VirtualDisplaySetupRunnerValidationAllowed)) {
    Set-SecureDataRootAcl $directory
  }
  $existingVirtualDisplaySetup = Get-ExistingVirtualDisplaySetupEvidence
  $document = [ordered]@{
    schemaVersion = 2
    candidateSourceHead = Get-EvidenceSourceHead
    phase = $EvidencePhase
    success = if ($EvidenceSuccess -ceq "unknown") {
      $null
    } else { $EvidenceSuccess -ceq "true" }
    resultCode = $EvidenceResultCode
    installDirectory = Get-SafePathProjection $installRoot
    dataRoot = [ordered]@{
      action = $EvidenceDataRootAction
      source = Get-SafePathProjection $EvidenceDataRootSource
      target = Get-SafePathProjection $DataRoot
    }
    uninstall = if ($EvidenceUninstallDisposition -ceq "none") {
      $null
    } else {
      [ordered]@{
        disposition = $EvidenceUninstallDisposition
        state = $EvidenceUninstallState
        resultCode = $EvidenceResultCode
      }
    }
    helper = [ordered]@{
      exitCode = $EvidenceHelperExit
      resultCode = $EvidenceResultCode
    }
    transactionHelper = [ordered]@{
      nativeExitCode = $script:transactionHelperNativeExit
      stage = $script:transactionHelperStage
      nativeCategory = $script:transactionHelperNativeCategory
      nativeCode = $script:transactionHelperNativeCode
      bindingReason = $script:transactionBindingReason
      readbackStage = $script:transactionReadbackStage
      readbackReason = $script:transactionReadbackReason
    }
    virtualDisplaySetup = $existingVirtualDisplaySetup
    failedField = if ($EvidenceFailedField -ceq "none") {
      $null
    } else { $EvidenceFailedField }
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
  $null = Write-InstallerEvidenceDocument $document
  return $document
}

function Write-InstallerEvidenceDocument($Document) {
  $evidencePath = Get-InstallerEvidencePath
  $directory = Split-Path -Parent $evidencePath
  if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
    $null = New-Item -ItemType Directory -Path $directory -Force
  }
  if ($Action -notin @("RecordEvidence", "ValidateInstallTransaction") -and
      -not (Test-VirtualDisplaySetupRunnerValidationAllowed)) {
    Set-SecureDataRootAcl $directory
  }
  $bytes = [Text.UTF8Encoding]::new($false).GetBytes(
    ($Document | ConvertTo-Json -Depth 10 -Compress))
  $temporary = Join-Path $directory (
    ".last-outcome-" + [Guid]::NewGuid().ToString("N") + ".tmp")
  $backup = Join-Path $directory (
    ".last-outcome-backup-" + [Guid]::NewGuid().ToString("N") + ".tmp")
  try {
    $stream = [IO.FileStream]::new(
      $temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
      [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
    try {
      $stream.Write($bytes, 0, $bytes.Length)
      $stream.Flush($true)
    } finally { $stream.Dispose() }
    if (Test-Path -LiteralPath $evidencePath -PathType Leaf) {
      [IO.File]::Replace($temporary, $evidencePath, $backup, $true)
      Remove-Item -LiteralPath $backup -Force
    } else {
      [IO.File]::Move($temporary, $evidencePath)
    }
    $actual = [IO.File]::ReadAllBytes($evidencePath)
    if ((Get-ByteSha256 $bytes) -cne (Get-ByteSha256 $actual) -or
        $bytes.Length -ne $actual.Length) {
      throw "installerEvidenceUnavailable"
    }
  } finally {
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
  }
  return $Document
}

function Write-VirtualDisplaySetupEvidence($Result) {
  $current = New-VirtualDisplaySetupEvidenceProjection $Result
  if (-not (Test-VirtualDisplaySetupEvidenceProjection ([pscustomobject]$current))) {
    throw "installerEvidenceInvalid"
  }
  $existing = Get-ExistingVirtualDisplaySetupEvidence
  $primary = if ($null -ne $existing) { $existing.primary } else { $null }
  if ($null -eq $primary -and [string]$Result.state -ceq "failed") {
    $primary = $current
  }
  $recovery = if ($null -ne $primary -and
      [string]$primary.resultFileSha256 -ceq [string]$current.resultFileSha256) {
    if ($null -ne $existing) { $existing.recovery } else { $null }
  } else { $current }
  $setup = [ordered]@{ primary = $primary; recovery = $recovery }
  $path = Get-InstallerEvidencePath
  $base = $null
  if (Test-Path -LiteralPath $path -PathType Leaf) {
    try {
      $raw = [IO.File]::ReadAllText($path)
      if ([LigaseStrictJson]::HasUniqueProperties($raw)) {
        $candidate = $raw | ConvertFrom-Json
        if ([int]$candidate.schemaVersion -eq 2 -and
            [string]$candidate.candidateSourceHead -ceq (Get-EvidenceSourceHead)) {
          $base = $candidate
        }
      }
    } catch { $base = $null }
  }
  if ($null -eq $base) {
    $base = [pscustomobject]([ordered]@{
      schemaVersion = 2; candidateSourceHead = Get-EvidenceSourceHead
      phase = "virtualDisplaySetup"; success = $null
      resultCode = "virtualDisplaySetupRecorded"; failedField = $null
      components = [pscustomobject]([ordered]@{ virtualDisplay = "pending" })
      timestampUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    })
  }
  $base | Add-Member -NotePropertyName virtualDisplaySetup -NotePropertyValue (
    [pscustomobject]$setup) -Force
  $base.phase = "virtualDisplaySetup"
  $base.success = if ($null -ne $primary) { $false } else {
    [string]$Result.state -ceq "completed"
  }
  $base.resultCode = if ($null -ne $primary) {
    [string]$primary.code
  } else { [string]$Result.code }
  if ($base.PSObject.Properties.Name -contains "failedField") {
    $base.failedField = if ($null -ne $primary) { "virtualDisplay" } else { $null }
  } else {
    $failedFieldValue = if ($null -ne $primary) { "virtualDisplay" } else { $null }
    $base | Add-Member -NotePropertyName failedField -NotePropertyValue (
      $failedFieldValue)
  }
  if ($null -eq $base.components) {
    $base | Add-Member -NotePropertyName components -NotePropertyValue (
      [pscustomobject]([ordered]@{})) -Force
  }
  $componentValue = if ($null -ne $primary) { "failed" } else { "verified" }
  $base.components | Add-Member -NotePropertyName virtualDisplay -NotePropertyValue (
    $componentValue) -Force
  $base.timestampUtc = [DateTime]::UtcNow.ToString(
    "yyyy-MM-ddTHH:mm:ss.fffZ",
    [Globalization.CultureInfo]::InvariantCulture)
  Write-InstallerEvidenceDocument $base
}

function Write-VirtualDisplaySetupLastResortEvidence($Result) {
  $current = New-VirtualDisplaySetupEvidenceProjection $Result
  $existing = Get-ExistingVirtualDisplaySetupEvidence
  $primary = if ($null -ne $existing) { $existing.primary } else { $null }
  if ($null -eq $primary -and [string]$Result.state -ceq "failed") {
    $primary = $current
  }
  $recovery = if ($null -ne $primary -and
      [string]$primary.resultFileSha256 -ceq [string]$current.resultFileSha256) {
    if ($null -ne $existing) { $existing.recovery } else { $null }
  } else { $current }
  $failed = $null -ne $primary
  $document = [ordered]@{
    schemaVersion = 2
    candidateSourceHead = Get-EvidenceSourceHead
    phase = "virtualDisplaySetup"
    success = -not $failed
    resultCode = if ($failed) { [string]$primary.code } else {
      [string]$Result.code
    }
    failedField = if ($failed) { "virtualDisplay" } else { $null }
    components = [ordered]@{
      virtualDisplay = if ($failed) { "failed" } else { "verified" }
    }
    virtualDisplaySetup = [ordered]@{
      primary = $primary
      recovery = $recovery
    }
    persistence = [ordered]@{
      state = "lastResort"
      standardWriter = "failed"
    }
    timestampUtc = [DateTime]::UtcNow.ToString(
      "yyyy-MM-ddTHH:mm:ss.fffZ",
      [Globalization.CultureInfo]::InvariantCulture)
  }
  Write-InstallerEvidenceDocument $document
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

$virtualDisplayPropertyBatchSize = 32
$virtualDisplayPropertyInventoryDeadlineMilliseconds = 10000
$virtualDisplayPropertyCleanupReserveMilliseconds = 1000

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

function Fail-FinalInstallReadback([string]$Field) {
  if (-not $script:finalComponents.Contains($Field)) {
    $Field = "artifacts"
  }
  $script:finalFailedField = $Field
  $script:finalComponents[$Field] = "failed"
  throw "installationFinalReadbackFailed"
}

function Assert-FinalInstallReadback($Manifest, $VirtualDisplayReadback) {
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
  # Virtual Display provisioning is an independent product axis. Core Host
  # finalization never reinterprets or rolls back its typed Setup result.
  $script:finalComponents.virtualDisplay = "independent"
  return [ordered]@{
    dataRoot = [string]$bootstrap.dataRoot
    firewall = $firewall
  }
}

function Get-RunningLigaseProductProcesses {
  $expected = [ordered]@{
    "Ligase Host" = Join-Path $installRoot "Ligase Host.exe"
    "Ligase.Host.Desktop" = Join-Path $installRoot "Desktop\Ligase.Host.Desktop.exe"
    "sunshine" = Join-Path $installRoot "Core\sunshine.exe"
    "Ligase.GameWatcher" = Join-Path $installRoot "Tools\GameWatcher\Ligase.GameWatcher.exe"
  }
  $matches = @()
  foreach ($entry in $expected.GetEnumerator()) {
    foreach ($process in @(Get-Process -Name $entry.Key -ErrorAction SilentlyContinue)) {
      $pidValue = [int]$process.Id
      try {
        $path = [IO.Path]::GetFullPath([string]$process.Path)
        if ($path.Equals(
            [IO.Path]::GetFullPath([string]$entry.Value),
            [StringComparison]::OrdinalIgnoreCase)) {
          $cim = Get-CimInstance Win32_Process -Filter (
            "ProcessId={0}" -f $pidValue) -ErrorAction Stop
          $owner = Invoke-CimMethod -InputObject $cim -MethodName GetOwnerSid `
            -ErrorAction Stop
          if ([uint32]$owner.ReturnValue -ne 0 -or
              [string]::IsNullOrWhiteSpace([string]$owner.Sid)) {
            throw "productProcessOwnerUnavailable"
          }
          $matches += [pscustomobject]@{
            Id = $pidValue
            ParentId = [int]$cim.ParentProcessId
            StartedUtc = $process.StartTime.ToUniversalTime().ToString("o")
            StartFileTimeUtc = $process.StartTime.ToUniversalTime().ToFileTimeUtc()
            SessionId = [int]$process.SessionId
            UserSid = [string]$owner.Sid
            Role = [string]$entry.Key
            Path = $path
          }
        }
      } catch {
        # Exit between enumeration and identity projection is a normal part of
        # the bounded shutdown drain.  Only fail closed when the exact PID is
        # still live but its identity cannot be read.
        $stillRunning = $false
        $fresh = $null
        try {
          $fresh = Get-Process -Id $pidValue -ErrorAction Stop
          $stillRunning = $true
        } catch {
          $stillRunning = $false
        } finally {
          if ($null -ne $fresh) { $fresh.Dispose() }
        }
        if ($stillRunning) { throw "productProcessIdentityUnavailable" }
      } finally {
        $process.Dispose()
      }
    }
  }
  return @($matches | Sort-Object Id)
}

function Get-LigaseRestartManagerState {
  param([int]$IgnoreProcessId = 0)
  $resources = @(Get-LigaseRestartManagerResources)
  if ($resources.Count -eq 0) {
    return [pscustomobject]@{ Code = "completed"; RebootReason = 0; Processes = @() }
  }
  try {
    $readback = [LigaseRestartManager]::GetLockingProcesses([string[]]$resources)
    $locks = @($readback.Processes | Where-Object {
      $IgnoreProcessId -le 0 -or [int]$_.ProcessId -ne $IgnoreProcessId
    } | ForEach-Object {
      $rmProcess = $_
      $pidValue = [int]$rmProcess.ProcessId
      try {
        $process = Get-Process -Id $pidValue -ErrorAction Stop
        try {
          $path = [IO.Path]::GetFullPath([string]$process.Path)
          $role = switch -CaseSensitive ($process.ProcessName) {
            "Ligase Host" { "Ligase Host" }
            "Ligase.Host.Desktop" { "Ligase.Host.Desktop" }
            "sunshine" { "sunshine" }
            "Ligase.GameWatcher" { "Ligase.GameWatcher" }
            default { "RestartManagerLocker" }
          }
          $cim = Get-CimInstance Win32_Process -Filter (
            "ProcessId={0}" -f $pidValue) -ErrorAction Stop
          $owner = Invoke-CimMethod -InputObject $cim -MethodName GetOwnerSid `
            -ErrorAction Stop
          if ([uint32]$owner.ReturnValue -ne 0 -or
              [string]::IsNullOrWhiteSpace([string]$owner.Sid)) {
            throw "restartManagerLockerOwnerUnavailable"
          }
          [pscustomobject]@{
            Id = $pidValue
            ParentId = [int]$cim.ParentProcessId
            StartedUtc = $process.StartTime.ToUniversalTime().ToString("o")
            StartFileTimeUtc = $process.StartTime.ToUniversalTime().ToFileTimeUtc()
            RestartManagerStartFileTimeUtc = [long]$rmProcess.ProcessStartFileTimeUtc
            AppStatus = [uint32]$rmProcess.AppStatus
            SessionId = [int]$process.SessionId
            RestartManagerSessionId = [uint32]$rmProcess.SessionId
            UserSid = [string]$owner.Sid
            Role = $role
            Path = $path
          }
        } finally { $process.Dispose() }
      } catch {
        # The locker exited between RmGetList and identity readback.  A fresh
        # Restart Manager sample decides terminal absence.
      }
    })
    return [pscustomobject]@{
      Code = "completed"
      RebootReason = [uint32]$readback.RebootReason
      Processes = @($locks | Sort-Object Id)
    }
  } catch {
    throw "restartManagerQueryFailed"
  }
}

function Get-LigaseRestartManagerResources {
  return @(
    (Join-Path $installRoot "Ligase Host.exe"),
    (Join-Path $installRoot "Desktop\Ligase.Host.Desktop.exe"),
    (Join-Path $installRoot "Core\sunshine.exe"),
    (Join-Path $installRoot "Tools\GameWatcher\Ligase.GameWatcher.exe")
  ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
}

function Test-ExactProcessLockerSet {
  param([object[]]$Processes, [object]$RestartManager)
  if ([string]$RestartManager.Code -cne "completed" -or
      [uint32]$RestartManager.RebootReason -ne 0 -or
      $Processes.Count -eq 0 -or
      @($RestartManager.Processes).Count -ne $Processes.Count) { return $false }
  $processKeys = @($Processes | ForEach-Object {
    "{0}:{1}" -f [int]$_.Id, [long]$_.StartFileTimeUtc
  } | Sort-Object)
  $lockerKeys = @($RestartManager.Processes | ForEach-Object {
    "{0}:{1}" -f [int]$_.Id, [long]$_.RestartManagerStartFileTimeUtc
  } | Sort-Object)
  return ($processKeys -join ',') -ceq ($lockerKeys -join ',')
}

function Read-AllStableBytes {
  param([IO.FileStream]$Stream)
  $Stream.Position = 0
  $memory = [IO.MemoryStream]::new()
  try { $Stream.CopyTo($memory); return $memory.ToArray() }
  finally { $memory.Dispose(); $Stream.Position = 0 }
}

function Get-LegacyArtifactRole([string]$ProcessRole) {
  switch -CaseSensitive ($ProcessRole) {
    "Ligase Host" { "launcher" }
    "Ligase.Host.Desktop" { "desktop" }
    "sunshine" { "managedCore" }
    "Ligase.GameWatcher" { "gameWatcher" }
    default { $null }
  }
}

function Close-LegacyForceAuthority([object]$Authority) {
  if ($null -eq $Authority) { return }
  foreach ($lease in @($Authority.Leases)) {
    if ($null -ne $lease) { $lease.Dispose() }
  }
}

function Open-LegacyForceAuthority {
  param([object[]]$InitialProcesses, [object[]]$Processes,
    [object]$RestartManager)
  $leases = [Collections.Generic.List[IO.FileStream]]::new()
  try {
    if ([string]$RestartManager.Code -cne "completed" -or
        [uint32]$RestartManager.RebootReason -ne 0 -or
        $Processes.Count -eq 0 -or
        @($RestartManager.Processes).Count -ne $Processes.Count) {
      throw "legacyAuthorityLockerSetMismatch"
    }
    $processIds = @($Processes | ForEach-Object { [int]$_.Id } | Sort-Object)
    $lockerIds = @($RestartManager.Processes | ForEach-Object { [int]$_.Id } | Sort-Object)
    if (($processIds -join ',') -cne ($lockerIds -join ',')) {
      throw "legacyAuthorityLockerSetMismatch"
    }

    $manifestLease = [LigaseFileIdentity]::OpenStableRead($manifestPath)
    $leases.Add($manifestLease)
    $manifestBytes = Read-AllStableBytes $manifestLease
    $manifestSha = Get-ByteSha256 $manifestBytes
    $manifestJson = [Text.UTF8Encoding]::new($false, $true).GetString($manifestBytes)
    if (-not [LigaseStrictJson]::HasUniqueProperties($manifestJson)) {
      throw "legacyAuthorityManifestInvalid"
    }
    $manifest = $manifestJson | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or
        $manifest.installLayout -cne "structured-v1" -or
        $manifest.platform -cne "x64" -or
        $manifest.installMode -cne "packaged" -or
        $manifest.releaseKind -notin @("UnsignedDev", "PublicRelease") -or
        @($manifest.artifacts).Count -ne 4) {
      throw "legacyAuthorityManifestInvalid"
    }
    $expectedRoles = @("desktop", "gameWatcher", "launcher", "managedCore")
    $manifestRoles = @($manifest.artifacts | ForEach-Object { [string]$_.role } | Sort-Object)
    if (($expectedRoles -join ',') -cne ($manifestRoles -join ',')) {
      throw "legacyAuthorityManifestInvalid"
    }

    $initialByRole = @{}
    foreach ($initial in $InitialProcesses) {
      if ($initialByRole.ContainsKey([string]$initial.Role)) {
        throw "legacyAuthorityInitialSetInvalid"
      }
      $initialByRole[[string]$initial.Role] = $initial
    }
    $targets = @()
    foreach ($process in @($Processes | Sort-Object Id)) {
      $role = [string]$process.Role
      $artifactRole = Get-LegacyArtifactRole $role
      if ([string]::IsNullOrWhiteSpace($artifactRole) -or
          -not $initialByRole.ContainsKey($role)) {
        throw "legacyAuthorityRoleInvalid"
      }
      $initial = $initialByRole[$role]
      if ([int]$initial.Id -ne [int]$process.Id -or
          [long]$initial.StartFileTimeUtc -ne [long]$process.StartFileTimeUtc -or
          [int]$initial.ParentId -ne [int]$process.ParentId -or
          [int]$initial.SessionId -ne [int]$process.SessionId -or
          [string]$initial.UserSid -cne [string]$process.UserSid) {
        throw "legacyAuthorityProcessDrift"
      }
      $locker = @($RestartManager.Processes | Where-Object { [int]$_.Id -eq [int]$process.Id })
      if ($locker.Count -ne 1 -or
          [long]$locker[0].RestartManagerStartFileTimeUtc -ne [long]$process.StartFileTimeUtc -or
          [int]$locker[0].RestartManagerSessionId -ne [int]$process.SessionId -or
          [int]$locker[0].ParentId -ne [int]$process.ParentId -or
          [string]$locker[0].UserSid -cne [string]$process.UserSid) {
        throw "legacyAuthorityRestartManagerDrift"
      }
      $artifact = @($manifest.artifacts | Where-Object { [string]$_.role -ceq $artifactRole })
      if ($artifact.Count -ne 1 -or
          [IO.Path]::IsPathRooted([string]$artifact[0].relativePath) -or
          ([string]$artifact[0].relativePath).Contains("..")) {
        throw "legacyAuthorityManifestInvalid"
      }
      $expectedPath = [IO.Path]::GetFullPath((Join-Path $installRoot (
        [string]$artifact[0].relativePath).Replace('/','\')))
      if (-not $expectedPath.Equals([IO.Path]::GetFullPath([string]$process.Path),
          [StringComparison]::OrdinalIgnoreCase)) {
        throw "legacyAuthorityPathMismatch"
      }
      $lease = [LigaseFileIdentity]::OpenStableRead($expectedPath)
      $leases.Add($lease)
      $identity = [LigaseFileIdentity]::GetIdentitySha256($lease.SafeFileHandle)
      $bytes = Read-AllStableBytes $lease
      $sha = Get-ByteSha256 $bytes
      if ($bytes.LongLength -ne [int64]$artifact[0].size -or
          $sha -cne ([string]$artifact[0].signedArtifactSha256).ToLowerInvariant() -or
          [Diagnostics.FileVersionInfo]::GetVersionInfo($expectedPath).FileVersion -cne
            [string]$artifact[0].version) {
        throw "legacyAuthorityArtifactMismatch"
      }
      $signature = Get-AuthenticodeSignature -LiteralPath $expectedPath
      if ($manifest.releaseKind -ceq "PublicRelease") {
        if ($signature.Status -ne "Valid" -or
            $null -eq $signature.SignerCertificate -or
            $signature.SignerCertificate.Subject -cne [string]$artifact[0].signature.signerSubject -or
            $signature.SignerCertificate.Thumbprint -cne [string]$artifact[0].signature.signerThumbprint) {
          throw "legacyAuthoritySignerMismatch"
        }
      } elseif ($artifact[0].signature.status -cne "nonRelease" -or
          $null -ne $artifact[0].signature.signerSubject -or
          $null -ne $artifact[0].signature.signerThumbprint) {
        throw "legacyAuthoritySignerMismatch"
      }
      $targets += [pscustomobject]@{
        Id = [int]$process.Id; ParentId = [int]$process.ParentId
        StartedUtc = [string]$process.StartedUtc
        StartFileTimeUtc = [long]$process.StartFileTimeUtc
        SessionId = [int]$process.SessionId; UserSid = [string]$process.UserSid
        Role = $role; Path = $expectedPath; FileIdentitySha256 = $identity
        ContentSha256 = $sha; Version = [string]$artifact[0].version
        SignerThumbprint = if ($null -eq $signature.SignerCertificate) { $null } else {
          [string]$signature.SignerCertificate.Thumbprint }
        AppStatus = [uint32]$locker[0].AppStatus
      }
    }
    # Parent identity is a freshness invariant, not a guessed topology rule:
    # older generations launched Core/GameWatcher from different owners. Each
    # target's parent PID is frozen in the initial receipt and re-read above.
    return [pscustomobject]@{
      Eligible = $true; Reason = "identityExact"; ManifestSha256 = $manifestSha
      ManifestIdentitySha256 = [LigaseFileIdentity]::GetIdentitySha256(
        $manifestLease.SafeFileHandle)
      Targets = @($targets); Leases = @($leases)
    }
  } catch {
    foreach ($lease in @($leases)) { if ($null -ne $lease) { $lease.Dispose() } }
    return [pscustomobject]@{
      Eligible = $false; Reason = [string]$_.Exception.Message
      ManifestSha256 = $null; ManifestIdentitySha256 = $null
      Targets = @(); Leases = @()
    }
  }
}

function Test-LegacyForceAuthorityCurrent {
  param([object]$Authority)
  if ($null -eq $Authority -or -not [bool]$Authority.Eligible) { return $false }
  $current = @(Get-RunningLigaseProductProcesses)
  # The retained image leases intentionally make this controller a read-only
  # Restart Manager locker. Exclude only this exact PID; every other locker
  # must still equal the force receipt. RmForceShutdown itself registers only
  # the exact RM_UNIQUE_PROCESS targets, so the controller is never targeted.
  $rm = Get-LigaseRestartManagerState -IgnoreProcessId $PID
  if ([string]$rm.Code -cne "completed" -or
      $current.Count -ne @($Authority.Targets).Count -or
      @($rm.Processes).Count -ne @($Authority.Targets).Count) { return $false }
  foreach ($target in @($Authority.Targets)) {
    $process = @($current | Where-Object { [int]$_.Id -eq [int]$target.Id })
    $locker = @($rm.Processes | Where-Object { [int]$_.Id -eq [int]$target.Id })
    if ($process.Count -ne 1 -or $locker.Count -ne 1 -or
        [long]$process[0].StartFileTimeUtc -ne [long]$target.StartFileTimeUtc -or
        [int]$process[0].ParentId -ne [int]$target.ParentId -or
        [int]$process[0].SessionId -ne [int]$target.SessionId -or
        [string]$process[0].UserSid -cne [string]$target.UserSid -or
        [long]$locker[0].RestartManagerStartFileTimeUtc -ne [long]$target.StartFileTimeUtc -or
        [string]$locker[0].UserSid -cne [string]$target.UserSid) { return $false }
    $fresh = $null
    try {
      $fresh = [LigaseFileIdentity]::OpenStableRead([string]$target.Path)
      if ([LigaseFileIdentity]::GetIdentitySha256($fresh.SafeFileHandle) -cne
          [string]$target.FileIdentitySha256 -or
          (Get-ByteSha256 (Read-AllStableBytes $fresh)) -cne
          [string]$target.ContentSha256) { return $false }
    } catch { return $false }
    finally { if ($null -ne $fresh) { $fresh.Dispose() } }
  }
  return $true
}

function Invoke-RestartManagerPrimaryShutdown {
  param([int]$RemainingMilliseconds, [object[]]$Processes, [bool]$Force)
  if ($RemainingMilliseconds -le 0) {
    return [pscustomobject]@{ Code = "deadlineUnavailable"; NativeCode = 1460; Cancelled = $true; ForceUsed = $Force }
  }
  if ($ShutdownValidationBehavior -cne "none") {
    if ($env:LIGASE_SHUTDOWN_VALIDATION_HARNESS -cne "1" -or
        [IO.Path]::GetPathRoot($installRoot) -cne "D:\") {
      return [pscustomobject]@{ Code = "failed"; NativeCode = -1
        Cancelled = $false; ForceUsed = $Force }
    }
    if (-not $Force -and $ShutdownValidationBehavior -like "simulateGraceful351*") {
      return [pscustomobject]@{ Code = "failed"; NativeCode = 351
        Cancelled = $false; ForceUsed = $false }
    }
    if ($Force -and $ShutdownValidationBehavior -ceq "simulateGraceful351Force351") {
      return [pscustomobject]@{ Code = "failed"; NativeCode = 351
        Cancelled = $false; ForceUsed = $true }
    }
    if ($Force -and $ShutdownValidationBehavior -ceq "simulateGraceful351ForceTimeout") {
      return [pscustomobject]@{ Code = "cancelled"; NativeCode = 1460
        Cancelled = $true; ForceUsed = $true }
    }
    if ($Force -and $ShutdownValidationBehavior -ceq "simulateGraceful351ForcePermission") {
      return [pscustomobject]@{ Code = "failed"; NativeCode = 5
        Cancelled = $false; ForceUsed = $true }
    }
    if ($Force -and $ShutdownValidationBehavior -ceq "simulateGraceful351ForceCompleted") {
      return [pscustomobject]@{ Code = "completed"; NativeCode = 0
        Cancelled = $false; ForceUsed = $true }
    }
  }
  try {
    # The forced legacy phase registers only the exact RM_UNIQUE_PROCESS
    # receipts. Registering paths again could admit a new foreign locker in
    # the interval between the final exact-set readback and RmShutdown.
    $resources = if ($Force) { @() } else { @(Get-LigaseRestartManagerResources) }
    $result = [LigaseRestartManager]::ShutdownLockingProcesses(
      [string[]]$resources,
      [int[]]@($Processes | ForEach-Object { [int]$_.Id }),
      [string[]]@($Processes | ForEach-Object { [string]$_.StartedUtc }),
      $RemainingMilliseconds, $Force)
    return [pscustomobject]@{
      Code = if ([int]$result.NativeCode -eq 0 -and -not [bool]$result.Cancelled) {
        "completed"
      } elseif ([bool]$result.Cancelled) { "cancelled" } else { "failed" }
      NativeCode = [int]$result.NativeCode
      Cancelled = [bool]$result.Cancelled
      ForceUsed = [bool]$result.ForceUsed
    }
  } catch {
    return [pscustomobject]@{ Code = "failed"; NativeCode = -1
      Cancelled = $false; ForceUsed = $Force
      Reason = [string]$_.Exception.Message }
  }
}

function ConvertTo-ShutdownProcessSnapshot {
  param([object[]]$Processes, [long]$ElapsedMilliseconds, [object]$RestartManager)
  return [ordered]@{
    elapsedMilliseconds = $ElapsedMilliseconds
    processes = @($Processes | ForEach-Object {
      [ordered]@{
        pid = [int]$_.Id
        parentPid = if ($null -eq $_.ParentId) { $null } else { [int]$_.ParentId }
        startedUtc = if ($null -eq $_.StartedUtc) { $null } else { [string]$_.StartedUtc }
        role = [string]$_.Role
        pathSha256 = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes(
          [IO.Path]::GetFullPath([string]$_.Path).ToLowerInvariant()))
      }
    })
    restartManager = [ordered]@{
      code = [string]$RestartManager.Code
      rebootReason = [uint32]$RestartManager.RebootReason
      processes = @($RestartManager.Processes | ForEach-Object {
        [ordered]@{
          pid = [int]$_.Id
          parentPid = if ($null -eq $_.ParentId) { $null } else { [int]$_.ParentId }
          startedUtc = if ($null -eq $_.StartedUtc) { $null } else { [string]$_.StartedUtc }
          role = [string]$_.Role
          pathSha256 = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes(
            [IO.Path]::GetFullPath([string]$_.Path).ToLowerInvariant()))
        }
      })
    }
  }
}

function Write-ShutdownTerminal {
  param([string]$RequestId, [hashtable]$Terminal)
  $root = if ([string]::IsNullOrWhiteSpace($ShutdownEvidenceRoot)) {
    Join-Path ([Environment]::GetFolderPath("CommonApplicationData")) `
      "Ligase Host\Installer\Shutdown"
  } else {
    if ($env:LIGASE_SHUTDOWN_VALIDATION_HARNESS -cne "1") {
      throw "shutdownEvidenceRootRejected"
    }
    $candidate = [IO.Path]::GetFullPath($ShutdownEvidenceRoot)
    if ([IO.Path]::GetPathRoot($candidate) -cne "D:\") {
      throw "shutdownEvidenceRootRejected"
    }
    $candidate
  }
  [IO.Directory]::CreateDirectory($root) | Out-Null
  $path = Join-Path $root ("shutdown-terminal-{0}.json" -f $RequestId)
  $temp = "$path.$([Guid]::NewGuid().ToString('N')).tmp"
  $bytes = [Text.UTF8Encoding]::new($false).GetBytes(
    ($Terminal | ConvertTo-Json -Depth 12 -Compress))
  try {
    $stream = [IO.FileStream]::new($temp, [IO.FileMode]::CreateNew,
      [IO.FileAccess]::Write, [IO.FileShare]::None, 4096,
      [IO.FileOptions]::WriteThrough)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
    [IO.File]::Move($temp, $path)
  } finally {
    if (Test-Path -LiteralPath $temp -PathType Leaf) {
      [IO.File]::Delete($temp)
    }
  }
  return $path
}

function Request-RunningLigaseProductExit {
  $initial = @(Get-RunningLigaseProductProcesses)
  $initialRm = Get-LigaseRestartManagerState
  $requestId = [Guid]::NewGuid().ToString("N")
  $clock = [Diagnostics.Stopwatch]::StartNew()
  $samples = @((ConvertTo-ShutdownProcessSnapshot $initial 0 $initialRm))
    $client = [pscustomobject]@{
      Code = if ($initial.Count -eq 0) { "shutdownAcknowledged" } else { "desktopCountInvalid" }
      Connected = $false; RequestSent = $false; AckReceived = $false
      DesktopTerminalReceived = $false
      DesktopTerminalState = $null; DesktopTerminalCode = $null
      DesktopCoreProcessStillAlive = $null
  }
  $desktop = @($initial | Where-Object { $_.Role -ceq "Ligase.Host.Desktop" })
  if ($initial.Count -ne 0 -and $desktop.Count -eq 1) {
    $client = [LigaseProductShutdownClient]::Request(
      $installRoot, [int]$desktop[0].Id, $requestId, 7000)
  }
  $gracefulRestartManagerShutdown = [ordered]@{
    eligible = $false; attempted = $false; code = "notRequired"
    nativeCode = $null; cancelled = $false; forceUsed = $false; reason = $null
  }
  $forcedRestartManagerShutdown = [ordered]@{
    eligible = $false; eligibilityReason = "notEvaluated"
    attempted = $false; code = "notRequired"; nativeCode = $null
    cancelled = $false; forceUsed = $false; reason = $null; manifestSha256 = $null
    manifestIdentitySha256 = $null; targets = @()
  }
  $shutdownAccepted = [string]$client.Code -ceq "shutdownAcknowledged"
  if (-not $UserConfirmedClose) {
    $shutdownAccepted = $false
    $client.Code = "userCloseConsentRequired"
  }
  # Give an exit-committed product a short opportunity to release its own
  # files. The product terminal is intent evidence; actual process and locker
  # absence remain installer authority.
  $graceLimit = [Math]::Min(10000, [int]$clock.ElapsedMilliseconds + 1000)
  while ($shutdownAccepted -and $clock.ElapsedMilliseconds -lt $graceLimit) {
    $graceProcesses = @(Get-RunningLigaseProductProcesses)
    $graceRm = Get-LigaseRestartManagerState
    $samples += ConvertTo-ShutdownProcessSnapshot $graceProcesses `
      $clock.ElapsedMilliseconds $graceRm
    if ($graceProcesses.Count -eq 0 -and $graceRm.Processes.Count -eq 0) { break }
    Start-Sleep -Milliseconds 100
  }

  $rmProcesses = @(Get-RunningLigaseProductProcesses)
  $rmState = Get-LigaseRestartManagerState
  $samples += ConvertTo-ShutdownProcessSnapshot $rmProcesses `
    $clock.ElapsedMilliseconds $rmState
  if ($rmProcesses.Count -eq 0 -and $rmState.Processes.Count -eq 0) {
    $shutdownAccepted = $true
  } elseif ($UserConfirmedClose -and $clock.ElapsedMilliseconds -lt 10000) {
    # Restart Manager is the standard primary-installer authority for files
    # that remain occupied after the product-specific graceful request.  It is
    # never replaces Ligase's graceful pipe. It is attempted only against an
    # exact PID/start-time set, and always starts with non-forcing flags.
    $gracefulRestartManagerShutdown.eligible = `
      Test-ExactProcessLockerSet $rmProcesses $rmState
    if ($gracefulRestartManagerShutdown.eligible) {
      $gracefulRestartManagerShutdown.attempted = $true
      $remaining = 10000 - [int]$clock.ElapsedMilliseconds
      $rmShutdown = Invoke-RestartManagerPrimaryShutdown $remaining `
        $rmProcesses $false
      $gracefulRestartManagerShutdown.code = [string]$rmShutdown.Code
      $gracefulRestartManagerShutdown.nativeCode = [int]$rmShutdown.NativeCode
      $gracefulRestartManagerShutdown.cancelled = [bool]$rmShutdown.Cancelled
      $gracefulRestartManagerShutdown.forceUsed = [bool]$rmShutdown.ForceUsed
      $gracefulRestartManagerShutdown.reason = if ($null -eq $rmShutdown.Reason) {
        $null } else { [string]$rmShutdown.Reason }
      $shutdownAccepted = $gracefulRestartManagerShutdown.code -ceq "completed"
    }
  }
  $forceAuthority = $null
  if (-not $shutdownAccepted -and $UserConfirmedClose -and
      $gracefulRestartManagerShutdown.attempted -and
      $gracefulRestartManagerShutdown.code -cne "completed") {
    $forceProcesses = @(Get-RunningLigaseProductProcesses)
    $forceRm = Get-LigaseRestartManagerState
    $samples += ConvertTo-ShutdownProcessSnapshot $forceProcesses `
      $clock.ElapsedMilliseconds $forceRm
    $forceAuthority = Open-LegacyForceAuthority $initial $forceProcesses $forceRm
    $forcedRestartManagerShutdown.eligible = [bool]$forceAuthority.Eligible
    $forcedRestartManagerShutdown.eligibilityReason = [string]$forceAuthority.Reason
    $forcedRestartManagerShutdown.manifestSha256 = $forceAuthority.ManifestSha256
    $forcedRestartManagerShutdown.manifestIdentitySha256 = `
      $forceAuthority.ManifestIdentitySha256
    $forcedRestartManagerShutdown.targets = @($forceAuthority.Targets | ForEach-Object {
      [ordered]@{
        pid = [int]$_.Id
        parentPid = [int]$_.ParentId
        startedUtc = [string]$_.StartedUtc
        role = [string]$_.Role
        pathSha256 = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes(
          [IO.Path]::GetFullPath([string]$_.Path).ToLowerInvariant()))
        fileIdentitySha256 = [string]$_.FileIdentitySha256
        contentSha256 = [string]$_.ContentSha256
        versionSha256 = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes(
          [string]$_.Version))
        signerThumbprintSha256 = if ($null -eq $_.SignerThumbprint) { $null } else {
          Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes([string]$_.SignerThumbprint)) }
        sessionId = [int]$_.SessionId
        userSidSha256 = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes([string]$_.UserSid))
        appStatus = [uint32]$_.AppStatus
      }
    })
    if ($forceAuthority.Eligible -and
        (Test-LegacyForceAuthorityCurrent $forceAuthority)) {
      $forcedRestartManagerShutdown.attempted = $true
      $forceRemaining = 45000 - [int]$clock.ElapsedMilliseconds
      $forced = Invoke-RestartManagerPrimaryShutdown $forceRemaining `
        @($forceAuthority.Targets) $true
      $forcedRestartManagerShutdown.code = [string]$forced.Code
      $forcedRestartManagerShutdown.nativeCode = [int]$forced.NativeCode
      $forcedRestartManagerShutdown.cancelled = [bool]$forced.Cancelled
      $forcedRestartManagerShutdown.forceUsed = [bool]$forced.ForceUsed
      $forcedRestartManagerShutdown.reason = if ($null -eq $forced.Reason) {
        $null } else { [string]$forced.Reason }
      $shutdownAccepted = $forcedRestartManagerShutdown.code -ceq "completed"
    } elseif ($forceAuthority.Eligible) {
      $forcedRestartManagerShutdown.eligible = $false
      $forcedRestartManagerShutdown.eligibilityReason = "legacyAuthorityFinalDrift"
    }
  }
  # The retained image leases prove the exact legacy targets through the RM
  # call. Release them before the independent final process/RM zero readback;
  # otherwise this controller is itself a read-only RM locker.
  if ($null -ne $forceAuthority) {
    Close-LegacyForceAuthority $forceAuthority
    $forceAuthority = $null
  }
  if ($shutdownAccepted) {
    $drainDeadline = if ($forcedRestartManagerShutdown.attempted) { 45000 } else { 10000 }
    while ($clock.ElapsedMilliseconds -lt $drainDeadline) {
      $current = @(Get-RunningLigaseProductProcesses)
      $currentRm = Get-LigaseRestartManagerState
      $samples += ConvertTo-ShutdownProcessSnapshot $current `
        $clock.ElapsedMilliseconds $currentRm
      if ($current.Count -eq 0 -and $currentRm.Processes.Count -eq 0) { break }
      Start-Sleep -Milliseconds 100
    }
  }
  $final = @(Get-RunningLigaseProductProcesses)
  $finalRm = Get-LigaseRestartManagerState
  $samples += ConvertTo-ShutdownProcessSnapshot $final $clock.ElapsedMilliseconds $finalRm
  $code = if (-not $shutdownAccepted) {
    if ($forcedRestartManagerShutdown.attempted) { "restartManagerForcedShutdownFailed" }
    elseif ($gracefulRestartManagerShutdown.attempted -and
        -not $forcedRestartManagerShutdown.eligible) { "legacyForceIneligible" }
    elseif ($gracefulRestartManagerShutdown.attempted) { "restartManagerShutdownFailed" } else {
    [string]$client.Code
    }
  } elseif ($final.Count -ne 0) { "shutdownResidualProcesses"
  } elseif ($finalRm.Processes.Count -ne 0) { "restartManagerResidualLocks"
  } else { "productStopped" }
  $success = $code -ceq "productStopped"
  $terminal = [ordered]@{
    schemaVersion = 3
    operation = "close"
    state = if ($success) { "completed" } else { "failed" }
    code = $code
    requestIdSha256 = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes($requestId))
    pipe = [ordered]@{
      connected = [bool]$client.Connected
      requestSent = [bool]$client.RequestSent
      ackReceived = [bool]$client.AckReceived
      desktopTerminalReceived = [bool]$client.DesktopTerminalReceived
      desktopTerminalState = if ($null -eq $client.DesktopTerminalState) {
        $null
      } else { [string]$client.DesktopTerminalState }
      desktopTerminalCode = if ($null -eq $client.DesktopTerminalCode) {
        $null
      } else { [string]$client.DesktopTerminalCode }
      desktopCleanupState = if ($null -eq $client.DesktopCleanupState) {
        $null
      } else { [string]$client.DesktopCleanupState }
      desktopCoreStopCode = if ($null -eq $client.DesktopCoreStopCode) {
        $null
      } else { [string]$client.DesktopCoreStopCode }
      desktopCoreProcessStillAlive = if (
          $null -eq $client.DesktopCoreProcessStillAlive) {
        $null
      } else { [bool]$client.DesktopCoreProcessStillAlive }
    }
    gracefulRestartManagerShutdown = $gracefulRestartManagerShutdown
    forcedRestartManagerShutdown = $forcedRestartManagerShutdown
    programWriteCalls = 0
    initial = $samples[0]
    samples = @($samples)
    finalResidual = ConvertTo-ShutdownProcessSnapshot $final `
      $clock.ElapsedMilliseconds $finalRm
    writtenUtc = [DateTime]::UtcNow.ToString("o")
  }
  try { $terminalPath = Write-ShutdownTerminal $requestId $terminal }
  finally { Close-LegacyForceAuthority $forceAuthority }
  return [pscustomobject]@{ Success = $success; Code = $code; TerminalPath = $terminalPath }
}

function Write-RunningLigaseProductQueryTerminal {
  param([object[]]$Processes)
  $restartManager = Get-LigaseRestartManagerState
  $requestId = [Guid]::NewGuid().ToString("N")
  $code = if ($Processes.Count -eq 0 -and
      $restartManager.Processes.Count -eq 0) { "productNotRunning" } else {
    "productRunning"
  }
  $snapshot = ConvertTo-ShutdownProcessSnapshot $Processes 0 $restartManager
  $terminal = [ordered]@{
    schemaVersion = 2
    operation = "query"
    state = "completed"
    code = $code
    requestIdSha256 = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes($requestId))
    pipe = [ordered]@{
      connected = $false
      requestSent = $false
      ackReceived = $false
      desktopTerminalReceived = $false
    }
    initial = $snapshot
    samples = @($snapshot)
    finalResidual = $snapshot
    writtenUtc = [DateTime]::UtcNow.ToString("o")
  }
  $terminalPath = Write-ShutdownTerminal $requestId $terminal
  return [pscustomobject]@{ Code = $code; TerminalPath = $terminalPath }
}

function Invoke-LegacyForceEligibilityDryRun {
  $requestId = [Guid]::NewGuid().ToString("N")
  $processes = @(Get-RunningLigaseProductProcesses)
  $restartManager = Get-LigaseRestartManagerState
  $authority = $null
  try {
    $authority = Open-LegacyForceAuthority $processes $processes $restartManager
    $current = [bool]$authority.Eligible -and
      (Test-LegacyForceAuthorityCurrent $authority)
    $wouldBeEligible = [bool]$authority.Eligible -and $current
    $reason = if (-not [bool]$authority.Eligible) {
      [string]$authority.Reason
    } elseif (-not $current) { "legacyAuthorityFinalDrift" } else { "identityExact" }
    $terminal = [ordered]@{
      schemaVersion = 1
      operation = "legacyForceEligibilityDryRun"
      state = "completed"
      code = if ($wouldBeEligible) { "liveLegacyForceEligible" } else {
        "liveLegacyForceIneligible" }
      userConsentSimulated = $true
      forcedAttempted = $false
      rmShutdownCalls = 0
      programWriteCalls = 0
      wouldBeForcedEligible = $wouldBeEligible
      eligibilityReason = $reason
      manifestSha256 = $authority.ManifestSha256
      manifestIdentitySha256 = $authority.ManifestIdentitySha256
      processCount = $processes.Count
      restartManagerLockerCount = @($restartManager.Processes).Count
      restartManagerRebootReason = [uint32]$restartManager.RebootReason
      targets = @($authority.Targets | ForEach-Object {
        [ordered]@{
          pid = [int]$_.Id
          parentPid = [int]$_.ParentId
          startedUtc = [string]$_.StartedUtc
          role = [string]$_.Role
          pathSha256 = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes(
            [IO.Path]::GetFullPath([string]$_.Path).ToLowerInvariant()))
          fileIdentitySha256 = [string]$_.FileIdentitySha256
          contentSha256 = [string]$_.ContentSha256
          versionSha256 = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes(
            [string]$_.Version))
          signerThumbprintSha256 = if ($null -eq $_.SignerThumbprint) { $null } else {
            Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes(
              [string]$_.SignerThumbprint)) }
          sessionId = [int]$_.SessionId
          userSidSha256 = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes(
            [string]$_.UserSid))
          appStatus = [uint32]$_.AppStatus
        }
      })
      writtenUtc = [DateTime]::UtcNow.ToString("o")
    }
    $terminalPath = Write-ShutdownTerminal $requestId $terminal
    return [pscustomobject]@{
      Success = $wouldBeEligible
      Code = [string]$terminal.code
      TerminalPath = $terminalPath
    }
  } finally {
    Close-LegacyForceAuthority $authority
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
    $uninstallEvidenceValid = if ($EvidencePhase -ceq "uninstalling") {
      $EvidenceSuccess -ceq "unknown" -and
      $EvidenceResultCode -ceq "uninstallStarted" -and
      $EvidenceUninstallDisposition -cne "none" -and
      $EvidenceUninstallState -ceq "pending"
    } elseif ($EvidencePhase -ceq "uninstalled") {
      $EvidenceSuccess -ceq "true" -and
      $EvidenceResultCode -ceq "uninstalled" -and
      $EvidenceUninstallDisposition -cne "none" -and
      $EvidenceUninstallState -in @("preserved", "quarantined")
    } else {
      $EvidenceUninstallDisposition -ceq "none" -and
      $EvidenceUninstallState -ceq "notAttempted"
    }
    if (-not $uninstallEvidenceValid) { throw "installerEvidenceInvalid" }
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
  if ($Action -eq "QueryRunningProduct") {
    $running = @(Get-RunningLigaseProductProcesses)
    $query = Write-RunningLigaseProductQueryTerminal $running
    Write-Outcome ([string]$query.Code) $true
    exit 0
  }
  if ($Action -eq "EvaluateLegacyForceEligibility") {
    $eligibility = Invoke-LegacyForceEligibilityDryRun
    Write-Outcome ([string]$eligibility.Code) ([bool]$eligibility.Success)
    if ([bool]$eligibility.Success) { exit 0 } else { exit 10 }
  }
  if ($Action -eq "CloseRunningProduct") {
    $shutdown = Request-RunningLigaseProductExit
    if ([bool]$shutdown.Success) {
      Write-Outcome "productStopped" $true
      exit 0
    }
    Write-Outcome ([string]$shutdown.Code) $false
    exit 10
  }
  $manifest = Read-Manifest
  if ($Action -ceq "ValidateVirtualDisplayResultContract") {
    if ($env:LIGASE_INSTALL_VALIDATION_HARNESS -cne "1" -or
        [string]::IsNullOrWhiteSpace($ValidationRoot) -or
        [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($ValidationRoot)) -cne "D:\" -or
        [string]::IsNullOrWhiteSpace($VirtualDisplayResultPath)) {
      throw "virtualDisplaySetupValidationRejected"
    }
    $validationRootFull = [IO.Path]::GetFullPath($ValidationRoot)
    if ($validationRootFull -cne $installRoot) {
      throw "virtualDisplaySetupValidationRejected"
    }
    $resultFull = [IO.Path]::GetFullPath($VirtualDisplayResultPath)
    if ((Split-Path -Parent $resultFull) -cne $validationRootFull -or
        -not (Test-Path -LiteralPath $resultFull -PathType Leaf)) {
      throw "virtualDisplaySetupValidationRejected"
    }
    $raw = [IO.File]::ReadAllText($resultFull)
    if (-not [LigaseStrictJson]::HasUniqueProperties($raw)) {
      throw "virtualDisplaySetupResultInvalid"
    }
    Assert-VirtualDisplaySetupResultContract ($raw | ConvertFrom-Json) $manifest
    [Console]::Out.WriteLine('{"code":"virtualDisplayResultContractAccepted"}')
    exit 0
  }
  $artifacts = Test-Artifacts $manifest
  $script:finalComponents.artifacts = "verified"
  if ($Action -in @("InstallVirtualDisplay", "UninstallVirtualDisplay")) {
    try {
      $setupOperation = if ($Action -ceq "InstallVirtualDisplay") {
        "provision"
      } else { "uninstall" }
      $setupResult = Invoke-VirtualDisplaySetup $manifest $setupOperation
      try { $null = Write-VirtualDisplaySetupEvidence $setupResult }
      catch {
        try { $null = Write-VirtualDisplaySetupLastResortEvidence $setupResult }
        catch { throw "installerEvidenceUnavailable" }
      }
      $success = [string]$setupResult.state -ceq "completed" -and
        [string]$setupResult.code -in @(
          "installed", "alreadyInstalled", "uninstalled", "alreadyAbsent",
          "uninstalledLegacyPackageRetained",
          "uninstalledSharedPackageRetained")
      $summary = [ordered]@{
        schemaVersion = 1
        code = [string]$setupResult.code
        state = [string]$setupResult.state
        success = $success
      }
      $summary = $summary | ConvertTo-Json -Compress
      [Console]::Out.WriteLine($summary)
      if ($success) { exit 0 }
      exit 20
    } catch {
      $failure = [ordered]@{
        schemaVersion = 1
        code = "virtualDisplaySetupUnavailable"
        state = "failed"
        success = $false
      }
      [Console]::Out.WriteLine((ConvertTo-Json $failure -Compress))
      exit 20
    }
  }
  $virtualDisplay = [ordered]@{
    state = "independent"
    machineCode = "managedByVirtualDisplaySetup"
  }
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
      $final = Assert-FinalInstallReadback $manifest $null
      $EvidencePhase = "succeeded"
      $EvidenceSuccess = "true"
      $EvidenceResultCode = "installed"
      $EvidenceFirewall = "configured"
      $EvidenceInstallResidue = "nonEmpty"
      $EvidenceDataRootResidue = "nonEmpty"
      $script:shortcutRollback = $null
      Remove-InstallTransaction
      # NSIS intentionally performs a byte-exact final success comparison.
      # Windows PowerShell does not preserve ordinary hashtable insertion
      # order, so keep this terminal projection explicitly ordered.
      Write-Outcome "installationFinalized" $true ([ordered]@{
        dataRootState = "existing"
        firewallState = [string]$final.firewall.state
      })
      exit 0
    } catch {
      if ($script:finalFailedField -ceq "none") {
        $script:finalFailedField = "artifacts"
      }
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
      Write-Outcome "installationFinalReadbackFailed" $false
      exit 10
    }
  }

  if ($Action -eq "DryRun") {
    $existingBootstrap = Read-ValidBootstrap
    Write-Outcome "dryRunReady" $true @{
      installMode = "packaged"
      artifacts = $artifacts
      virtualDisplay = $virtualDisplay
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
      restartRequired = $false
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
    Write-Outcome "uninstalled" $true ([ordered]@{
      dataRootState = $dataRootState
      ownedFirewallRulesRemoved = [bool]$ConfigureFirewall
    })
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
    firewall = $firewallReadback
    encoder = $encoder
    dataRootState = $bootstrapState
    restartRequired = $false
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
    "virtualDisplayInstallerToolUnavailable",
    "virtualDisplayInstallerOutputInvalid",
    "virtualDisplayInstallerUnavailable",
    "virtualDisplayInstallerTimeout",
    "virtualDisplayInstallerCleanupFailed",
    "virtualDisplayInstallerOutputUnavailable",
    "virtualDisplayInstallerOutputOverflow",
    "virtualDisplayCertificateRootFailed",
    "virtualDisplayCertificatePublisherFailed",
    "virtualDisplayDeviceRemoveFailed",
    "virtualDisplayDeviceRemoveFallbackFailed",
    "virtualDisplayDeviceRemoveReadbackFailed",
    "virtualDisplayDeviceRemoveSettleFailed",
    "virtualDisplayDeviceZeroProofFailed",
    "virtualDisplayDeviceCreateFailed",
    "virtualDisplayDriverPackageInstallFailed",
    "virtualDisplayReadbackFailed",
    "virtualDisplayMarkerCommitFailed",
    "virtualDisplayRollbackFailed",
    "virtualDisplayOwnershipInvalid",
    "virtualDisplayUninstallFailed",
    "virtualDisplaySetupResultInvalid",
    "virtualDisplaySetupValidationRejected",
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
