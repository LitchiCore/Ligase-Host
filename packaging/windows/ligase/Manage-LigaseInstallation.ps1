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
    "ValidateVirtualDisplayReadback",
    "ValidateVirtualDisplayNativeInventoryHelper",
    "ValidateVirtualDisplayRemovalReconciliation",
    "ValidateVirtualDisplayTerminalReadback",
    "ValidateVirtualDisplayTrustedPnPUtil",
    "ValidateVirtualDisplayDiagnosticProjection",
    "ValidateVirtualDisplayInstallerProcess",
    "ValidateVirtualDisplayMarkerTransaction",
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
  [string]$ValidationRoot,
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
  [string]$VirtualDisplayOutcome = "unknown",
  [string]$VirtualDisplayDiagnosticToken
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
$script:virtualDisplayDiagnostic = $null
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

    public static string GetTrustedPnPUtilPath()
    {
        var buffer = new System.Text.StringBuilder(32768);
        uint length = GetSystemDirectoryW(buffer, (uint)buffer.Capacity);
        if (length == 0 || length >= buffer.Capacity)
            throw new IOException("virtualDisplayTrustedToolUnavailable");
        string systemDirectory = Path.GetFullPath(buffer.ToString());
        var directory = new DirectoryInfo(systemDirectory);
        if (!directory.Exists ||
            (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("virtualDisplayTrustedToolUnavailable");
        string candidate = Path.GetFullPath(
            Path.Combine(systemDirectory, "pnputil.exe"));
        if (!string.Equals(Path.GetDirectoryName(candidate), systemDirectory,
                StringComparison.OrdinalIgnoreCase))
            throw new IOException("virtualDisplayTrustedToolUnavailable");
        var file = new FileInfo(candidate);
        if (!file.Exists || (file.Attributes & FileAttributes.Directory) != 0 ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("virtualDisplayTrustedToolUnavailable");
        using (var stream = new FileStream(
            candidate, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete))
        {
            var final = new System.Text.StringBuilder(32768);
            uint finalLength = GetFinalPathNameByHandleW(
                stream.SafeFileHandle, final, (uint)final.Capacity, 0);
            if (finalLength == 0 || finalLength >= final.Capacity)
                throw new IOException("virtualDisplayTrustedToolUnavailable");
            string finalPath = final.ToString();
            if (finalPath.StartsWith(@"\\?\", StringComparison.Ordinal))
                finalPath = finalPath.Substring(4);
            if (!string.Equals(
                    Path.GetFullPath(finalPath), candidate,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("virtualDisplayTrustedToolUnavailable");
            BY_HANDLE_FILE_INFORMATION information;
            if (!GetFileInformationByHandle(
                    stream.SafeFileHandle, out information) ||
                information.FileIndexHigh == 0 && information.FileIndexLow == 0)
                throw new IOException("virtualDisplayTrustedToolUnavailable");
        }
        return candidate;
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
            inheritedHandleList = Marshal.AllocHGlobal(IntPtr.Size * 2);
            Marshal.WriteIntPtr(inheritedHandleList, 0, stdoutWrite);
            Marshal.WriteIntPtr(
                inheritedHandleList, IntPtr.Size, stderrWrite);
            if (!UpdateProcThreadAttribute(
                attributeList, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                inheritedHandleList,
                new UIntPtr(unchecked((uint)(IntPtr.Size * 2))),
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
  if ($Action -eq "ValidateVirtualDisplayDiagnosticProjection" -and
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

function Get-VirtualDisplayInventoryHelperPath {
  if ($Action -ceq "ValidateVirtualDisplayNativeInventoryHelper" -and
      $env:LIGASE_INSTALL_VALIDATION_HARNESS -ceq "1" -and
      $env:LIGASE_VDISPLAY_INVENTORY_HELPER_VALIDATION -ceq "1") {
    $validationPath = [IO.Path]::GetFullPath(
      [string]$env:LIGASE_VDISPLAY_INVENTORY_HELPER_PATH)
    if (-not $validationPath.StartsWith(
          "D:\", [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $validationPath -PathType Leaf) -or
        [bool]((Get-Item -LiteralPath $validationPath -Force).Attributes -band
          [IO.FileAttributes]::ReparsePoint)) {
      throw "virtualDisplayReadbackFailed"
    }
    return $validationPath
  }
  $path = Join-Path $installRoot (
    "Deployment\Ligase.VirtualDisplay.InventoryHelper.exe")
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "virtualDisplayReadbackFailed"
  }
  $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
  $matches = @($manifest.privilegedHelpers | Where-Object {
    [string]$_.relativePath -ceq (
      "Deployment/Ligase.VirtualDisplay.InventoryHelper.exe")
  })
  $expectedHash = if ($matches.Count -eq 1) {
    [string]$matches[0].signedArtifactSha256
  } else { "" }
  if ($matches.Count -ne 1 -or $expectedHash -notmatch '^[0-9a-f]{64}$' -or
      (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -cne
        $expectedHash.ToUpperInvariant()) {
    throw "virtualDisplayReadbackFailed"
  }
  return $path
}

function Invoke-VirtualDisplayInventoryHelper {
  $script:virtualDisplayNativeInventoryValidationStage = "resolve"
  $script:virtualDisplayNativeInventoryFailureReason = "resolve"
  $script:virtualDisplayNativeInventoryValidationExitCode = -1
  $script:virtualDisplayNativeInventoryValidationStderrLength = -1
  $script:virtualDisplayNativeInventoryCleanupState = "notRequired"
  $script:virtualDisplayNativeInventoryRootPidZero = $true
  $script:virtualDisplayNativeInventoryJobActiveProcesses = 0
  $script:virtualDisplayNativeInventoryStdoutClosed = $false
  $script:virtualDisplayNativeInventoryStderrClosed = $false
  $totalClock = [Diagnostics.Stopwatch]::StartNew()
  $helper = Get-VirtualDisplayInventoryHelperPath
  $commandLine = '"' + $helper + '" --inventory'
  $job = $null
  try {
    $script:virtualDisplayNativeInventoryValidationStage = "start"
    $script:virtualDisplayNativeInventoryFailureReason = "start"
    $startFault = if (
        $Action -ceq "ValidateVirtualDisplayNativeInventoryHelper" -and
        $env:LIGASE_VDISPLAY_INVENTORY_HELPER_BEHAVIOR -ceq "startRetain") {
      "retain"
    } else { "none" }
    $startCleanup = [Math]::Max(1, [Math]::Min(1000,
      10000 - [int]$totalClock.ElapsedMilliseconds))
    $job = [LigaseJobProcess]::StartExact(
      $helper, $commandLine, (Split-Path -Parent $helper), $startFault,
      $startCleanup)
  } catch {
    if ([LigaseJobProcess]::AuthorityRetained) {
      $remaining = [Math]::Max(1, [Math]::Min(5000,
        10000 - [int]$totalClock.ElapsedMilliseconds))
      $null = [LigaseJobProcess]::SecondaryContainment("none", $remaining)
    }
    $closed = [LigaseJobProcess]::LastCleanupProven -or
      [LigaseJobProcess]::SecondaryCompleted
    $script:virtualDisplayNativeInventoryCleanupState = if ($closed) {
      "completed"
    } else { "failed" }
    $script:virtualDisplayNativeInventoryRootPidZero =
      $closed -and [LigaseJobProcess]::LastCleanupPid -eq 0
    $script:virtualDisplayNativeInventoryJobActiveProcesses = if ($closed) {
      0
    } else { -1 }
    $script:virtualDisplayNativeInventoryStdoutClosed = $closed
    $script:virtualDisplayNativeInventoryStderrClosed = $closed
    if (-not $closed) {
      $script:virtualDisplayNativeInventoryFailureReason = "cleanup"
    }
    throw "virtualDisplayReadbackFailed"
  }
  try {
    $script:virtualDisplayNativeInventoryValidationStage = "capture"
    $script:virtualDisplayNativeInventoryFailureReason = "timeout"
    $stdoutBytes = [byte[]]::new(65536)
    $stderrBytes = [byte[]]::new(65536)
    $stdoutBuffer = [byte[]]::new(4096)
    $stderrBuffer = [byte[]]::new(4096)
    $stdoutLength = 0
    $stderrLength = 0
    $stdoutClosed = $false
    $stderrClosed = $false
    $overflow = $false
    $pipeFault = $false
    $stdoutTask = $job.StandardOutput.ReadAsync(
      $stdoutBuffer, 0, $stdoutBuffer.Length)
    $stderrTask = $job.StandardError.ReadAsync(
      $stderrBuffer, 0, $stderrBuffer.Length)
    if ($Action -ceq "ValidateVirtualDisplayNativeInventoryHelper" -and
        $env:LIGASE_VDISPLAY_INVENTORY_HELPER_BEHAVIOR -ceq "pipeFault") {
      $stdoutTask = [Threading.Tasks.Task[int]]::FromException(
        [IO.IOException]::new("closedValidationPipeFault"))
    }
    $clock = $totalClock
    while (-not ($job.HasNoActiveProcesses() -and
        $stdoutClosed -and $stderrClosed)) {
      foreach ($streamName in @("stdout", "stderr")) {
        $task = if ($streamName -ceq "stdout") { $stdoutTask } else { $stderrTask }
        if (-not $task.IsCompleted) { continue }
        try { $count = $task.GetAwaiter().GetResult() } catch {
          $pipeFault = $true
          break
        }
        if ($count -eq 0) {
          if ($streamName -ceq "stdout") { $stdoutClosed = $true } else { $stderrClosed = $true }
          continue
        }
        if ($streamName -ceq "stdout") {
          if ($stdoutLength + $count -gt $stdoutBytes.Length) { $overflow = $true; break }
          [Array]::Copy($stdoutBuffer, 0, $stdoutBytes, $stdoutLength, $count)
          $stdoutLength += $count
          $stdoutTask = $job.StandardOutput.ReadAsync(
            $stdoutBuffer, 0, $stdoutBuffer.Length)
        } else {
          if ($stderrLength + $count -gt $stderrBytes.Length) { $overflow = $true; break }
          [Array]::Copy($stderrBuffer, 0, $stderrBytes, $stderrLength, $count)
          $stderrLength += $count
          $stderrTask = $job.StandardError.ReadAsync(
            $stderrBuffer, 0, $stderrBuffer.Length)
        }
      }
      if ($overflow -or $pipeFault -or $clock.ElapsedMilliseconds -ge 9000) { break }
      Start-Sleep -Milliseconds 10
    }
    if ($overflow -or $pipeFault -or -not ($job.HasNoActiveProcesses() -and
        $stdoutClosed -and $stderrClosed)) {
      $script:virtualDisplayNativeInventoryFailureReason = if ($overflow) {
        "overflow"
      } elseif ($pipeFault) { "pipe" } else { "timeout" }
      $terminated = $job.Terminate()
      $remaining = [Math]::Max(0,
        10000 - [int]$clock.ElapsedMilliseconds)
      $rootExited = $remaining -gt 0 -and $job.WaitForRoot($remaining)
      while ($clock.ElapsedMilliseconds -lt 10000 -and
          -not ($stdoutClosed -and $stderrClosed)) {
        foreach ($streamName in @("stdout", "stderr")) {
          if (($streamName -ceq "stdout" -and $stdoutClosed) -or
              ($streamName -ceq "stderr" -and $stderrClosed)) { continue }
          $task = if ($streamName -ceq "stdout") { $stdoutTask } else { $stderrTask }
          if (-not $task.IsCompleted) { continue }
          try { $count = $task.GetAwaiter().GetResult() } catch {
            try {
              if ($streamName -ceq "stdout") {
                $stdoutTask = $job.StandardOutput.ReadAsync(
                  $stdoutBuffer, 0, $stdoutBuffer.Length)
              } else {
                $stderrTask = $job.StandardError.ReadAsync(
                  $stderrBuffer, 0, $stderrBuffer.Length)
              }
            } catch {}
            continue
          }
          if ($count -eq 0) {
            if ($streamName -ceq "stdout") { $stdoutClosed = $true }
            else { $stderrClosed = $true }
          } elseif ($streamName -ceq "stdout") {
            $stdoutTask = $job.StandardOutput.ReadAsync(
              $stdoutBuffer, 0, $stdoutBuffer.Length)
          } else {
            $stderrTask = $job.StandardError.ReadAsync(
              $stderrBuffer, 0, $stderrBuffer.Length)
          }
        }
        if (-not ($stdoutClosed -and $stderrClosed)) {
          Start-Sleep -Milliseconds 10
        }
      }
      $jobEmpty = $job.HasNoActiveProcesses()
      $cleanup = $terminated -and $rootExited -and $jobEmpty -and
        $stdoutClosed -and $stderrClosed -and
        $clock.ElapsedMilliseconds -le 10000
      $script:virtualDisplayNativeInventoryStdoutClosed = $stdoutClosed
      $script:virtualDisplayNativeInventoryStderrClosed = $stderrClosed
      $script:virtualDisplayNativeInventoryCleanupState = if ($cleanup) {
        "completed"
      } else { "failed" }
      $script:virtualDisplayNativeInventoryRootPidZero = $cleanup
      $script:virtualDisplayNativeInventoryJobActiveProcesses = if ($cleanup) {
        0
      } else { -1 }
      if (-not $cleanup) {
        $script:virtualDisplayNativeInventoryFailureReason = "cleanup"
        throw "virtualDisplayReadbackFailed"
      }
      throw "virtualDisplayReadbackFailed"
    }
    $script:virtualDisplayNativeInventoryCleanupState = "completed"
    $script:virtualDisplayNativeInventoryStdoutClosed = $stdoutClosed
    $script:virtualDisplayNativeInventoryStderrClosed = $stderrClosed
    $process = $job.Process
    $script:virtualDisplayNativeInventoryValidationStage = "exit"
    $script:virtualDisplayNativeInventoryValidationExitCode =
      [int]$process.ExitCode
    $script:virtualDisplayNativeInventoryValidationStderrLength = $stderrLength
    if ($script:virtualDisplayNativeInventoryValidationExitCode -ne 0 -or
        $stderrLength -ne 0) {
      $script:virtualDisplayNativeInventoryFailureReason = if (
          $script:virtualDisplayNativeInventoryValidationExitCode -ne 0) {
        "nativeExit"
      } else { "stderr" }
      throw "virtualDisplayReadbackFailed"
    }
    try {
      $script:virtualDisplayNativeInventoryValidationStage = "decode"
      $script:virtualDisplayNativeInventoryFailureReason = "utf8"
      $stdout = [Text.UTF8Encoding]::new($false, $true).GetString(
        $stdoutBytes, 0, $stdoutLength)
    } catch { throw "virtualDisplayReadbackFailed" }
    $script:virtualDisplayNativeInventoryFailureReason = "json"
    if (-not [LigaseStrictJson]::HasUniqueProperties($stdout)) {
      throw "virtualDisplayReadbackFailed"
    }
    $script:virtualDisplayNativeInventoryValidationStage = "schema"
    $script:virtualDisplayNativeInventoryFailureReason = "schema"
    $document = $stdout | ConvertFrom-Json
    Assert-ClosedProperties $document @("schemaVersion", "state", "devices") (
      "virtualDisplayInventory")
    if ($document.schemaVersion -ne 1 -or
        [string]$document.state -cne "available" -or
        $document.devices -isnot [array] -or $document.devices.Count -gt 16) {
      $script:virtualDisplayNativeInventoryFailureReason = "result"
      throw "virtualDisplayReadbackFailed"
    }
    $seen = [Collections.Generic.HashSet[string]]::new(
      [StringComparer]::OrdinalIgnoreCase)
    foreach ($device in @($document.devices)) {
      Assert-ClosedProperties $device @(
        "instanceId", "present", "status", "driverInf") (
          "virtualDisplayInventoryDevice")
      if ([string]::IsNullOrWhiteSpace([string]$device.instanceId) -or
          $device.present -isnot [bool] -or
          [string]$device.status -cnotin @("OK", "Problem", "Unknown") -or
          $device.driverInf -isnot [string] -or
          -not $seen.Add([string]$device.instanceId)) {
        $script:virtualDisplayNativeInventoryFailureReason = "result"
        throw "virtualDisplayReadbackFailed"
      }
    }
    $script:virtualDisplayNativeInventoryValidationStage = "completed"
    $script:virtualDisplayNativeInventoryFailureReason = "none"
    return @($document.devices | ForEach-Object {
      [ordered]@{
        instanceId = [string]$_.instanceId
        hardwareIds = @("root\sudomaker\sudovda")
        status = [string]$_.status
        present = [bool]$_.present
        driverInf = [string]$_.driverInf
      }
    })
  } finally {
    if ($null -ne $job) { $job.Dispose() }
  }
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

function Get-VirtualDisplayDiagnosticPath {
  return Join-Path (Split-Path -Parent (Get-InstallerEvidencePath)) (
    "virtual-display-outcome.json")
}

function New-VirtualDisplayDiagnostic([string]$ResultCode, [bool]$Success) {
  return [ordered]@{
    schemaVersion = 1
    candidateSourceHead = Get-EvidenceSourceHead
    writtenUtc = [DateTime]::UtcNow.ToString(
      "O", [Globalization.CultureInfo]::InvariantCulture)
    resultCode = $ResultCode
    success = $Success
    installStage = [string]$script:virtualDisplayInstallStage
    readbackCode = [string]$script:virtualDisplayReadbackCode
    childExitCode = [int]$script:virtualDisplayChildExit
    removeExitCode = [int]$script:virtualDisplayRemoveExit
    removeCount = [int]$script:virtualDisplayRemoveCount
    cleanupState = [string]$script:virtualDisplayProcessCleanup
    markerStage = [string]$script:virtualDisplayMarkerStage
    observedDeviceCount = [int]$script:virtualDisplayObservedDeviceCount
    presentDeviceCount = [int]$script:virtualDisplayPresentDeviceCount
    uniqueDeviceIdsSha256 =
      [string]$script:virtualDisplayUniqueDeviceIdsSha256
    driverBindingVerified =
      [bool]$script:virtualDisplayDriverBindingVerified
    fallbackAttempted = [bool]$script:virtualDisplayFallbackAttempted
    fallbackExitCode = [int]$script:virtualDisplayFallbackExitCode
    fallbackStage = [string]$script:virtualDisplayFallbackStage
    fallbackReason = [string]$script:virtualDisplayFallbackReason
    deviceRecovery = [string]$script:virtualDisplayDeviceRecovery
    residualDeviceState = [string]$script:virtualDisplayResidualState
    compensationState = [string]$script:virtualDisplayCompensation
    compensationFailureReason =
      [string]$script:virtualDisplayCompensationFailureReason
    terminalReadbackState =
      [string]$script:virtualDisplayTerminalReadbackState
    terminalReadbackReason =
      [string]$script:virtualDisplayTerminalReadbackReason
    createInvocationCount = [int]$script:virtualDisplayCreateInvocationCount
    createInvocationIdSha256 =
      [string]$script:virtualDisplayCreateInvocationIdSha256
    preCreateIdentitySha256 =
      [string]$script:virtualDisplayPreCreateIdentitySha256
    postCreateIdentitySha256 =
      [string]$script:virtualDisplayPostCreateIdentitySha256
    postCreateIdentityState =
      [string]$script:virtualDisplayPostCreateIdentityState
    postCreateIdentityReason =
      [string]$script:virtualDisplayPostCreateIdentityReason
    inventoryStage = [string]$script:virtualDisplayInventoryStage
    inventoryFailureStage =
      [string]$script:virtualDisplayInventoryFailureStage
    inventoryOutputReason =
      [string]$script:virtualDisplayInventoryOutputReason
    inventoryNativeExitCode =
      [int]$script:virtualDisplayInventoryNativeExitCode
    inventoryChildFailureStage =
      [string]$script:virtualDisplayInventoryChildFailureStage
    inventoryCoverageStage =
      [string]$script:virtualDisplayInventoryCoverageStage
    inventoryCoverageReason =
      [string]$script:virtualDisplayInventoryCoverageReason
    inventoryRequestedCount =
      [int]$script:virtualDisplayInventoryRequestedCount
    inventoryReturnedCount =
      [int]$script:virtualDisplayInventoryReturnedCount
    inventoryResponseRequestedCount =
      [int]$script:virtualDisplayInventoryResponseRequestedCount
    inventoryResponseReturnedRowCount =
      [int]$script:virtualDisplayInventoryResponseReturnedRowCount
    inventoryResponseUniqueOrdinalCount =
      [int]$script:virtualDisplayInventoryResponseUniqueOrdinalCount
    inventoryResponseUniqueOrdinalIgnoreCaseCount =
      [int]$script:virtualDisplayInventoryResponseUniqueOrdinalIgnoreCaseCount
    inventoryResponseDuplicateGroupCount =
      [int]$script:virtualDisplayInventoryResponseDuplicateGroupCount
    inventoryResponseDuplicateMaxMultiplicity =
      [int]$script:virtualDisplayInventoryResponseDuplicateMaxMultiplicity
    inventoryResponseCaseOnlyDuplicateCount =
      [int]$script:virtualDisplayInventoryResponseCaseOnlyDuplicateCount
    inventoryResponseDataRelation =
      [string]$script:virtualDisplayInventoryResponseDataRelation
    inventoryResponseInvalidReason =
      [string]$script:virtualDisplayInventoryResponseInvalidReason
    inventoryResponseInvalidCount =
      [int]$script:virtualDisplayInventoryResponseInvalidCount
    inventoryDeviceCount = [int]$script:virtualDisplayInventoryDeviceCount
    inventoryCurrentBatchIndex =
      [int]$script:virtualDisplayInventoryCurrentBatchIndex
    inventoryTotalBatchCount =
      [int]$script:virtualDisplayInventoryTotalBatchCount
    inventoryHardwareBatchesCompleted =
      [int]$script:virtualDisplayInventoryHardwareBatchesCompleted
    inventoryDriverBatchesCompleted =
      [int]$script:virtualDisplayInventoryDriverBatchesCompleted
    inventoryElapsedMilliseconds =
      [int]$script:virtualDisplayInventoryElapsedMilliseconds
    inventoryRunBudgetMilliseconds =
      [int]$script:virtualDisplayInventoryRunBudgetMilliseconds
    inventoryHardCapMilliseconds =
      [int]$script:virtualDisplayInventoryHardCapMilliseconds
    inventoryCleanupState =
      [string]$script:virtualDisplayInventoryCleanupState
    inventoryRootPidZero = [bool]$script:virtualDisplayInventoryRootPidZero
    inventoryJobActiveProcesses =
      [int]$script:virtualDisplayInventoryJobActiveProcesses
    finalizePreReadStage = [string]$script:virtualDisplayFinalizePreReadStage
    finalizePreReadReason = [string]$script:virtualDisplayFinalizePreReadReason
    finalizePreReadCleanupState =
      [string]$script:virtualDisplayFinalizePreReadCleanupState
    finalizePreReadRootPidZero =
      [bool]$script:virtualDisplayFinalizePreReadRootPidZero
    finalizePreReadJobActiveProcesses =
      [int]$script:virtualDisplayFinalizePreReadJobActiveProcesses
    finalizePreReadStdoutClosed =
      [bool]$script:virtualDisplayFinalizePreReadStdoutClosed
    finalizePreReadStderrClosed =
      [bool]$script:virtualDisplayFinalizePreReadStderrClosed
    finalizePreReadDeviceCount =
      [int]$script:virtualDisplayFinalizePreReadDeviceCount
    finalizePreReadPresentDeviceCount =
      [int]$script:virtualDisplayFinalizePreReadPresentDeviceCount
    finalizePreReadIdentitySha256 =
      [string]$script:virtualDisplayFinalizePreReadIdentitySha256
    finalizePreReadDriverBindingVerified =
      [bool]$script:virtualDisplayFinalizePreReadDriverBindingVerified
  }
}

function Assert-VirtualDisplayDiagnosticCorrelation($Document) {
  $emptyIdentitySha =
    "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
  $fallbackValid = if (-not [bool]$Document.fallbackAttempted) {
    [int]$Document.fallbackExitCode -eq -1 -and
    [string]$Document.fallbackStage -ceq "notAttempted" -and
    [string]$Document.fallbackReason -ceq "none"
  } else {
    switch ([string]$Document.fallbackStage) {
      "trustedToolResolve" {
        [int]$Document.fallbackExitCode -eq -1 -and
        [string]$Document.fallbackReason -ceq "trustedToolUnavailable"
      }
      "processInvoke" {
        [int]$Document.fallbackExitCode -eq -1 -and
        @("processStartOrCleanup", "timeout", "outputInvalid",
          "encodingInvalid",
          "outputOverflow", "outputUnavailable", "unknown") -ccontains
            [string]$Document.fallbackReason
      }
      "tupleValidation" {
        (([string]$Document.fallbackReason -ceq "tupleInvalid" -and
            [int]$Document.fallbackExitCode -eq -1) -or
          ([string]$Document.fallbackReason -ceq "nativeFailure" -and
            [int]$Document.fallbackExitCode -gt 0))
      }
      "completed" {
        [int]$Document.fallbackExitCode -eq 0 -and
        [string]$Document.fallbackReason -ceq "none"
      }
      default { $false }
    }
  }
  $compensationValid = switch (
      [string]$Document.compensationState) {
    "notRequired" {
      [string]$Document.compensationFailureReason -ceq "none"
    }
    "completed" {
      [string]$Document.compensationFailureReason -ceq "none"
    }
    "failed" {
      [string]$Document.compensationFailureReason -cne "none"
    }
    default { $false }
  }
  $terminalValid = switch ([string]$Document.terminalReadbackState) {
    "notAttempted" {
      [string]$Document.terminalReadbackReason -ceq "none" -and
      [int]$Document.observedDeviceCount -eq 0 -and
      [int]$Document.presentDeviceCount -eq 0 -and
      [string]$Document.uniqueDeviceIdsSha256 -ceq $emptyIdentitySha -and
      -not [bool]$Document.driverBindingVerified -and
      [string]$Document.residualDeviceState -ceq "unknown"
    }
    "completed" {
      if ([string]$Document.terminalReadbackReason -cne "none" -or
          [int]$Document.observedDeviceCount -lt 0 -or
          [int]$Document.presentDeviceCount -lt 0 -or
          [int]$Document.presentDeviceCount -gt
            [int]$Document.observedDeviceCount) {
        $false
      } elseif ([int]$Document.observedDeviceCount -eq 0) {
        -not [bool]$Document.driverBindingVerified -and
        [string]$Document.residualDeviceState -ceq "zero" -and
        [string]$Document.uniqueDeviceIdsSha256 -ceq $emptyIdentitySha
      } elseif ([int]$Document.observedDeviceCount -eq 1) {
        [string]$Document.uniqueDeviceIdsSha256 -cne $emptyIdentitySha -and
        (([bool]$Document.driverBindingVerified -and
          [int]$Document.presentDeviceCount -eq 1 -and
          [string]$Document.residualDeviceState -ceq "exactOneBound") -or
        (-not [bool]$Document.driverBindingVerified -and
          [string]$Document.residualDeviceState -ceq "exactOneUnbound"))
      } else {
        -not [bool]$Document.driverBindingVerified -and
        [string]$Document.residualDeviceState -ceq "multiple" -and
        [string]$Document.uniqueDeviceIdsSha256 -cne $emptyIdentitySha
      }
    }
    "failed" {
      @("unavailable", "invalid") -ccontains
      [string]$Document.terminalReadbackReason -and
      [int]$Document.observedDeviceCount -eq -1 -and
      [int]$Document.presentDeviceCount -eq -1 -and
      [string]$Document.uniqueDeviceIdsSha256 -ceq $emptyIdentitySha -and
      -not [bool]$Document.driverBindingVerified -and
      [string]$Document.residualDeviceState -ceq "unknown"
    }
    default { $false }
  }
  $createProvenanceValid = if (
      [int]$Document.createInvocationCount -eq 0) {
    [string]$Document.createInvocationIdSha256 -ceq $emptyIdentitySha -and
    [string]$Document.preCreateIdentitySha256 -ceq $emptyIdentitySha -and
    [string]$Document.postCreateIdentitySha256 -ceq $emptyIdentitySha -and
    [string]$Document.postCreateIdentityState -ceq "notAttempted" -and
    [string]$Document.postCreateIdentityReason -ceq "none"
  } elseif ([int]$Document.createInvocationCount -eq 1) {
    if ([string]$Document.createInvocationIdSha256 -ceq $emptyIdentitySha -or
        [string]$Document.preCreateIdentitySha256 -cne $emptyIdentitySha) {
      $false
    } elseif ([string]$Document.postCreateIdentityState -ceq "completed") {
      [string]$Document.postCreateIdentityReason -ceq "none" -and
      [string]$Document.terminalReadbackState -ceq "completed" -and
      [string]$Document.postCreateIdentitySha256 -ceq
        [string]$Document.uniqueDeviceIdsSha256
    } elseif ([string]$Document.postCreateIdentityState -ceq "failed") {
      [string]$Document.terminalReadbackState -ceq "failed" -and
      [string]$Document.postCreateIdentityReason -ceq
        [string]$Document.terminalReadbackReason -and
      @("unavailable", "invalid") -ccontains
        [string]$Document.postCreateIdentityReason -and
      [string]$Document.postCreateIdentitySha256 -ceq $emptyIdentitySha
    } else {
      $false
    }
  } else {
    $false
  }
  if (-not $fallbackValid -or -not $compensationValid -or
      -not $terminalValid -or -not $createProvenanceValid) {
    throw "virtualDisplayDiagnosticInvalid"
  }
  Assert-VirtualDisplayInventoryDiagnosticCorrelation $Document
  Assert-VirtualDisplayFinalizePreReadCorrelation $Document
}

function Assert-VirtualDisplayFinalizePreReadCorrelation($Document) {
  $emptyIdentitySha =
    "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
  $stage = [string]$Document.finalizePreReadStage
  $reason = [string]$Document.finalizePreReadReason
  $cleanup = [string]$Document.finalizePreReadCleanupState
  $rootPidZero = [bool]$Document.finalizePreReadRootPidZero
  $jobActive = [int]$Document.finalizePreReadJobActiveProcesses
  $stdoutClosed = [bool]$Document.finalizePreReadStdoutClosed
  $stderrClosed = [bool]$Document.finalizePreReadStderrClosed
  $count = [int]$Document.finalizePreReadDeviceCount
  $present = [int]$Document.finalizePreReadPresentDeviceCount
  $identitySha = [string]$Document.finalizePreReadIdentitySha256
  $binding = [bool]$Document.finalizePreReadDriverBindingVerified
  $valid = switch ($stage) {
    "notAttempted" {
      $reason -ceq "none" -and $cleanup -ceq "notRequired" -and
      $rootPidZero -and $jobActive -eq 0 -and
      -not $stdoutClosed -and -not $stderrClosed -and
      $count -eq -1 -and $present -eq -1 -and
      $identitySha -ceq $emptyIdentitySha -and -not $binding
    }
    "completed" {
      $reason -ceq "none" -and $cleanup -ceq "completed" -and
      $rootPidZero -and $jobActive -eq 0 -and
      $stdoutClosed -and $stderrClosed -and
      $count -ge 0 -and $count -le 16 -and
      $present -ge 0 -and $present -le $count -and
      (($count -eq 0 -and $identitySha -ceq $emptyIdentitySha -and
          -not $binding) -or
       ($count -gt 0 -and $identitySha -cne $emptyIdentitySha -and
          (-not $binding -or ($count -eq 1 -and $present -eq 1))))
    }
    "failed" {
      $reason -cne "none" -and $count -eq -1 -and $present -eq -1 -and
      $identitySha -ceq $emptyIdentitySha -and -not $binding -and
      $(if ($reason -ceq "resolve") {
        $cleanup -ceq "notRequired" -and $rootPidZero -and
        $jobActive -eq 0 -and -not $stdoutClosed -and -not $stderrClosed
      } elseif ($reason -ceq "cleanup") {
        $cleanup -ceq "failed" -and -not $rootPidZero -and
        $jobActive -eq -1
      } else {
        $cleanup -ceq "completed" -and $rootPidZero -and
        $jobActive -eq 0 -and $stdoutClosed -and $stderrClosed
      })
    }
    default { $false }
  }
  if (-not $valid) { throw "virtualDisplayDiagnosticInvalid" }
}

function Assert-VirtualDisplayInventoryDiagnosticCorrelation($Document) {
  $stage = [string]$Document.inventoryStage
  $failure = [string]$Document.inventoryFailureStage
  $outputReason = [string]$Document.inventoryOutputReason
  $nativeExitCode = [int]$Document.inventoryNativeExitCode
  $childFailureStage = [string]$Document.inventoryChildFailureStage
  $coverageStage = [string]$Document.inventoryCoverageStage
  $coverageReason = [string]$Document.inventoryCoverageReason
  $requestedCount = [int]$Document.inventoryRequestedCount
  $returnedCount = [int]$Document.inventoryReturnedCount
  $responseRequested = [int]$Document.inventoryResponseRequestedCount
  $responseReturned = [int]$Document.inventoryResponseReturnedRowCount
  $responseOrdinal = [int]$Document.inventoryResponseUniqueOrdinalCount
  $responseOrdinalIgnoreCase =
    [int]$Document.inventoryResponseUniqueOrdinalIgnoreCaseCount
  $responseGroups = [int]$Document.inventoryResponseDuplicateGroupCount
  $responseMultiplicity =
    [int]$Document.inventoryResponseDuplicateMaxMultiplicity
  $responseCaseOnly = [int]$Document.inventoryResponseCaseOnlyDuplicateCount
  $responseRelation = [string]$Document.inventoryResponseDataRelation
  $responseInvalidReason = [string]$Document.inventoryResponseInvalidReason
  $responseInvalidCount = [int]$Document.inventoryResponseInvalidCount
  $deviceCount = [int]$Document.inventoryDeviceCount
  $currentBatch = [int]$Document.inventoryCurrentBatchIndex
  $totalBatches = [int]$Document.inventoryTotalBatchCount
  $hardwareCompleted =
    [int]$Document.inventoryHardwareBatchesCompleted
  $driverCompleted = [int]$Document.inventoryDriverBatchesCompleted
  $elapsed = [int]$Document.inventoryElapsedMilliseconds
  $runBudget = [int]$Document.inventoryRunBudgetMilliseconds
  $hardCap = [int]$Document.inventoryHardCapMilliseconds
  $cleanup = [string]$Document.inventoryCleanupState
  $rootPidZero = [bool]$Document.inventoryRootPidZero
  $jobActive = [int]$Document.inventoryJobActiveProcesses
  $closedCleanup = (
    ($cleanup -ceq "notRequired" -and $rootPidZero -and $jobActive -eq 0) -or
    ($cleanup -ceq "completed" -and $rootPidZero -and $jobActive -eq 0) -or
    ($cleanup -ceq "failed" -and -not $rootPidZero -and $jobActive -eq -1))
  $batchShape = (
    $deviceCount -ge -1 -and $deviceCount -le 4096 -and
    $totalBatches -ge 0 -and $totalBatches -le 128 -and
    $currentBatch -ge -1 -and $currentBatch -lt [Math]::Max(1, $totalBatches) -and
    $hardwareCompleted -ge 0 -and $hardwareCompleted -le $totalBatches -and
    $driverCompleted -ge 0 -and $driverCompleted -le $totalBatches -and
    $driverCompleted -le $hardwareCompleted -and
    $elapsed -ge 0 -and $elapsed -le $hardCap -and
    $runBudget -gt 0 -and $runBudget -lt $hardCap -and
    $hardCap -eq 10000)
  $valid = $false
  $coverageNone = (
    $coverageStage -ceq "none" -and $coverageReason -ceq "none" -and
    $requestedCount -eq -1 -and $returnedCount -eq -1)
  $phaseBatchAuthority = (
    @("hardwareIds", "driverInf") -ccontains $stage -and
    $deviceCount -ge 1 -and
    $totalBatches -eq [int][Math]::Ceiling($deviceCount / 32.0) -and
    $currentBatch -ge 0 -and $currentBatch -lt $totalBatches -and
    (($stage -ceq "hardwareIds" -and
      $hardwareCompleted -eq $currentBatch -and $driverCompleted -eq 0) -or
     ($stage -ceq "driverInf" -and
      $hardwareCompleted -eq $totalBatches -and
      $driverCompleted -eq $currentBatch)))
  $nativeExitAuthority = switch ($nativeExitCode) {
    91 { $childFailureStage -ceq "inputValidation" }
    93 { $childFailureStage -ceq "validationMode" }
    94 { $childFailureStage -ceq "validationQuota" }
    95 { $childFailureStage -ceq "requestIdentityDuplicate" }
    96 { $childFailureStage -ceq "responseIdentityUnknown" }
    97 { $childFailureStage -ceq "propertyQuery" }
    98 { $childFailureStage -ceq "hostFailure" }
    101 { $childFailureStage -ceq "responseIdentityInvalid" }
    102 { $childFailureStage -ceq "requestIdentityInvalid" }
    103 { $childFailureStage -ceq "responseIdentityConflict" }
    default { $false }
  }
  $outputTupleValid = switch ($outputReason) {
    "nativeExit" { [bool]$nativeExitAuthority }
    "stderr" {
      $nativeExitCode -eq 0 -and $childFailureStage -ceq "stderr"
    }
    "invokeFailure" {
      $nativeExitCode -eq -1 -and $childFailureStage -ceq "processInvoke"
    }
    default { $false }
  }
  $outputReasonValid = if ($failure -ceq "output") {
    $phaseBatchAuthority -and $cleanup -ceq "completed" -and
    $rootPidZero -and $jobActive -eq 0 -and $outputTupleValid
  } else {
    $outputReason -ceq "none" -and $nativeExitCode -eq -1 -and
    $childFailureStage -ceq "none"
  }
  $expectedRequestedCount = if (
      $deviceCount -ge 1 -and $currentBatch -ge 0 -and
      $currentBatch -lt $totalBatches) {
    [Math]::Min(32, $deviceCount - ($currentBatch * 32))
  } else { -1 }
  $responseStatsDefault = (
    $responseRequested -eq -1 -and $responseReturned -eq -1 -and
    $responseOrdinal -eq -1 -and $responseOrdinalIgnoreCase -eq -1 -and
    $responseGroups -eq 0 -and $responseMultiplicity -eq 0 -and
    $responseCaseOnly -eq 0 -and $responseRelation -ceq "none" -and
    $responseInvalidReason -ceq "none" -and $responseInvalidCount -eq 0)
  $responseStatsShape = (
    $responseRequested -eq $expectedRequestedCount -and
    $responseReturned -ge 0 -and $responseReturned -le 64 -and
    $responseOrdinal -ge 0 -and $responseOrdinal -le $responseReturned -and
    $responseOrdinalIgnoreCase -ge 0 -and
      $responseOrdinalIgnoreCase -le $responseOrdinal -and
    $responseGroups -ge 0 -and
      $responseGroups -le $responseOrdinalIgnoreCase -and
    $responseMultiplicity -ge 0 -and
      $responseMultiplicity -le $responseReturned -and
    $responseCaseOnly -ge 0 -and $responseCaseOnly -le $responseGroups -and
    $responseInvalidCount -ge 0 -and
      $responseInvalidCount -le $responseReturned)
  $responseStatsValid = if (
      $failure -ceq "output" -and $outputReason -ceq "nativeExit" -and
      $nativeExitCode -in @(101, 103)) {
    $phaseBatchAuthority -and $responseStatsShape -and
    (($nativeExitCode -eq 101 -and $responseRelation -ceq "invalid" -and
        @("empty", "missingProperty", "wrongType", "rowShape",
          "escapingInvalid", "canonicalInvalid", "propertyStateInvalid",
          "absentDataInvalid", "hardwareIdsDataInvalid",
          "driverInfDataInvalid", "mixed") -ccontains
            $responseInvalidReason -and $responseInvalidCount -ge 1) -or
      ($nativeExitCode -eq 103 -and
        $responseRelation -ceq "conflicting" -and
        $responseGroups -ge 1 -and $responseMultiplicity -ge 2 -and
        $responseInvalidReason -ceq "none" -and
        $responseInvalidCount -eq 0))
  } else { $responseStatsDefault }
  $coverageFailure = if ($failure -ceq "coverage") {
    $phaseBatchAuthority -and
    $requestedCount -eq $expectedRequestedCount -and
    $returnedCount -ge 0 -and $returnedCount -le 33 -and
    ((($coverageStage -ceq "rowCount") -and
      ((($coverageReason -ceq "missing") -and
          $returnedCount -lt $requestedCount) -or
       (($coverageReason -ceq "extra") -and
          $returnedCount -gt $requestedCount))) -or
     (($coverageStage -ceq "rowIdentity") -and
      $returnedCount -eq $requestedCount -and
      @("duplicate", "identityMismatch") -ccontains $coverageReason) -or
     (($coverageStage -ceq "batchCoverage") -and
      $returnedCount -eq $requestedCount -and
      $coverageReason -ceq "setMismatch"))
  } else { $coverageNone }
  if ($stage -ceq "notAttempted") {
    $valid = (
      $failure -ceq "none" -and $deviceCount -eq -1 -and
      $currentBatch -eq -1 -and $totalBatches -eq 0 -and
      $hardwareCompleted -eq 0 -and $driverCompleted -eq 0 -and
      $elapsed -eq 0 -and $cleanup -ceq "notRequired" -and
      $rootPidZero -and $jobActive -eq 0 -and $coverageNone -and
      $outputReasonValid)
  } elseif ($stage -ceq "completed") {
    $successCleanup = if ($totalBatches -eq 0) {
      $cleanup -ceq "notRequired" -and $rootPidZero -and $jobActive -eq 0
    } else {
      $cleanup -ceq "completed" -and $rootPidZero -and $jobActive -eq 0
    }
    $valid = (
      $failure -ceq "none" -and $deviceCount -ge 0 -and
      $totalBatches -eq [int][Math]::Ceiling($deviceCount / 32.0) -and
      $hardwareCompleted -eq $totalBatches -and
      $driverCompleted -eq $totalBatches -and $successCleanup -and
      $coverageNone -and $outputReasonValid -and
      ($totalBatches -eq 0 -or $currentBatch -eq $totalBatches - 1))
  } else {
    $valid = (
      @("allDevices", "hardwareIds", "driverInf") -ccontains $stage -and
      @("allDevices", "input", "trustedRunnerStart", "output", "decode",
        "tuple", "coverage", "deadline", "cleanup") -ccontains $failure -and
      $coverageFailure -and $outputReasonValid -and
      $closedCleanup -and
      ($failure -cne "deadline" -or $elapsed -ge $runBudget) -and
      ($failure -cne "cleanup" -or $cleanup -ceq "failed") -and
      ($stage -cne "allDevices" -or (
        $deviceCount -eq -1 -and $currentBatch -eq -1 -and
        $totalBatches -eq 0 -and $hardwareCompleted -eq 0 -and
        $driverCompleted -eq 0 -and $cleanup -ceq "notRequired")))
  }
  if (-not $batchShape -or -not $valid -or -not $responseStatsValid) {
    throw "virtualDisplayDiagnosticInvalid"
  }
}

function ConvertTo-VirtualDisplayDiagnosticToken($Document) {
  $raw = $Document | ConvertTo-Json -Compress
  $bytes = [Text.UTF8Encoding]::new($false).GetBytes($raw)
  $payload = [Convert]::ToBase64String($bytes).TrimEnd('=').
    Replace('+', '-').Replace('/', '_')
  return "vd1.$payload.$((Get-ByteSha256 $bytes).ToLowerInvariant())"
}

function ConvertFrom-VirtualDisplayDiagnosticToken([string]$Token) {
  if ($Token -notmatch '^vd1\.([A-Za-z0-9_-]{1,4096})\.([0-9a-f]{64})$') {
    throw "virtualDisplayDiagnosticInvalid"
  }
  $expectedSha = $Matches[2]
  $encoded = $Matches[1].Replace('-', '+').Replace('_', '/')
  while (($encoded.Length % 4) -ne 0) { $encoded += "=" }
  try { $bytes = [Convert]::FromBase64String($encoded) } catch {
    throw "virtualDisplayDiagnosticInvalid"
  }
  if ((Get-ByteSha256 $bytes).ToLowerInvariant() -cne $expectedSha) {
    throw "virtualDisplayDiagnosticInvalid"
  }
  try {
    $raw = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
  } catch { throw "virtualDisplayDiagnosticInvalid" }
  if (-not [LigaseStrictJson]::HasUniqueProperties($raw)) {
    throw "virtualDisplayDiagnosticInvalid"
  }
  try { $document = $raw | ConvertFrom-Json } catch {
    throw "virtualDisplayDiagnosticInvalid"
  }
  Assert-ClosedProperties $document @(
    "schemaVersion", "candidateSourceHead", "writtenUtc", "resultCode",
    "success", "installStage", "readbackCode",
    "childExitCode", "removeExitCode", "removeCount", "cleanupState",
    "markerStage", "observedDeviceCount", "presentDeviceCount",
    "uniqueDeviceIdsSha256",
    "driverBindingVerified", "fallbackAttempted", "fallbackExitCode",
    "fallbackStage", "fallbackReason",
    "deviceRecovery", "residualDeviceState",
    "compensationState", "compensationFailureReason",
    "terminalReadbackState", "terminalReadbackReason",
    "createInvocationCount", "createInvocationIdSha256",
    "preCreateIdentitySha256", "postCreateIdentitySha256",
    "postCreateIdentityState", "postCreateIdentityReason",
    "inventoryStage", "inventoryFailureStage", "inventoryOutputReason",
    "inventoryNativeExitCode", "inventoryChildFailureStage",
    "inventoryCoverageStage",
    "inventoryCoverageReason", "inventoryRequestedCount",
    "inventoryReturnedCount", "inventoryResponseRequestedCount",
    "inventoryResponseReturnedRowCount",
    "inventoryResponseUniqueOrdinalCount",
    "inventoryResponseUniqueOrdinalIgnoreCaseCount",
    "inventoryResponseDuplicateGroupCount",
    "inventoryResponseDuplicateMaxMultiplicity",
    "inventoryResponseCaseOnlyDuplicateCount",
    "inventoryResponseDataRelation", "inventoryResponseInvalidReason",
    "inventoryResponseInvalidCount", "inventoryDeviceCount",
    "inventoryCurrentBatchIndex", "inventoryTotalBatchCount",
    "inventoryHardwareBatchesCompleted", "inventoryDriverBatchesCompleted",
    "inventoryElapsedMilliseconds", "inventoryRunBudgetMilliseconds",
    "inventoryHardCapMilliseconds", "inventoryCleanupState",
    "inventoryRootPidZero", "inventoryJobActiveProcesses",
    "finalizePreReadStage", "finalizePreReadReason",
    "finalizePreReadCleanupState", "finalizePreReadRootPidZero",
    "finalizePreReadJobActiveProcesses", "finalizePreReadStdoutClosed",
    "finalizePreReadStderrClosed", "finalizePreReadDeviceCount",
    "finalizePreReadPresentDeviceCount", "finalizePreReadIdentitySha256",
    "finalizePreReadDriverBindingVerified") `
      "virtualDisplayDiagnostic"
  $written = [DateTime]::MinValue
  if (-not [DateTime]::TryParseExact(
      [string]$document.writtenUtc, "O",
      [Globalization.CultureInfo]::InvariantCulture,
      [Globalization.DateTimeStyles]::RoundtripKind, [ref]$written) -or
      $document.schemaVersion -ne 1 -or
      $document.candidateSourceHead -isnot [string] -or
      [string]$document.candidateSourceHead -cne (Get-EvidenceSourceHead) -or
      $written.Kind -ne [DateTimeKind]::Utc -or
      $written -gt [DateTime]::UtcNow.AddMinutes(5) -or
      $written -lt [DateTime]::UtcNow.AddHours(-2) -or
      $document.resultCode -isnot [string] -or
      [string]$document.resultCode -cnotmatch '^[a-z][A-Za-z0-9]{0,63}$' -or
      $document.success -isnot [bool] -or
      $document.installStage -isnot [string] -or
      @("notStarted", "toolValidation", "certificateRoot",
        "certificatePublisher", "deviceRemove", "deviceCreate",
        "driverPackageInstall", "completed") -cnotcontains
          [string]$document.installStage -or
      $document.readbackCode -isnot [string] -or
      @("notAttempted", "available", "virtualDisplayNotInstalled",
        "virtualDisplayDeviceCountInvalid", "virtualDisplayDriverBindingMissing",
        "virtualDisplayRebootRequired", "virtualDisplayReadbackFailed") -cnotcontains
          [string]$document.readbackCode -or
      $document.childExitCode -isnot [int] -or
      $document.removeExitCode -isnot [int] -or
      $document.removeCount -isnot [int] -or
      $document.cleanupState -isnot [string] -or
      @("notRequired", "completed", "failed") -cnotcontains
        [string]$document.cleanupState -or
      $document.markerStage -isnot [string] -or
      @("notAttempted", "commit", "completed") -cnotcontains
        [string]$document.markerStage -or
      $document.removeCount -lt 0 -or $document.removeCount -gt 16 -or
      $document.observedDeviceCount -isnot [int] -or
      $document.observedDeviceCount -lt -1 -or
      $document.observedDeviceCount -gt 16 -or
      $document.presentDeviceCount -isnot [int] -or
      $document.presentDeviceCount -lt -1 -or
      $document.presentDeviceCount -gt 16 -or
      $document.uniqueDeviceIdsSha256 -isnot [string] -or
      [string]$document.uniqueDeviceIdsSha256 -cnotmatch '^[0-9a-f]{64}$' -or
      $document.driverBindingVerified -isnot [bool] -or
      $document.fallbackAttempted -isnot [bool] -or
      $document.fallbackExitCode -isnot [int] -or
      $document.fallbackStage -isnot [string] -or
      @("notAttempted", "trustedToolResolve", "processInvoke",
        "tupleValidation", "completed") -cnotcontains
          [string]$document.fallbackStage -or
      $document.fallbackReason -isnot [string] -or
      @("none", "trustedToolUnavailable", "processStartOrCleanup",
        "timeout", "outputInvalid", "encodingInvalid", "outputOverflow",
        "outputUnavailable",
        "tupleInvalid", "nativeFailure", "unknown") -cnotcontains
          [string]$document.fallbackReason -or
      $document.deviceRecovery -isnot [string] -or
      @("notAttempted", "inProgress", "completed", "failed") -cnotcontains
        [string]$document.deviceRecovery -or
      $document.residualDeviceState -isnot [string] -or
      @("unknown", "zero", "exactOneBound", "exactOneUnbound",
        "multiple") -cnotcontains [string]$document.residualDeviceState -or
      $document.compensationState -isnot [string] -or
      @("notRequired", "completed", "failed") -cnotcontains
        [string]$document.compensationState -or
      $document.compensationFailureReason -isnot [string] -or
      @("none", "markerRestore", "dependentDevice", "certificateRemove",
        "certificateResidue", "temporaryResidue", "multiple") -cnotcontains
          [string]$document.compensationFailureReason -or
      $document.terminalReadbackState -isnot [string] -or
      @("notAttempted", "completed", "failed") -cnotcontains
        [string]$document.terminalReadbackState -or
      $document.terminalReadbackReason -isnot [string] -or
      @("none", "unavailable", "invalid") -cnotcontains
        [string]$document.terminalReadbackReason -or
      $document.createInvocationCount -isnot [int] -or
      $document.createInvocationCount -lt 0 -or
      $document.createInvocationCount -gt 1 -or
      $document.createInvocationIdSha256 -isnot [string] -or
      [string]$document.createInvocationIdSha256 -cnotmatch
        '^[0-9a-f]{64}$' -or
      $document.preCreateIdentitySha256 -isnot [string] -or
      [string]$document.preCreateIdentitySha256 -cnotmatch
        '^[0-9a-f]{64}$' -or
      $document.postCreateIdentitySha256 -isnot [string] -or
      [string]$document.postCreateIdentitySha256 -cnotmatch
        '^[0-9a-f]{64}$' -or
      $document.postCreateIdentityState -isnot [string] -or
      @("notAttempted", "completed", "failed") -cnotcontains
        [string]$document.postCreateIdentityState -or
      $document.postCreateIdentityReason -isnot [string] -or
      @("none", "unavailable", "invalid") -cnotcontains
        [string]$document.postCreateIdentityReason -or
      $document.inventoryStage -isnot [string] -or
      $document.inventoryFailureStage -isnot [string] -or
      $document.inventoryOutputReason -isnot [string] -or
      @("none", "nativeExit", "stderr", "invokeFailure") -cnotcontains
        [string]$document.inventoryOutputReason -or
      $document.inventoryNativeExitCode -isnot [int] -or
      $document.inventoryChildFailureStage -isnot [string] -or
      @("none", "inputValidation", "validationMode", "validationQuota",
        "requestIdentityDuplicate", "requestIdentityInvalid",
        "responseIdentityUnknown", "responseIdentityInvalid",
        "responseIdentityConflict", "propertyQuery",
        "hostFailure", "stderr", "processInvoke") -cnotcontains
          [string]$document.inventoryChildFailureStage -or
      $document.inventoryCoverageStage -isnot [string] -or
      @("none", "rowCount", "rowIdentity", "batchCoverage") -cnotcontains
        [string]$document.inventoryCoverageStage -or
      $document.inventoryCoverageReason -isnot [string] -or
      @("none", "missing", "extra", "duplicate", "identityMismatch",
        "setMismatch") -cnotcontains
          [string]$document.inventoryCoverageReason -or
      $document.inventoryRequestedCount -isnot [int] -or
      $document.inventoryReturnedCount -isnot [int] -or
      $document.inventoryResponseRequestedCount -isnot [int] -or
      $document.inventoryResponseReturnedRowCount -isnot [int] -or
      $document.inventoryResponseUniqueOrdinalCount -isnot [int] -or
      $document.inventoryResponseUniqueOrdinalIgnoreCaseCount -isnot [int] -or
      $document.inventoryResponseDuplicateGroupCount -isnot [int] -or
      $document.inventoryResponseDuplicateMaxMultiplicity -isnot [int] -or
      $document.inventoryResponseCaseOnlyDuplicateCount -isnot [int] -or
      $document.inventoryResponseInvalidReason -isnot [string] -or
      $document.inventoryResponseInvalidCount -isnot [int] -or
      $document.inventoryResponseDataRelation -isnot [string] -or
      @("none", "identical", "conflicting", "invalid") -cnotcontains
        [string]$document.inventoryResponseDataRelation -or
      $document.inventoryDeviceCount -isnot [int] -or
      $document.inventoryCurrentBatchIndex -isnot [int] -or
      $document.inventoryTotalBatchCount -isnot [int] -or
      $document.inventoryHardwareBatchesCompleted -isnot [int] -or
      $document.inventoryDriverBatchesCompleted -isnot [int] -or
      $document.inventoryElapsedMilliseconds -isnot [int] -or
      $document.inventoryRunBudgetMilliseconds -isnot [int] -or
      $document.inventoryHardCapMilliseconds -isnot [int] -or
      $document.inventoryCleanupState -isnot [string] -or
      $document.inventoryRootPidZero -isnot [bool] -or
      $document.inventoryJobActiveProcesses -isnot [int] -or
      $document.finalizePreReadStage -isnot [string] -or
      @("notAttempted", "completed", "failed") -cnotcontains
        [string]$document.finalizePreReadStage -or
      $document.finalizePreReadReason -isnot [string] -or
      @("none", "resolve", "start", "timeout", "overflow", "pipe",
        "nativeExit", "stderr", "utf8", "json", "schema", "result",
        "cleanup") -cnotcontains [string]$document.finalizePreReadReason -or
      $document.finalizePreReadCleanupState -isnot [string] -or
      @("notRequired", "completed", "failed") -cnotcontains
        [string]$document.finalizePreReadCleanupState -or
      $document.finalizePreReadRootPidZero -isnot [bool] -or
      $document.finalizePreReadJobActiveProcesses -isnot [int] -or
      $document.finalizePreReadStdoutClosed -isnot [bool] -or
      $document.finalizePreReadStderrClosed -isnot [bool] -or
      $document.finalizePreReadDeviceCount -isnot [int] -or
      $document.finalizePreReadPresentDeviceCount -isnot [int] -or
      $document.finalizePreReadIdentitySha256 -isnot [string] -or
      [string]$document.finalizePreReadIdentitySha256 -cnotmatch
        '^[0-9a-f]{64}$' -or
      $document.finalizePreReadDriverBindingVerified -isnot [bool] -or
      ([string]$document.terminalReadbackState -ceq "failed" -and (
        [int]$document.observedDeviceCount -ne -1 -or
        [string]$document.residualDeviceState -cne "unknown" -or
        [bool]$document.driverBindingVerified)) -or
      ([string]$document.terminalReadbackState -ceq "completed" -and
        [int]$document.observedDeviceCount -lt 0)) {
    throw "virtualDisplayDiagnosticInvalid"
  }
  Assert-VirtualDisplayDiagnosticCorrelation $document
  return $document
}

function Write-VirtualDisplayDiagnostic($Document) {
  if ($env:LIGASE_INSTALL_VALIDATION_HARNESS -ceq "1" -and
      $env:LIGASE_VIRTUAL_DISPLAY_DIAGNOSTIC_WRITE_FAULT -ceq "1" -and
      -not [string]::IsNullOrWhiteSpace($ValidationRoot) -and
      [IO.Path]::GetFullPath($ValidationRoot) -ceq $installRoot -and
      [IO.Path]::GetPathRoot($installRoot) -ceq "D:\") {
    throw "virtualDisplayDiagnosticUnavailable"
  }
  $path = Get-VirtualDisplayDiagnosticPath
  $directory = Split-Path -Parent $path
  if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
  }
  Set-SecureDataRootAcl $directory
  $raw = $Document | ConvertTo-Json -Compress
  $bytes = [Text.UTF8Encoding]::new($false).GetBytes($raw)
  $temporary = Join-Path $directory (
    ".virtual-display-outcome-" + [Guid]::NewGuid().ToString("N") + ".tmp")
  $stream = [IO.File]::Open(
    $temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
    [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
  try {
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush($true)
  } finally {
    $stream.Dispose()
  }
  try {
    if (Test-Path -LiteralPath $path -PathType Leaf) {
      [IO.File]::Replace($temporary, $path, $null, $true)
    } else {
      [IO.File]::Move($temporary, $path)
    }
    $readback = [IO.File]::ReadAllBytes($path)
    if (-not (Test-ExactBytes $bytes $readback)) {
      throw "virtualDisplayDiagnosticUnavailable"
    }
  } finally {
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
  }
}

function Read-VirtualDisplayDiagnostic {
  $path = Get-VirtualDisplayDiagnosticPath
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "virtualDisplayDiagnosticUnavailable"
  }
  $raw = [IO.File]::ReadAllText($path, [Text.UTF8Encoding]::new($false, $true))
  if (-not [LigaseStrictJson]::HasUniqueProperties($raw)) {
    throw "virtualDisplayDiagnosticInvalid"
  }
  try { $document = $raw | ConvertFrom-Json } catch {
    throw "virtualDisplayDiagnosticInvalid"
  }
  Assert-ClosedProperties $document @(
    "schemaVersion", "candidateSourceHead", "writtenUtc", "resultCode",
    "success", "installStage", "readbackCode",
    "childExitCode", "removeExitCode", "removeCount", "cleanupState",
    "markerStage", "observedDeviceCount", "presentDeviceCount",
    "uniqueDeviceIdsSha256",
    "driverBindingVerified", "fallbackAttempted", "fallbackExitCode",
    "fallbackStage", "fallbackReason",
    "deviceRecovery", "residualDeviceState",
    "compensationState", "compensationFailureReason",
    "terminalReadbackState", "terminalReadbackReason",
    "createInvocationCount", "createInvocationIdSha256",
    "preCreateIdentitySha256", "postCreateIdentitySha256",
    "postCreateIdentityState", "postCreateIdentityReason",
    "inventoryStage", "inventoryFailureStage", "inventoryOutputReason",
    "inventoryNativeExitCode", "inventoryChildFailureStage",
    "inventoryCoverageStage",
    "inventoryCoverageReason", "inventoryRequestedCount",
    "inventoryReturnedCount", "inventoryResponseRequestedCount",
    "inventoryResponseReturnedRowCount",
    "inventoryResponseUniqueOrdinalCount",
    "inventoryResponseUniqueOrdinalIgnoreCaseCount",
    "inventoryResponseDuplicateGroupCount",
    "inventoryResponseDuplicateMaxMultiplicity",
    "inventoryResponseCaseOnlyDuplicateCount",
    "inventoryResponseDataRelation", "inventoryResponseInvalidReason",
    "inventoryResponseInvalidCount", "inventoryDeviceCount",
    "inventoryCurrentBatchIndex", "inventoryTotalBatchCount",
    "inventoryHardwareBatchesCompleted", "inventoryDriverBatchesCompleted",
    "inventoryElapsedMilliseconds", "inventoryRunBudgetMilliseconds",
    "inventoryHardCapMilliseconds", "inventoryCleanupState",
    "inventoryRootPidZero", "inventoryJobActiveProcesses",
    "finalizePreReadStage", "finalizePreReadReason",
    "finalizePreReadCleanupState", "finalizePreReadRootPidZero",
    "finalizePreReadJobActiveProcesses", "finalizePreReadStdoutClosed",
    "finalizePreReadStderrClosed", "finalizePreReadDeviceCount",
    "finalizePreReadPresentDeviceCount", "finalizePreReadIdentitySha256",
    "finalizePreReadDriverBindingVerified") `
      "virtualDisplayDiagnostic"
  $written = [DateTime]::MinValue
  if (-not [DateTime]::TryParseExact(
      [string]$document.writtenUtc, "O",
      [Globalization.CultureInfo]::InvariantCulture,
      [Globalization.DateTimeStyles]::RoundtripKind, [ref]$written)) {
    throw "virtualDisplayDiagnosticInvalid"
  }
  if ($document.schemaVersion -ne 1 -or
      $document.candidateSourceHead -isnot [string] -or
      [string]$document.candidateSourceHead -cne (Get-EvidenceSourceHead) -or
      $written.Kind -ne [DateTimeKind]::Utc -or
      $written -gt [DateTime]::UtcNow.AddMinutes(5) -or
      $written -lt [DateTime]::UtcNow.AddHours(-2) -or
      $document.resultCode -isnot [string] -or
      [string]$document.resultCode -cnotmatch '^[a-z][A-Za-z0-9]{0,63}$' -or
      $document.success -isnot [bool] -or
      $document.installStage -isnot [string] -or
      @("notStarted", "toolValidation", "certificateRoot",
        "certificatePublisher", "deviceRemove", "deviceCreate",
        "driverPackageInstall", "completed") -cnotcontains
          [string]$document.installStage -or
      $document.readbackCode -isnot [string] -or
      @("notAttempted", "available", "virtualDisplayNotInstalled",
        "virtualDisplayDeviceCountInvalid", "virtualDisplayDriverBindingMissing",
        "virtualDisplayRebootRequired", "virtualDisplayReadbackFailed") -cnotcontains
          [string]$document.readbackCode -or
      $document.childExitCode -isnot [int] -or
      $document.removeExitCode -isnot [int] -or
      $document.removeCount -isnot [int] -or
      $document.cleanupState -isnot [string] -or
      @("notRequired", "completed", "failed") -cnotcontains
        [string]$document.cleanupState -or
      $document.markerStage -isnot [string] -or
      @("notAttempted", "commit", "completed") -cnotcontains
        [string]$document.markerStage -or
      $document.removeCount -lt 0 -or $document.removeCount -gt 16 -or
      $document.observedDeviceCount -lt -1 -or
      $document.observedDeviceCount -gt 16 -or
      $document.observedDeviceCount -isnot [int] -or
      $document.presentDeviceCount -isnot [int] -or
      $document.presentDeviceCount -lt -1 -or
      $document.presentDeviceCount -gt 16 -or
      $document.uniqueDeviceIdsSha256 -isnot [string] -or
      [string]$document.uniqueDeviceIdsSha256 -cnotmatch '^[0-9a-f]{64}$' -or
      $document.driverBindingVerified -isnot [bool] -or
      $document.fallbackAttempted -isnot [bool] -or
      $document.fallbackExitCode -isnot [int] -or
      $document.fallbackStage -isnot [string] -or
      @("notAttempted", "trustedToolResolve", "processInvoke",
        "tupleValidation", "completed") -cnotcontains
          [string]$document.fallbackStage -or
      $document.fallbackReason -isnot [string] -or
      @("none", "trustedToolUnavailable", "processStartOrCleanup",
        "timeout", "outputInvalid", "encodingInvalid", "outputOverflow",
        "outputUnavailable",
        "tupleInvalid", "nativeFailure", "unknown") -cnotcontains
          [string]$document.fallbackReason -or
      $document.deviceRecovery -isnot [string] -or
      @("notAttempted", "inProgress", "completed", "failed") -cnotcontains
        [string]$document.deviceRecovery -or
      $document.residualDeviceState -isnot [string] -or
      @("unknown", "zero", "exactOneBound", "exactOneUnbound",
        "multiple") -cnotcontains [string]$document.residualDeviceState -or
      $document.compensationState -isnot [string] -or
      @("notRequired", "completed", "failed") -cnotcontains
        [string]$document.compensationState -or
      $document.compensationFailureReason -isnot [string] -or
      @("none", "markerRestore", "dependentDevice", "certificateRemove",
        "certificateResidue", "temporaryResidue", "multiple") -cnotcontains
          [string]$document.compensationFailureReason -or
      $document.terminalReadbackState -isnot [string] -or
      @("notAttempted", "completed", "failed") -cnotcontains
        [string]$document.terminalReadbackState -or
      $document.terminalReadbackReason -isnot [string] -or
      @("none", "unavailable", "invalid") -cnotcontains
        [string]$document.terminalReadbackReason -or
      $document.createInvocationCount -isnot [int] -or
      $document.createInvocationCount -lt 0 -or
      $document.createInvocationCount -gt 1 -or
      $document.createInvocationIdSha256 -isnot [string] -or
      [string]$document.createInvocationIdSha256 -cnotmatch
        '^[0-9a-f]{64}$' -or
      $document.preCreateIdentitySha256 -isnot [string] -or
      [string]$document.preCreateIdentitySha256 -cnotmatch
        '^[0-9a-f]{64}$' -or
      $document.postCreateIdentitySha256 -isnot [string] -or
      [string]$document.postCreateIdentitySha256 -cnotmatch
        '^[0-9a-f]{64}$' -or
      $document.postCreateIdentityState -isnot [string] -or
      @("notAttempted", "completed", "failed") -cnotcontains
        [string]$document.postCreateIdentityState -or
      $document.postCreateIdentityReason -isnot [string] -or
      @("none", "unavailable", "invalid") -cnotcontains
        [string]$document.postCreateIdentityReason -or
      $document.inventoryStage -isnot [string] -or
      $document.inventoryFailureStage -isnot [string] -or
      $document.inventoryOutputReason -isnot [string] -or
      @("none", "nativeExit", "stderr", "invokeFailure") -cnotcontains
        [string]$document.inventoryOutputReason -or
      $document.inventoryNativeExitCode -isnot [int] -or
      $document.inventoryChildFailureStage -isnot [string] -or
      @("none", "inputValidation", "validationMode", "validationQuota",
        "requestIdentityDuplicate", "requestIdentityInvalid",
        "responseIdentityUnknown", "responseIdentityInvalid",
        "responseIdentityConflict", "propertyQuery",
        "hostFailure", "stderr", "processInvoke") -cnotcontains
          [string]$document.inventoryChildFailureStage -or
      $document.inventoryCoverageStage -isnot [string] -or
      @("none", "rowCount", "rowIdentity", "batchCoverage") -cnotcontains
        [string]$document.inventoryCoverageStage -or
      $document.inventoryCoverageReason -isnot [string] -or
      @("none", "missing", "extra", "duplicate", "identityMismatch",
        "setMismatch") -cnotcontains
          [string]$document.inventoryCoverageReason -or
      $document.inventoryRequestedCount -isnot [int] -or
      $document.inventoryReturnedCount -isnot [int] -or
      $document.inventoryResponseRequestedCount -isnot [int] -or
      $document.inventoryResponseReturnedRowCount -isnot [int] -or
      $document.inventoryResponseUniqueOrdinalCount -isnot [int] -or
      $document.inventoryResponseUniqueOrdinalIgnoreCaseCount -isnot [int] -or
      $document.inventoryResponseDuplicateGroupCount -isnot [int] -or
      $document.inventoryResponseDuplicateMaxMultiplicity -isnot [int] -or
      $document.inventoryResponseCaseOnlyDuplicateCount -isnot [int] -or
      $document.inventoryResponseInvalidReason -isnot [string] -or
      $document.inventoryResponseInvalidCount -isnot [int] -or
      $document.inventoryResponseDataRelation -isnot [string] -or
      @("none", "identical", "conflicting", "invalid") -cnotcontains
        [string]$document.inventoryResponseDataRelation -or
      $document.inventoryDeviceCount -isnot [int] -or
      $document.inventoryCurrentBatchIndex -isnot [int] -or
      $document.inventoryTotalBatchCount -isnot [int] -or
      $document.inventoryHardwareBatchesCompleted -isnot [int] -or
      $document.inventoryDriverBatchesCompleted -isnot [int] -or
      $document.inventoryElapsedMilliseconds -isnot [int] -or
      $document.inventoryRunBudgetMilliseconds -isnot [int] -or
      $document.inventoryHardCapMilliseconds -isnot [int] -or
      $document.inventoryCleanupState -isnot [string] -or
      $document.inventoryRootPidZero -isnot [bool] -or
      $document.inventoryJobActiveProcesses -isnot [int] -or
      $document.finalizePreReadStage -isnot [string] -or
      @("notAttempted", "completed", "failed") -cnotcontains
        [string]$document.finalizePreReadStage -or
      $document.finalizePreReadReason -isnot [string] -or
      @("none", "resolve", "start", "timeout", "overflow", "pipe",
        "nativeExit", "stderr", "utf8", "json", "schema", "result",
        "cleanup") -cnotcontains [string]$document.finalizePreReadReason -or
      $document.finalizePreReadCleanupState -isnot [string] -or
      @("notRequired", "completed", "failed") -cnotcontains
        [string]$document.finalizePreReadCleanupState -or
      $document.finalizePreReadRootPidZero -isnot [bool] -or
      $document.finalizePreReadJobActiveProcesses -isnot [int] -or
      $document.finalizePreReadStdoutClosed -isnot [bool] -or
      $document.finalizePreReadStderrClosed -isnot [bool] -or
      $document.finalizePreReadDeviceCount -isnot [int] -or
      $document.finalizePreReadPresentDeviceCount -isnot [int] -or
      $document.finalizePreReadIdentitySha256 -isnot [string] -or
      [string]$document.finalizePreReadIdentitySha256 -cnotmatch
        '^[0-9a-f]{64}$' -or
      $document.finalizePreReadDriverBindingVerified -isnot [bool] -or
      ([string]$document.terminalReadbackState -ceq "failed" -and (
        [int]$document.observedDeviceCount -ne -1 -or
        [string]$document.residualDeviceState -cne "unknown" -or
        [bool]$document.driverBindingVerified)) -or
      ([string]$document.terminalReadbackState -ceq "completed" -and
        [int]$document.observedDeviceCount -lt 0)) {
    throw "virtualDisplayDiagnosticInvalid"
  }
  Assert-VirtualDisplayDiagnosticCorrelation $document
  return $document
}

$script:virtualDisplayInstallStage = "notStarted"
$script:virtualDisplayChildExit = -1
$script:virtualDisplayRemoveExit = -1
$script:virtualDisplayRemoveCount = 0
$script:virtualDisplayReadbackCode = "notAttempted"
$script:virtualDisplayMarkerStage = "notAttempted"
$script:virtualDisplayObservedDeviceCount = 0
$script:virtualDisplayPresentDeviceCount = 0
$script:virtualDisplayUniqueDeviceIdsSha256 =
  "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
$script:virtualDisplayCreateInvocationCount = 0
$script:virtualDisplayCreateInvocationIdSha256 =
  "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
$script:virtualDisplayPreCreateIdentitySha256 =
  "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
$script:virtualDisplayPostCreateIdentitySha256 =
  "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
$script:virtualDisplayPostCreateIdentityState = "notAttempted"
$script:virtualDisplayPostCreateIdentityReason = "none"
$script:virtualDisplayDriverBindingVerified = $false
$script:virtualDisplayFallbackAttempted = $false
$script:virtualDisplayFallbackExitCode = -1
$script:virtualDisplayFallbackStage = "notAttempted"
$script:virtualDisplayFallbackReason = "none"
$script:virtualDisplayDeviceRecovery = "notAttempted"
$script:virtualDisplayResidualState = "unknown"
$script:virtualDisplayCompensation = "notRequired"
$script:virtualDisplayCompensationFailureReason = "none"
$script:virtualDisplayTerminalReadbackState = "notAttempted"
$script:virtualDisplayTerminalReadbackReason = "none"
$script:virtualDisplayStdoutSha256 =
  "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"
$script:virtualDisplayStderrSha256 =
  "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"
$script:virtualDisplayProcessCleanup = "notRequired"
$script:virtualDisplayOutputFailureReason = "none"
$script:virtualDisplayCleanupPid = 0
$script:virtualDisplayFirstCleanupProven = $true
$script:virtualDisplayAuthorityRetained = $false
$script:virtualDisplaySecondaryAttempted = $false
$script:virtualDisplaySecondaryCompleted = $false
$script:virtualDisplayInventoryStage = "notAttempted"
$script:virtualDisplayInventoryFailureStage = "none"
$script:virtualDisplayInventoryOutputReason = "none"
$script:virtualDisplayInventoryNativeExitCode = -1
$script:virtualDisplayInventoryChildFailureStage = "none"
$script:virtualDisplayInventoryCoverageStage = "none"
$script:virtualDisplayInventoryCoverageReason = "none"
$script:virtualDisplayInventoryRequestedCount = -1
$script:virtualDisplayInventoryReturnedCount = -1
$script:virtualDisplayInventoryResponseRequestedCount = -1
$script:virtualDisplayInventoryResponseReturnedRowCount = -1
$script:virtualDisplayInventoryResponseUniqueOrdinalCount = -1
$script:virtualDisplayInventoryResponseUniqueOrdinalIgnoreCaseCount = -1
$script:virtualDisplayInventoryResponseDuplicateGroupCount = 0
$script:virtualDisplayInventoryResponseDuplicateMaxMultiplicity = 0
$script:virtualDisplayInventoryResponseCaseOnlyDuplicateCount = 0
$script:virtualDisplayInventoryResponseDataRelation = "none"
$script:virtualDisplayInventoryResponseInvalidReason = "none"
$script:virtualDisplayInventoryResponseInvalidCount = 0
$script:virtualDisplayInventoryDeviceCount = -1
$script:virtualDisplayInventoryCurrentBatchIndex = -1
$script:virtualDisplayInventoryTotalBatchCount = 0
$script:virtualDisplayInventoryHardwareBatchesCompleted = 0
$script:virtualDisplayInventoryDriverBatchesCompleted = 0
$script:virtualDisplayInventoryElapsedMilliseconds = 0
$script:virtualDisplayInventoryRunBudgetMilliseconds = 9000
$script:virtualDisplayInventoryHardCapMilliseconds = 10000
$script:virtualDisplayInventoryCleanupState = "notRequired"
$script:virtualDisplayInventoryRootPidZero = $true
$script:virtualDisplayInventoryJobActiveProcesses = 0
$script:virtualDisplayInventoryFailureLatched = $false
$script:virtualDisplayFinalizePreReadStage = "notAttempted"
$script:virtualDisplayFinalizePreReadReason = "none"
$script:virtualDisplayFinalizePreReadCleanupState = "notRequired"
$script:virtualDisplayFinalizePreReadRootPidZero = $true
$script:virtualDisplayFinalizePreReadJobActiveProcesses = 0
$script:virtualDisplayFinalizePreReadStdoutClosed = $false
$script:virtualDisplayFinalizePreReadStderrClosed = $false
$script:virtualDisplayFinalizePreReadDeviceCount = -1
$script:virtualDisplayFinalizePreReadPresentDeviceCount = -1
$script:virtualDisplayFinalizePreReadIdentitySha256 =
  "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
$script:virtualDisplayFinalizePreReadDriverBindingVerified = $false

function Invoke-VirtualDisplayInstaller(
    [string]$InstallerPath,
    [int]$TimeoutMilliseconds = 120000,
    [ValidateSet(
      "none", "assign", "resume", "startTerminate", "startWait",
      "jobTerminate", "jobAccounting", "retain",
      "secondaryTerminate", "secondaryWait", "secondaryAccounting", "read",
      "terminate", "wait", "pipe")]
    [string]$ValidationFault = "none") {
  if ($TimeoutMilliseconds -lt 1000 -or $TimeoutMilliseconds -gt 120000) {
    throw "virtualDisplayInstallerUnavailable"
  }
  $script:virtualDisplayOutputFailureReason = "none"
  try {
    $jobProcess = [LigaseJobProcess]::Start(
      (Join-Path $env:SystemRoot "System32\cmd.exe"),
      $InstallerPath, (Split-Path -Parent $InstallerPath),
      $ValidationFault)
  } catch {
    $script:virtualDisplayFirstCleanupProven =
      [bool][LigaseJobProcess]::LastCleanupProven
    $script:virtualDisplayAuthorityRetained =
      [bool][LigaseJobProcess]::AuthorityRetained
    $script:virtualDisplayCleanupPid =
      [int][LigaseJobProcess]::LastCleanupPid
    if ($script:virtualDisplayCleanupPid -ne 0) {
      if ([LigaseJobProcess]::SecondaryContainment($ValidationFault)) {
        $script:virtualDisplayProcessCleanup = "completed"
        $script:virtualDisplayCleanupPid = 0
      } else {
        $script:virtualDisplayProcessCleanup = "failed"
      }
    } elseif ([LigaseJobProcess]::LastCleanupProven) {
      $script:virtualDisplayProcessCleanup = "completed"
    }
    $script:virtualDisplaySecondaryAttempted =
      [bool][LigaseJobProcess]::SecondaryAttempted
    $script:virtualDisplaySecondaryCompleted =
      [bool][LigaseJobProcess]::SecondaryCompleted
    throw "virtualDisplayInstallerCleanupFailed"
  }
  try {
    $process = $jobProcess.Process
    $stdoutBuffer = [byte[]]::new(128)
    $stderrBuffer = [byte[]]::new(128)
    $stdoutBytes = [byte[]]::new(512)
    $stderrBytes = [byte[]]::new(512)
    $stdoutLength = 0
    $stderrLength = 0
    try {
      if ($ValidationFault -ceq "read") {
        throw [IO.IOException]::new("validationReadFault")
      }
      $stdoutTask = $jobProcess.StandardOutput.ReadAsync(
        $stdoutBuffer, 0, $stdoutBuffer.Length)
      $stderrTask = $jobProcess.StandardError.ReadAsync(
        $stderrBuffer, 0, $stderrBuffer.Length)
    } catch {
      if (-not $jobProcess.Terminate() -or
          -not $jobProcess.WaitForRoot(5000) -or
          -not $jobProcess.HasNoActiveProcesses()) {
        $script:virtualDisplayProcessCleanup = "failed"
        throw "virtualDisplayInstallerCleanupFailed"
      }
      $script:virtualDisplayProcessCleanup = "completed"
      throw "virtualDisplayInstallerOutputUnavailable"
    }
    $stdoutClosed = $false
    $stderrClosed = $false
    $overflow = $false
    $pipeFault = $false
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (-not (
        $jobProcess.HasNoActiveProcesses() -and
        $stdoutClosed -and $stderrClosed)) {
      if ($stdoutTask.IsCompleted) {
        try {
          $count = $stdoutTask.GetAwaiter().GetResult()
        } catch {
          $pipeFault = $true
          break
        }
        if ($count -eq 0) {
          $stdoutClosed = $true
        } elseif ($stdoutLength + $count -gt 512) {
          $overflow = $true
          break
        } else {
          [Array]::Copy($stdoutBuffer, 0, $stdoutBytes, $stdoutLength, $count)
          $stdoutLength += $count
          $stdoutTask = $jobProcess.StandardOutput.ReadAsync(
            $stdoutBuffer, 0, $stdoutBuffer.Length)
        }
      }
      if ($stderrTask.IsCompleted) {
        try {
          $count = $stderrTask.GetAwaiter().GetResult()
        } catch {
          $pipeFault = $true
          break
        }
        if ($count -eq 0) {
          $stderrClosed = $true
        } elseif ($stderrLength + $count -gt 512) {
          $overflow = $true
          break
        } else {
          [Array]::Copy($stderrBuffer, 0, $stderrBytes, $stderrLength, $count)
          $stderrLength += $count
          $stderrTask = $jobProcess.StandardError.ReadAsync(
            $stderrBuffer, 0, $stderrBuffer.Length)
        }
      }
      if ($clock.ElapsedMilliseconds -ge $TimeoutMilliseconds) { break }
      if (-not (
          $jobProcess.HasNoActiveProcesses() -and
          $stdoutClosed -and $stderrClosed)) {
        Start-Sleep -Milliseconds 10
      }
    }
    $stdoutExact = [byte[]]::new($stdoutLength)
    $stderrExact = [byte[]]::new($stderrLength)
    [Array]::Copy($stdoutBytes, $stdoutExact, $stdoutLength)
    [Array]::Copy($stderrBytes, $stderrExact, $stderrLength)
    $script:virtualDisplayStdoutSha256 = Get-ByteSha256 $stdoutExact
    $script:virtualDisplayStderrSha256 = Get-ByteSha256 $stderrExact
    if ($overflow -or $pipeFault -or -not (
        $jobProcess.HasNoActiveProcesses() -and
        $stdoutClosed -and $stderrClosed)) {
      $terminated = $jobProcess.Terminate()
      if (-not $terminated) {
        $script:virtualDisplayProcessCleanup = "failed"
        throw "virtualDisplayInstallerCleanupFailed"
      }
      $cleanupClock = [Diagnostics.Stopwatch]::StartNew()
      while ($cleanupClock.ElapsedMilliseconds -lt 5000 -and
          -not ($jobProcess.HasNoActiveProcesses() -and
            $stdoutClosed -and $stderrClosed)) {
        if (-not $stdoutClosed -and $stdoutTask.IsCompleted) {
          try {
            $count = $stdoutTask.GetAwaiter().GetResult()
            if ($count -eq 0) {
              $stdoutClosed = $true
            } else {
              $stdoutTask = $jobProcess.StandardOutput.ReadAsync(
                $stdoutBuffer, 0, $stdoutBuffer.Length)
            }
          } catch { throw "virtualDisplayInstallerCleanupFailed" }
        }
        if (-not $stderrClosed -and $stderrTask.IsCompleted) {
          try {
            $count = $stderrTask.GetAwaiter().GetResult()
            if ($count -eq 0) {
              $stderrClosed = $true
            } else {
              $stderrTask = $jobProcess.StandardError.ReadAsync(
                $stderrBuffer, 0, $stderrBuffer.Length)
            }
          } catch { throw "virtualDisplayInstallerCleanupFailed" }
        }
        if (-not ($jobProcess.HasNoActiveProcesses() -and
            $stdoutClosed -and $stderrClosed)) {
          Start-Sleep -Milliseconds 10
        }
      }
      if ($ValidationFault -in @("terminate", "wait", "pipe") -or
          -not $jobProcess.WaitForRoot(0) -or
          -not $jobProcess.HasNoActiveProcesses() -or
          -not $stdoutClosed -or -not $stderrClosed) {
        $script:virtualDisplayProcessCleanup = "failed"
        throw "virtualDisplayInstallerCleanupFailed"
      }
      $script:virtualDisplayProcessCleanup = "completed"
      if ($overflow) { throw "virtualDisplayInstallerOutputOverflow" }
      if ($pipeFault) { throw "virtualDisplayInstallerOutputUnavailable" }
      throw "virtualDisplayInstallerTimeout"
    }
    try {
      $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
      $stdout = $strictUtf8.GetString($stdoutExact)
      $stderr = $strictUtf8.GetString($stderrExact)
    } catch {
      $script:virtualDisplayOutputFailureReason = "encodingInvalid"
      throw "virtualDisplayInstallerOutputInvalid"
    }
    return [ordered]@{
      exitCode = [int]$jobProcess.ExitCode
      stdout = $stdout
      stderr = $stderr
    }
  } finally {
    $jobProcess.Dispose()
  }
}

function Assert-VirtualDisplayInstallerTuple(
    $Result,
    [ValidateSet("install", "removeOne", "removeInstance")]
    [string]$Mode = "install") {
  $installerExit = [int]$Result.exitCode
  $script:virtualDisplayChildExit = $installerExit
  if (-not [string]::IsNullOrEmpty([string]$Result.stderr)) {
    throw "virtualDisplayInstallerOutputInvalid"
  }
  $closed = ([string]$Result.stdout).Trim()
  if ($closed.Length -gt 512 -or $closed -notmatch
      '^LIGASE_VDISPLAY_V1\|stage=([A-Za-z]+)\|nativeExit=([0-9]+)(?:\|removeExit=([0-9]+)\|removeCount=([0-9]+))?$') {
    throw "virtualDisplayInstallerOutputInvalid"
  }
  $stage = [string]$Matches[1]
  $nativeExit = [int]$Matches[2]
  $script:virtualDisplayInstallStage = $stage
  $script:virtualDisplayRemoveExit = if ($Matches[3]) {
    [int]$Matches[3]
  } else { -1 }
  $script:virtualDisplayRemoveCount = if ($Matches[4]) {
    [int]$Matches[4]
  } else { 0 }
  $expectedStage = switch ($installerExit) {
    0 {
      if ($Mode -ceq "removeOne") { "deviceRemove" }
      elseif ($Mode -ceq "removeInstance") { "deviceRemoveFallback" }
      else { "completed" }
    }
    20 { "toolValidation" }
    21 { "certificateRoot" }
    22 { "certificatePublisher" }
    23 { "deviceCreate" }
    24 { "driverPackageInstall" }
    25 { "deviceRemove" }
    26 { "deviceRemoveFallback" }
    default { "none" }
  }
  if ($expectedStage -ceq "none" -or $stage -cne $expectedStage -or
      (($installerExit -eq 0) -ne ($nativeExit -eq 0))) {
    throw "virtualDisplayInstallerOutputInvalid"
  }
  if ($Mode -ceq "removeOne" -and (
      ($installerExit -eq 0 -and (
        $nativeExit -ne 0 -or $script:virtualDisplayRemoveExit -ne 0 -or
        $script:virtualDisplayRemoveCount -ne 1)) -or
      ($installerExit -eq 25 -and (
        $nativeExit -eq 0 -or
        $script:virtualDisplayRemoveExit -ne $nativeExit -or
        $script:virtualDisplayRemoveCount -ne 0)) -or
      $installerExit -notin @(0, 25))) {
    throw "virtualDisplayInstallerOutputInvalid"
  }
  if ($Mode -ceq "removeInstance" -and (
      ($installerExit -eq 0 -and (
        $nativeExit -ne 0 -or $script:virtualDisplayRemoveExit -ne 0 -or
        $script:virtualDisplayRemoveCount -ne 1)) -or
      ($installerExit -eq 26 -and (
        $nativeExit -eq 0 -or
        $script:virtualDisplayRemoveExit -ne $nativeExit -or
        $script:virtualDisplayRemoveCount -ne 0)) -or
      $installerExit -notin @(0, 26))) {
    throw "virtualDisplayInstallerOutputInvalid"
  }
  if ($Mode -ceq "install" -and $installerExit -in @(0, 23, 24) -and (
      $script:virtualDisplayRemoveExit -ne 0 -or
      $script:virtualDisplayRemoveCount -ne 0)) {
    throw "virtualDisplayInstallerOutputInvalid"
  }
  if ($installerExit -ne 0) {
    throw $(switch ($installerExit) {
      20 { "virtualDisplayInstallerToolUnavailable" }
      21 { "virtualDisplayCertificateRootFailed" }
      22 { "virtualDisplayCertificatePublisherFailed" }
      23 { "virtualDisplayDeviceCreateFailed" }
      24 { "virtualDisplayDriverPackageInstallFailed" }
      25 { "virtualDisplayDeviceRemoveFailed" }
      26 { "virtualDisplayDeviceRemoveFallbackFailed" }
    })
  }
}

function Set-VirtualDisplayResidualAuthority($Snapshot) {
  $count = [int]$Snapshot.deviceCount
  $script:virtualDisplayObservedDeviceCount = $count
  $script:virtualDisplayPresentDeviceCount = [int]$Snapshot.presentDeviceCount
  $script:virtualDisplayUniqueDeviceIdsSha256 =
    [string]$Snapshot.uniqueDeviceIdsSha256
  $script:virtualDisplayDriverBindingVerified =
    [bool]$Snapshot.driverBindingVerified
  $script:virtualDisplayResidualState = if (
      [string]$Snapshot.machineCode -ceq "virtualDisplayReadbackFailed") {
    "unknown"
  } elseif ($count -eq 0) {
    "zero"
  } elseif ($count -gt 1) {
    "multiple"
  } elseif ([bool]$Snapshot.driverBindingVerified) {
    "exactOneBound"
  } else {
    "exactOneUnbound"
  }
}

function Set-VirtualDisplayTerminalResidualAuthority(
    [scriptblock]$SnapshotProvider) {
  try {
    $terminalSnapshot = & $SnapshotProvider
  } catch {
    $script:virtualDisplayTerminalReadbackState = "failed"
    $script:virtualDisplayTerminalReadbackReason = "unavailable"
    $script:virtualDisplayObservedDeviceCount = -1
    $script:virtualDisplayPresentDeviceCount = -1
    $script:virtualDisplayUniqueDeviceIdsSha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayResidualState = "unknown"
    $script:virtualDisplayDriverBindingVerified = $false
    return $false
  }
  if ($null -eq $terminalSnapshot -or
      $terminalSnapshot.deviceCount -isnot [int] -or
      [int]$terminalSnapshot.deviceCount -lt 0 -or
      [int]$terminalSnapshot.deviceCount -gt 16 -or
      $terminalSnapshot.presentDeviceCount -isnot [int] -or
      [int]$terminalSnapshot.presentDeviceCount -lt 0 -or
      [int]$terminalSnapshot.presentDeviceCount -gt
        [int]$terminalSnapshot.deviceCount -or
      $terminalSnapshot.uniqueDeviceIdsSha256 -isnot [string] -or
      [string]$terminalSnapshot.uniqueDeviceIdsSha256 -cnotmatch
        '^[0-9a-f]{64}$' -or
      $terminalSnapshot.driverBindingVerified -isnot [bool] -or
      [string]$terminalSnapshot.machineCode -ceq
        "virtualDisplayReadbackFailed") {
    $script:virtualDisplayTerminalReadbackState = "failed"
    $script:virtualDisplayTerminalReadbackReason = "invalid"
    $script:virtualDisplayObservedDeviceCount = -1
    $script:virtualDisplayPresentDeviceCount = -1
    $script:virtualDisplayUniqueDeviceIdsSha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayResidualState = "unknown"
    $script:virtualDisplayDriverBindingVerified = $false
    return $false
  }
  Set-VirtualDisplayResidualAuthority $terminalSnapshot
  $script:virtualDisplayTerminalReadbackState = "completed"
  $script:virtualDisplayTerminalReadbackReason = "none"
  return $true
}

function Set-VirtualDisplayPostCreateIdentityAuthority {
  if ($script:virtualDisplayCreateInvocationCount -ne 1) { return }
  if ($script:virtualDisplayTerminalReadbackState -ceq "completed") {
    $script:virtualDisplayPostCreateIdentitySha256 =
      [string]$script:virtualDisplayUniqueDeviceIdsSha256
    $script:virtualDisplayPostCreateIdentityState = "completed"
    $script:virtualDisplayPostCreateIdentityReason = "none"
  } else {
    $script:virtualDisplayPostCreateIdentitySha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayPostCreateIdentityState = "failed"
    $script:virtualDisplayPostCreateIdentityReason =
      [string]$script:virtualDisplayTerminalReadbackReason
  }
}

function Invoke-VirtualDisplayRemovalReconciliation(
    [string]$InstallerPath,
    [scriptblock]$SnapshotProvider,
    [scriptblock]$RemoveInvoker,
    [scriptblock]$FallbackInvoker,
    [int]$SettleMilliseconds = 5000,
    [int]$TotalMilliseconds = 30000) {
  if ($null -eq $SnapshotProvider -or $null -eq $RemoveInvoker -or
      $null -eq $FallbackInvoker -or
      $SettleMilliseconds -lt 100 -or $SettleMilliseconds -gt 5000 -or
      $TotalMilliseconds -lt $SettleMilliseconds -or
      $TotalMilliseconds -gt 30000) {
    throw "virtualDisplayDeviceRemoveFailed"
  }
  $totalClock = [Diagnostics.Stopwatch]::StartNew()
  $removed = 0
  while ($true) {
    $before = & $SnapshotProvider
    $beforeCount = [int]$before.deviceCount
    if ($beforeCount -lt 0 -or $beforeCount -gt 16 -or
        ([string]$before.state -ceq "failed" -and
          [string]$before.machineCode -notin @(
            "virtualDisplayDeviceCountInvalid",
            "virtualDisplayDriverBindingMissing"))) {
      throw "virtualDisplayDeviceRemoveReadbackFailed"
    }
    Set-VirtualDisplayResidualAuthority $before
    if ($beforeCount -eq 0) {
      $zeroEpoch = [string]$before.uniqueDeviceIdsSha256
      if ($zeroEpoch -cnotmatch '^[0-9a-f]{64}$') {
        $script:virtualDisplayDeviceRecovery = "failed"
        $script:virtualDisplayResidualState = "unknown"
        $script:virtualDisplayDriverBindingVerified = $false
        throw "virtualDisplayDeviceZeroProofFailed"
      }
      $zeroProofClock = [Diagnostics.Stopwatch]::StartNew()
      $zeroProofSamples = 1
      while ($zeroProofSamples -lt 3 -or
          $zeroProofClock.ElapsedMilliseconds -lt
            [Math]::Min(500, $SettleMilliseconds)) {
        if ($totalClock.ElapsedMilliseconds -ge $TotalMilliseconds) {
          $script:virtualDisplayDeviceRecovery = "failed"
          throw "virtualDisplayDeviceZeroProofFailed"
        }
        Start-Sleep -Milliseconds 50
        try {
          $zeroReadback = & $SnapshotProvider
        } catch {
          $script:virtualDisplayDeviceRecovery = "failed"
          $script:virtualDisplayResidualState = "unknown"
          $script:virtualDisplayDriverBindingVerified = $false
          throw "virtualDisplayDeviceZeroProofFailed"
        }
        if ($totalClock.ElapsedMilliseconds -ge $TotalMilliseconds) {
          $script:virtualDisplayDeviceRecovery = "failed"
          throw "virtualDisplayDeviceZeroProofFailed"
        }
        $zeroReadbackCount = [int]$zeroReadback.deviceCount
        if ($zeroReadbackCount -lt 0 -or $zeroReadbackCount -gt 16 -or
            ([string]$zeroReadback.state -ceq "failed" -and
              [string]$zeroReadback.machineCode -notin @(
                "virtualDisplayDeviceCountInvalid",
                "virtualDisplayDriverBindingMissing"))) {
          $script:virtualDisplayDeviceRecovery = "failed"
          $script:virtualDisplayResidualState = "unknown"
          $script:virtualDisplayDriverBindingVerified = $false
          throw "virtualDisplayDeviceZeroProofFailed"
        }
        Set-VirtualDisplayResidualAuthority $zeroReadback
        if ($zeroReadbackCount -ne 0 -or
            [string]$zeroReadback.uniqueDeviceIdsSha256 -cne $zeroEpoch) {
          $script:virtualDisplayDeviceRecovery = "failed"
          throw "virtualDisplayDeviceZeroProofFailed"
        }
        $zeroProofSamples++
      }
      if ($totalClock.ElapsedMilliseconds -ge $TotalMilliseconds) {
        $script:virtualDisplayDeviceRecovery = "failed"
        throw "virtualDisplayDeviceZeroProofFailed"
      }
      $script:virtualDisplayRemoveExit = 0
      $script:virtualDisplayRemoveCount = $removed
      $script:virtualDisplayDeviceRecovery = "completed"
      return $removed
    }
    if ($removed -ge 16 -or
        $totalClock.ElapsedMilliseconds -ge $TotalMilliseconds) {
      throw "virtualDisplayDeviceRemoveSettleFailed"
    }
    $script:virtualDisplayInstallStage = "deviceRemove"
    $script:virtualDisplayDeviceRecovery = "inProgress"
    try {
      $removeResult = & $RemoveInvoker $InstallerPath
      Assert-VirtualDisplayInstallerTuple $removeResult "removeOne"
    } catch {
      if ([string]$_.Exception.Message -cne
          "virtualDisplayDeviceRemoveFailed" -or
          [int]$script:virtualDisplayRemoveExit -ne 6) {
        $script:virtualDisplayDeviceRecovery = "failed"
        throw
      }
      $primaryChildExit = [int]$script:virtualDisplayChildExit
      $primaryRemoveExit = [int]$script:virtualDisplayRemoveExit
      $primaryRemoveCount = [int]$script:virtualDisplayRemoveCount
      $instanceIds = @($before.removalInstanceIds)
      if ($instanceIds.Count -ne $beforeCount -or
          @($instanceIds | Where-Object {
            $_ -isnot [string] -or
            [string]$_ -cnotmatch '(?i)^ROOT\\DISPLAY\\[0-9A-F]{4}$'
          }).Count -ne 0 -or
          @($instanceIds | Sort-Object -Unique).Count -ne $instanceIds.Count) {
        $script:virtualDisplayDeviceRecovery = "failed"
        throw "virtualDisplayDeviceRemoveFallbackFailed"
      }
      $script:virtualDisplayFallbackAttempted = $true
      $script:virtualDisplayFallbackExitCode = -1
      $script:virtualDisplayFallbackStage = "processInvoke"
      $script:virtualDisplayFallbackReason = "unknown"
      $fallbackResultReturned = $false
      try {
        $fallbackResult = & $FallbackInvoker $InstallerPath (
          [string](@($instanceIds | Sort-Object -CaseSensitive)[0]))
        $fallbackResultReturned = $true
        $script:virtualDisplayFallbackStage = "tupleValidation"
        Assert-VirtualDisplayInstallerTuple $fallbackResult "removeInstance"
        $script:virtualDisplayFallbackExitCode = 0
        $script:virtualDisplayFallbackStage = "completed"
        $script:virtualDisplayFallbackReason = "none"
      } catch {
        $fallbackExceptionCode = [string]$_.Exception.Message
        if ([string]$script:virtualDisplayFallbackReason -ceq "unknown") {
          switch ($fallbackExceptionCode) {
            "virtualDisplayTrustedToolUnavailable" {
              $script:virtualDisplayFallbackStage = "trustedToolResolve"
              $script:virtualDisplayFallbackReason = "trustedToolUnavailable"
            }
            "virtualDisplayInstallerTimeout" {
              $script:virtualDisplayFallbackReason = "timeout"
            }
            "virtualDisplayInstallerOutputInvalid" {
              $script:virtualDisplayFallbackReason = if (
                [string]$script:virtualDisplayOutputFailureReason -ceq
                  "encodingInvalid") {
                "encodingInvalid"
              } else { "outputInvalid" }
            }
            "virtualDisplayInstallerEncodingInvalid" {
              $script:virtualDisplayFallbackReason = "encodingInvalid"
            }
            "virtualDisplayInstallerOutputOverflow" {
              $script:virtualDisplayFallbackReason = "outputOverflow"
            }
            "virtualDisplayInstallerOutputUnavailable" {
              $script:virtualDisplayFallbackReason = "outputUnavailable"
            }
            "virtualDisplayInstallerCleanupFailed" {
              $script:virtualDisplayFallbackReason = "processStartOrCleanup"
            }
          }
        }
        if ($fallbackResultReturned -and
            $fallbackExceptionCode -ceq
            "virtualDisplayDeviceRemoveFallbackFailed") {
          $script:virtualDisplayFallbackExitCode =
            [int]$script:virtualDisplayRemoveExit
          $script:virtualDisplayFallbackReason = if (
              [int]$script:virtualDisplayRemoveExit -eq 0) {
            "tupleInvalid"
          } else { "nativeFailure" }
        } elseif ([string]$script:virtualDisplayFallbackReason -ceq "none") {
          $script:virtualDisplayFallbackReason = "unknown"
        }
        $script:virtualDisplayChildExit = $primaryChildExit
        $script:virtualDisplayRemoveExit = $primaryRemoveExit
        $script:virtualDisplayRemoveCount = $primaryRemoveCount
        $script:virtualDisplayDeviceRecovery = "failed"
        throw "virtualDisplayDeviceRemoveFallbackFailed"
      }
    }
    $settleClock = [Diagnostics.Stopwatch]::StartNew()
    $progress = $false
    while ($totalClock.ElapsedMilliseconds -lt $TotalMilliseconds -and
        $settleClock.ElapsedMilliseconds -lt $SettleMilliseconds) {
      try {
        $after = & $SnapshotProvider
      } catch {
        $script:virtualDisplayRemoveCount = $removed
        $script:virtualDisplayDeviceRecovery = "failed"
        $script:virtualDisplayResidualState = "unknown"
        $script:virtualDisplayDriverBindingVerified = $false
        throw "virtualDisplayDeviceRemoveReadbackFailed"
      }
      $afterCount = [int]$after.deviceCount
      if ($afterCount -lt 0 -or $afterCount -gt 16 -or
          ([string]$after.state -ceq "failed" -and
            [string]$after.machineCode -notin @(
              "virtualDisplayDeviceCountInvalid",
              "virtualDisplayDriverBindingMissing"))) {
        $script:virtualDisplayRemoveCount = $removed
        $script:virtualDisplayDeviceRecovery = "failed"
        $script:virtualDisplayResidualState = "unknown"
        $script:virtualDisplayDriverBindingVerified = $false
        throw "virtualDisplayDeviceRemoveReadbackFailed"
      }
      Set-VirtualDisplayResidualAuthority $after
      if ($afterCount -lt $beforeCount) {
        $progress = $true
        break
      }
      Start-Sleep -Milliseconds 50
    }
    if (-not $progress) {
      $script:virtualDisplayRemoveCount = $removed
      $script:virtualDisplayDeviceRecovery = "failed"
      throw "virtualDisplayDeviceRemoveSettleFailed"
    }
    $removed++
    $script:virtualDisplayRemoveCount = $removed
  }
}

function Invoke-VirtualDisplayRemovalValidation([string]$Root) {
  $fullRoot = [IO.Path]::GetFullPath($Root)
  if ($env:LIGASE_INSTALL_VALIDATION_HARNESS -cne "1" -or
      [string]$env:LIGASE_VIRTUAL_DISPLAY_REMOVAL_VALIDATION_ROOT -cne
        $fullRoot -or [IO.Path]::GetPathRoot($fullRoot) -cne "D:\") {
    throw "virtualDisplayValidationUnavailable"
  }
  $casePath = Join-Path $fullRoot "virtual-display-removal-case.json"
  $raw = [IO.File]::ReadAllText(
    $casePath, [Text.UTF8Encoding]::new($false, $true))
  if (-not [LigaseStrictJson]::HasUniqueProperties($raw)) {
    throw "virtualDisplayValidationUnavailable"
  }
  $case = $raw | ConvertFrom-Json
  Assert-ClosedProperties $case @(
    "schemaVersion", "counts", "removeExits", "fallbackExits",
    "fallbackFault", "readbackFaultAt", "settleMilliseconds",
    "totalMilliseconds", "identityEpochs",
    "snapshotDelayMilliseconds") "virtualDisplayRemovalValidation"
  if ($case.schemaVersion -ne 1 -or $case.counts -isnot [array] -or
      $case.removeExits -isnot [array] -or
      $case.fallbackExits -isnot [array] -or
      $case.identityEpochs -isnot [array] -or
      $case.identityEpochs.Count -ne $case.counts.Count -or
      @($case.identityEpochs | Where-Object {
        $_ -isnot [string] -or [string]$_ -cnotmatch '^[0-9a-f]{64}$'
      }).Count -ne 0 -or
      $case.snapshotDelayMilliseconds -isnot [array] -or
      $case.snapshotDelayMilliseconds.Count -ne $case.counts.Count -or
      @($case.snapshotDelayMilliseconds | Where-Object {
        $_ -isnot [int] -or [int]$_ -lt 0 -or [int]$_ -gt 1000
      }).Count -ne 0 -or
      $case.fallbackFault -isnot [string] -or
      @("none", "trustedTool", "timeout", "output", "encoding", "overflow",
        "unavailable", "cleanup") -cnotcontains
        [string]$case.fallbackFault -or
      $case.readbackFaultAt -isnot [int] -or
      $case.readbackFaultAt -lt -1 -or $case.readbackFaultAt -gt 17 -or
      $case.counts.Count -lt 1 -or $case.counts.Count -gt 18 -or
      $case.removeExits.Count -gt 16 -or $case.fallbackExits.Count -gt 16) {
    throw "virtualDisplayValidationUnavailable"
  }
  foreach ($count in @($case.counts)) {
    if ($count -isnot [int] -or $count -lt 0 -or $count -gt 16) {
      throw "virtualDisplayValidationUnavailable"
    }
  }
  foreach ($removeExit in @($case.removeExits)) {
    if ($removeExit -isnot [int] -or
        $removeExit -lt 0 -or $removeExit -gt 65535) {
      throw "virtualDisplayValidationUnavailable"
    }
  }
  foreach ($fallbackExit in @($case.fallbackExits)) {
    if ($fallbackExit -isnot [int] -or
        $fallbackExit -lt 0 -or $fallbackExit -gt 65535) {
      throw "virtualDisplayValidationUnavailable"
    }
  }
  $validationState = @{
    snapshotIndex = 0
    removeIndex = 0
    fallbackIndex = 0
  }
  $snapshotProvider = {
    $delayIndex = [Math]::Min(
      [int]$validationState.snapshotIndex,
      $case.snapshotDelayMilliseconds.Count - 1)
    $snapshotDelay =
      [int]$case.snapshotDelayMilliseconds[$delayIndex]
    if ($snapshotDelay -gt 0) {
      Start-Sleep -Milliseconds $snapshotDelay
    }
    if ([int]$case.readbackFaultAt -eq
        [int]$validationState.snapshotIndex) {
      $validationState.snapshotIndex =
        [int]$validationState.snapshotIndex + 1
      throw "validationReadbackFault"
    }
    $count = [int]$case.counts[
      [Math]::Min(
        [int]$validationState.snapshotIndex, $case.counts.Count - 1)]
    $validationState.snapshotIndex =
      [int]$validationState.snapshotIndex + 1
    [ordered]@{
      state = if ($count -eq 0) {
        "notInstalled"
      } elseif ($count -eq 1) {
        "available"
      } else {
        "failed"
      }
      machineCode = if ($count -eq 0) {
        "virtualDisplayNotInstalled"
      } elseif ($count -eq 1) {
        "available"
      } else {
        "virtualDisplayDeviceCountInvalid"
      }
      deviceCount = $count
      presentDeviceCount = $count
      uniqueDeviceIdsSha256 = [string]$case.identityEpochs[
        [Math]::Min(
          [int]$validationState.snapshotIndex - 1,
          $case.identityEpochs.Count - 1)]
      driverBindingVerified = $count -eq 1
      removalInstanceIds = @(
        0..([Math]::Max(0, $count - 1)) | ForEach-Object {
          if ($count -gt 0) { "ROOT\DISPLAY\{0:X4}" -f $_ }
        } | Where-Object { $null -ne $_ })
    }
  }.GetNewClosure()
  $fallbackInvoker = {
    param([string]$UnusedPath, [string]$InstanceId)
    if ([string]$case.fallbackFault -cne "none") {
      if ([string]$case.fallbackFault -ceq "encoding") {
        $script:virtualDisplayOutputFailureReason = "encodingInvalid"
      }
      $script:virtualDisplayFallbackStage = if (
          [string]$case.fallbackFault -ceq "trustedTool") {
        "trustedToolResolve"
      } else { "processInvoke" }
      $script:virtualDisplayFallbackReason = switch (
          [string]$case.fallbackFault) {
        "trustedTool" { "trustedToolUnavailable" }
        "timeout" { "timeout" }
        "output" { "outputInvalid" }
        "encoding" { "encodingInvalid" }
        "overflow" { "outputOverflow" }
        "unavailable" { "outputUnavailable" }
        "cleanup" { "processStartOrCleanup" }
      }
      throw $(switch ([string]$case.fallbackFault) {
        "trustedTool" { "virtualDisplayTrustedToolUnavailable" }
        "timeout" { "virtualDisplayInstallerTimeout" }
        "output" { "virtualDisplayInstallerOutputInvalid" }
        "encoding" { "virtualDisplayInstallerEncodingInvalid" }
        "overflow" { "virtualDisplayInstallerOutputOverflow" }
        "unavailable" { "virtualDisplayInstallerOutputUnavailable" }
        "cleanup" { "virtualDisplayInstallerCleanupFailed" }
      })
    }
    if ($InstanceId -cnotmatch '(?i)^ROOT\\DISPLAY\\[0-9A-F]{4}$' -or
        [int]$validationState.fallbackIndex -ge $case.fallbackExits.Count) {
      return [ordered]@{
        exitCode = 26
        stdout = "LIGASE_VDISPLAY_V1|stage=deviceRemoveFallback|nativeExit=87|removeExit=87|removeCount=0"
        stderr = ""
      }
    }
    $exit = [int]$case.fallbackExits[
      [int]$validationState.fallbackIndex]
    $validationState.fallbackIndex =
      [int]$validationState.fallbackIndex + 1
    if ($exit -eq 0) {
      return [ordered]@{
        exitCode = 0
        stdout = "LIGASE_VDISPLAY_V1|stage=deviceRemoveFallback|nativeExit=0|removeExit=0|removeCount=1"
        stderr = ""
      }
    }
    [ordered]@{
      exitCode = 26
      stdout = "LIGASE_VDISPLAY_V1|stage=deviceRemoveFallback|nativeExit=$exit|removeExit=$exit|removeCount=0"
      stderr = ""
    }
  }.GetNewClosure()
  $removeInvoker = {
    param([string]$UnusedPath)
    if ([int]$validationState.removeIndex -ge $case.removeExits.Count) {
      return [ordered]@{
        exitCode = 25
        stdout = "LIGASE_VDISPLAY_V1|stage=deviceRemove|nativeExit=1460|removeExit=1460|removeCount=0"
        stderr = ""
      }
    }
    $exit = [int]$case.removeExits[[int]$validationState.removeIndex]
    $validationState.removeIndex = [int]$validationState.removeIndex + 1
    if ($exit -eq 0) {
      return [ordered]@{
        exitCode = 0
        stdout = "LIGASE_VDISPLAY_V1|stage=deviceRemove|nativeExit=0|removeExit=0|removeCount=1"
        stderr = ""
      }
    }
    [ordered]@{
      exitCode = 25
      stdout = "LIGASE_VDISPLAY_V1|stage=deviceRemove|nativeExit=$exit|removeExit=$exit|removeCount=0"
      stderr = ""
    }
  }.GetNewClosure()
  $code = "virtualDisplayRemoved"
  $success = $true
  try {
    $removed = Invoke-VirtualDisplayRemovalReconciliation "validation.cmd" `
      $snapshotProvider $removeInvoker $fallbackInvoker `
      ([int]$case.settleMilliseconds) `
      ([int]$case.totalMilliseconds)
  } catch {
    $code = [string]$_.Exception.Message
    $success = $false
    $removed = [int]$script:virtualDisplayRemoveCount
  }
  return [ordered]@{
    code = $code
    success = $success
    removeCalls = [int]$validationState.removeIndex
    fallbackCalls = [int]$validationState.fallbackIndex
    removeCount = $removed
    removeExitCode = [int]$script:virtualDisplayRemoveExit
    fallbackExitCode = [int]$script:virtualDisplayFallbackExitCode
    fallbackStage = [string]$script:virtualDisplayFallbackStage
    fallbackReason = [string]$script:virtualDisplayFallbackReason
    observedDeviceCount = [int]$script:virtualDisplayObservedDeviceCount
    snapshotReads = [int]$validationState.snapshotIndex
    deviceRecovery = [string]$script:virtualDisplayDeviceRecovery
    residualDeviceState = [string]$script:virtualDisplayResidualState
    terminalReadbackState =
      [string]$script:virtualDisplayTerminalReadbackState
    terminalReadbackReason =
      [string]$script:virtualDisplayTerminalReadbackReason
  }
}

function Invoke-VirtualDisplayInstallerValidation([string]$Root) {
  $fullRoot = [IO.Path]::GetFullPath($Root)
  if ($env:LIGASE_INSTALL_VALIDATION_HARNESS -cne "1" -or
      [string]$env:LIGASE_VIRTUAL_DISPLAY_PROCESS_VALIDATION_ROOT -cne
        $fullRoot -or [IO.Path]::GetPathRoot($fullRoot) -cne "D:\") {
    throw "virtualDisplayValidationUnavailable"
  }
  $mock = Join-Path $fullRoot "virtual-display-installer-mock.cmd"
  if (-not (Test-Path -LiteralPath $mock -PathType Leaf)) {
    throw "virtualDisplayValidationUnavailable"
  }
  $code = "virtualDisplayInstalled"
  $success = $true
  try {
    $timeout = if (
        $env:LIGASE_VIRTUAL_DISPLAY_PROCESS_TIMEOUT_MS -ceq "1000") {
      1000
    } else { 120000 }
    $fault = if ($env:LIGASE_VIRTUAL_DISPLAY_PROCESS_CLEANUP_FAULT -in @(
        "assign", "resume", "startTerminate", "startWait",
        "jobTerminate", "jobAccounting", "retain",
        "secondaryTerminate", "secondaryWait", "secondaryAccounting", "read",
        "terminate", "wait", "pipe")) {
      [string]$env:LIGASE_VIRTUAL_DISPLAY_PROCESS_CLEANUP_FAULT
    } else { "none" }
    $result = Invoke-VirtualDisplayInstaller $mock $timeout $fault
    Assert-VirtualDisplayInstallerTuple $result
  } catch {
    $code = [string]$_.Exception.Message
    $success = $false
  }
  return [ordered]@{
    code = $code
    success = $success
    installStage = [string]$script:virtualDisplayInstallStage
    childExitCode = [int]$script:virtualDisplayChildExit
    removeExitCode = [int]$script:virtualDisplayRemoveExit
    removeCount = [int]$script:virtualDisplayRemoveCount
    stdoutSha256 = [string]$script:virtualDisplayStdoutSha256
    stderrSha256 = [string]$script:virtualDisplayStderrSha256
    cleanupState = [string]$script:virtualDisplayProcessCleanup
    cleanupPid = [int]$script:virtualDisplayCleanupPid
    firstCleanupProven = [bool]$script:virtualDisplayFirstCleanupProven
    authorityRetained = [bool]$script:virtualDisplayAuthorityRetained
    secondaryContainmentAttempted =
      [bool]$script:virtualDisplaySecondaryAttempted
    secondaryContainmentCompleted =
      [bool]$script:virtualDisplaySecondaryCompleted
  }
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
  $evidenceDirectory = Split-Path -Parent $evidencePath
  if (-not (Test-Path -LiteralPath $evidenceDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
  }
  if ($Action -ne "ValidateVirtualDisplayDiagnosticProjection") {
    Set-SecureDataRootAcl $evidenceDirectory
  }
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
      aclInspectionReason = if (
        $script:transactionAclInspectionReason -ne "none") {
        $script:transactionAclInspectionReason
      } else { $EvidenceTransactionAclInspectionReason }
      emptyRootInspectionReason = if (
        $script:transactionEmptyRootInspectionReason -ne "none") {
        $script:transactionEmptyRootInspectionReason
      } else { $EvidenceTransactionEmptyRootInspectionReason }
      aclMutationOccurred = if ($script:transactionAclMutationOccurred) {
        $true
      } else { [bool]$EvidenceTransactionAclMutationOccurred }
      aclRollback = if ($script:transactionAclRollback -ne "notRequired") {
        $script:transactionAclRollback
      } else { $EvidenceTransactionAclRollback }
      recoveryAction = if ($script:transactionRecoveryAction -ne "none") {
        $script:transactionRecoveryAction
      } else { $EvidenceTransactionRecoveryAction }
      readbackStage = $script:transactionReadbackStage
      readbackReason = $script:transactionReadbackReason
    }
    failedField = if ($EvidenceFailedField -eq "none") {
      $null
    } else {
      $EvidenceFailedField
    }
    components = $script:finalComponents
    virtualDisplay = if ($null -eq $script:virtualDisplayDiagnostic) {
      $null
    } else {
      $script:virtualDisplayDiagnostic
    }
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

$virtualDisplayPropertyBatchSize = 32
$virtualDisplayPropertyInventoryDeadlineMilliseconds = 10000
$virtualDisplayPropertyCleanupReserveMilliseconds = 1000

function Get-VirtualDisplaySnapshot {
  if ($env:LIGASE_INSTALL_VALIDATION_HARNESS -ceq "1" -and
      -not [string]::IsNullOrWhiteSpace(
        [string]$env:LIGASE_VIRTUAL_DISPLAY_READBACK_VALIDATION_ROOT)) {
    $validationRoot = [IO.Path]::GetFullPath(
      [string]$env:LIGASE_VIRTUAL_DISPLAY_READBACK_VALIDATION_ROOT)
    if ([IO.Path]::GetPathRoot($validationRoot) -cne "D:\") {
      throw "virtualDisplayValidationUnavailable"
    }
    $fixturePath = Join-Path $validationRoot "virtual-display-snapshot.json"
    $raw = [IO.File]::ReadAllText(
      $fixturePath, [Text.UTF8Encoding]::new($false, $true))
    if (-not [LigaseStrictJson]::HasUniqueProperties($raw)) {
      throw "virtualDisplayValidationUnavailable"
    }
    $fixture = $raw | ConvertFrom-Json
    Assert-ClosedProperties $fixture @(
      "schemaVersion", "inventoryState", "devices") (
      "virtualDisplayValidation")
    if ($fixture.schemaVersion -ne 1 -or
        [string]$fixture.inventoryState -cnotin @("available", "unavailable") -or
        $fixture.devices -isnot [array]) {
      throw "virtualDisplayValidationUnavailable"
    }
    if ([string]$fixture.inventoryState -ceq "unavailable") {
      throw "virtualDisplayReadbackFailed"
    }
    foreach ($device in @($fixture.devices)) {
      Assert-ClosedProperties $device @(
        "instanceId", "hardwareIds", "status", "present", "driverInf") (
        "virtualDisplayValidationDevice")
      if ([string]::IsNullOrWhiteSpace([string]$device.instanceId) -or
          $device.hardwareIds -isnot [array] -or
          $device.status -isnot [string] -or
          $device.present -isnot [bool] -or
          $device.driverInf -isnot [string]) {
        throw "virtualDisplayValidationUnavailable"
      }
    }
    return @($fixture.devices)
  }
  return @(Invoke-VirtualDisplayInventoryHelper)
}

function Set-VirtualDisplayInventoryDiagnostic([bool]$Succeeded) {
  if ($script:virtualDisplayInventoryFailureLatched) { return }
  $script:virtualDisplayInventoryElapsedMilliseconds = [Math]::Min(
    $virtualDisplayPropertyInventoryDeadlineMilliseconds,
    [Math]::Max(0, [int]$script:virtualDisplayInventoryElapsedMilliseconds))
  $script:virtualDisplayInventoryRunBudgetMilliseconds =
    $virtualDisplayPropertyInventoryDeadlineMilliseconds -
      $virtualDisplayPropertyCleanupReserveMilliseconds
  $script:virtualDisplayInventoryHardCapMilliseconds =
    $virtualDisplayPropertyInventoryDeadlineMilliseconds
  $script:virtualDisplayInventoryCleanupState =
    [string]$script:virtualDisplayChunkCleanupState
  $script:virtualDisplayInventoryRootPidZero =
    [bool]$script:virtualDisplayChunkRootPidZero
  $script:virtualDisplayInventoryJobActiveProcesses =
    [int]$script:virtualDisplayChunkJobActiveProcesses
  if ($Succeeded) {
    $script:virtualDisplayInventoryStage = "completed"
    $script:virtualDisplayInventoryFailureStage = "none"
    $script:virtualDisplayInventoryOutputReason = "none"
    $script:virtualDisplayInventoryNativeExitCode = -1
    $script:virtualDisplayInventoryChildFailureStage = "none"
    $script:virtualDisplayInventoryCoverageStage = "none"
    $script:virtualDisplayInventoryCoverageReason = "none"
    $script:virtualDisplayInventoryRequestedCount = -1
    $script:virtualDisplayInventoryReturnedCount = -1
    $script:virtualDisplayInventoryResponseRequestedCount = -1
    $script:virtualDisplayInventoryResponseReturnedRowCount = -1
    $script:virtualDisplayInventoryResponseUniqueOrdinalCount = -1
    $script:virtualDisplayInventoryResponseUniqueOrdinalIgnoreCaseCount = -1
    $script:virtualDisplayInventoryResponseDuplicateGroupCount = 0
    $script:virtualDisplayInventoryResponseDuplicateMaxMultiplicity = 0
    $script:virtualDisplayInventoryResponseCaseOnlyDuplicateCount = 0
    $script:virtualDisplayInventoryResponseDataRelation = "none"
    $script:virtualDisplayInventoryResponseInvalidReason = "none"
    $script:virtualDisplayInventoryResponseInvalidCount = 0
    return
  }
  $script:virtualDisplayInventoryFailureStage = switch (
      [string]$script:virtualDisplayChunkFailureStage) {
    "start" { "trustedRunnerStart" }
    "deadline" { "deadline" }
    "postDeadline" { "deadline" }
    "nativeExit" { "output" }
    "stderr" { "output" }
    "json" { "decode" }
    "shape" { "tuple" }
    "rowCount" { "coverage" }
    "rowIdentity" { "coverage" }
    "batchCoverage" { "coverage" }
    "globalCoverage" { "coverage" }
    "invoke" {
      if ([string]$script:virtualDisplayChunkCleanupState -ceq "failed") {
        "cleanup"
      } else { "output" }
    }
    default {
      if ([string]$script:virtualDisplayInventoryStage -ceq "allDevices") {
        "allDevices"
      } else { "input" }
    }
  }
  if ([string]$script:virtualDisplayInventoryFailureStage -ceq "output" -and
      [string]$script:virtualDisplayChunkCleanupState -ceq "failed") {
    $script:virtualDisplayInventoryFailureStage = "cleanup"
  }
  $script:virtualDisplayInventoryOutputReason = if (
      [string]$script:virtualDisplayInventoryFailureStage -ceq "output") {
    switch ([string]$script:virtualDisplayChunkFailureStage) {
      "nativeExit" { "nativeExit" }
      "stderr" { "stderr" }
      "invoke" { "invokeFailure" }
      default { throw "virtualDisplayDiagnosticInvalid" }
    }
  } else { "none" }
  if ([string]$script:virtualDisplayInventoryFailureStage -ceq "output") {
    $script:virtualDisplayInventoryNativeExitCode =
      [int]$script:virtualDisplayChunkNativeExitCode
    $script:virtualDisplayInventoryChildFailureStage =
      [string]$script:virtualDisplayChunkChildFailureStage
  } else {
    $script:virtualDisplayInventoryNativeExitCode = -1
    $script:virtualDisplayInventoryChildFailureStage = "none"
  }
  if ($script:virtualDisplayInventoryFailureStage -ceq "coverage") {
    $script:virtualDisplayInventoryCoverageStage =
      [string]$script:virtualDisplayChunkCoverageStage
    $script:virtualDisplayInventoryCoverageReason =
      [string]$script:virtualDisplayChunkCoverageReason
    $script:virtualDisplayInventoryRequestedCount =
      [int]$script:virtualDisplayChunkRequestedCount
    $script:virtualDisplayInventoryReturnedCount =
      [int]$script:virtualDisplayChunkReturnedCount
  } else {
    $script:virtualDisplayInventoryCoverageStage = "none"
    $script:virtualDisplayInventoryCoverageReason = "none"
    $script:virtualDisplayInventoryRequestedCount = -1
    $script:virtualDisplayInventoryReturnedCount = -1
  }
  if ($script:virtualDisplayInventoryFailureStage -ceq "output" -and
      [int]$script:virtualDisplayChunkNativeExitCode -in @(101, 103)) {
    $script:virtualDisplayInventoryResponseRequestedCount =
      [int]$script:virtualDisplayChunkDuplicateRequestedCount
    $script:virtualDisplayInventoryResponseReturnedRowCount =
      [int]$script:virtualDisplayChunkDuplicateReturnedRowCount
    $script:virtualDisplayInventoryResponseUniqueOrdinalCount =
      [int]$script:virtualDisplayChunkDuplicateUniqueOrdinalCount
    $script:virtualDisplayInventoryResponseUniqueOrdinalIgnoreCaseCount =
      [int]$script:virtualDisplayChunkDuplicateUniqueOrdinalIgnoreCaseCount
    $script:virtualDisplayInventoryResponseDuplicateGroupCount =
      [int]$script:virtualDisplayChunkDuplicateGroupCount
    $script:virtualDisplayInventoryResponseDuplicateMaxMultiplicity =
      [int]$script:virtualDisplayChunkDuplicateMaxMultiplicity
    $script:virtualDisplayInventoryResponseCaseOnlyDuplicateCount =
      [int]$script:virtualDisplayChunkDuplicateCaseOnlyCount
    $script:virtualDisplayInventoryResponseDataRelation =
      [string]$script:virtualDisplayChunkDuplicateDataRelation
    $script:virtualDisplayInventoryResponseInvalidReason =
      [string]$script:virtualDisplayChunkIdentityInvalidReason
    $script:virtualDisplayInventoryResponseInvalidCount =
      [int]$script:virtualDisplayChunkIdentityInvalidCount
  } else {
    $script:virtualDisplayInventoryResponseRequestedCount = -1
    $script:virtualDisplayInventoryResponseReturnedRowCount = -1
    $script:virtualDisplayInventoryResponseUniqueOrdinalCount = -1
    $script:virtualDisplayInventoryResponseUniqueOrdinalIgnoreCaseCount = -1
    $script:virtualDisplayInventoryResponseDuplicateGroupCount = 0
    $script:virtualDisplayInventoryResponseDuplicateMaxMultiplicity = 0
    $script:virtualDisplayInventoryResponseCaseOnlyDuplicateCount = 0
    $script:virtualDisplayInventoryResponseDataRelation = "none"
    $script:virtualDisplayInventoryResponseInvalidReason = "none"
    $script:virtualDisplayInventoryResponseInvalidCount = 0
  }
  $script:virtualDisplayInventoryFailureLatched = $true
}

function New-VirtualDisplayState(
    [string]$State, [string]$MachineCode, [int]$DeviceCount,
    [string]$IdentitySha256, [bool]$DriverBindingVerified,
    [array]$Devices, [bool]$IncludeRemovalAuthority) {
  $result = [ordered]@{
    state = $State
    machineCode = $MachineCode
    deviceCount = $DeviceCount
    presentDeviceCount = @($Devices | Where-Object {
      [bool]$_.present
    }).Count
    uniqueDeviceIdsSha256 = $IdentitySha256
    driverBindingVerified = $DriverBindingVerified
    physicalDesktopAvailable = $true
  }
  if ($IncludeRemovalAuthority) {
    $result.removalInstanceIds = @($Devices.instanceId)
  }
  return $result
}

function Get-VirtualDisplay([switch]$IncludeRemovalAuthority) {
  $trackChunkInventory = [string]::IsNullOrWhiteSpace(
    [string]$env:LIGASE_VIRTUAL_DISPLAY_READBACK_VALIDATION_ROOT)
  if ($trackChunkInventory -and
      -not $script:virtualDisplayInventoryFailureLatched) {
    $script:virtualDisplayInventoryStage = "allDevices"
    $script:virtualDisplayInventoryFailureStage = "none"
    $script:virtualDisplayInventoryOutputReason = "none"
    $script:virtualDisplayInventoryCoverageStage = "none"
    $script:virtualDisplayInventoryCoverageReason = "none"
    $script:virtualDisplayInventoryRequestedCount = -1
    $script:virtualDisplayInventoryReturnedCount = -1
    $script:virtualDisplayInventoryDeviceCount = -1
    $script:virtualDisplayInventoryCurrentBatchIndex = -1
    $script:virtualDisplayInventoryTotalBatchCount = 0
    $script:virtualDisplayInventoryHardwareBatchesCompleted = 0
    $script:virtualDisplayInventoryDriverBatchesCompleted = 0
    $script:virtualDisplayInventoryElapsedMilliseconds = 0
    $script:virtualDisplayChunkFailureStage = "none"
    $script:virtualDisplayChunkCoverageStage = "none"
    $script:virtualDisplayChunkCoverageReason = "none"
    $script:virtualDisplayChunkRequestedCount = -1
    $script:virtualDisplayChunkReturnedCount = -1
    $script:virtualDisplayChunkDuplicateRequestedCount = -1
    $script:virtualDisplayChunkDuplicateReturnedRowCount = -1
    $script:virtualDisplayChunkDuplicateUniqueOrdinalCount = -1
    $script:virtualDisplayChunkDuplicateUniqueOrdinalIgnoreCaseCount = -1
    $script:virtualDisplayChunkDuplicateGroupCount = 0
    $script:virtualDisplayChunkDuplicateMaxMultiplicity = 0
    $script:virtualDisplayChunkDuplicateCaseOnlyCount = 0
    $script:virtualDisplayChunkDuplicateDataRelation = "none"
    $script:virtualDisplayChunkCleanupState = "notRequired"
    $script:virtualDisplayChunkRootPidZero = $true
    $script:virtualDisplayChunkJobActiveProcesses = 0
  }
  try {
    $candidates = @(Get-VirtualDisplaySnapshot)
    if ($trackChunkInventory) {
      $script:virtualDisplayInventoryDeviceCount = $candidates.Count
      Set-VirtualDisplayInventoryDiagnostic $true
    }
    $devices = @($candidates | Where-Object {
      $hardwareIds = @($_.hardwareIds)
      @($hardwareIds | Where-Object {
        [StringComparer]::OrdinalIgnoreCase.Equals(
          [string]$_, "root\sudomaker\sudovda")
      }).Count -eq 1
    })
    $identityBytes = [Text.UTF8Encoding]::new($false).GetBytes(
      (@($devices.instanceId | Sort-Object -CaseSensitive) -join "`n"))
    $identitySha256 = Get-ByteSha256 $identityBytes
    if ($devices.Count -eq 0) {
      return New-VirtualDisplayState "notInstalled" (
        "virtualDisplayNotInstalled") 0 $identitySha256 $false $devices (
        [bool]$IncludeRemovalAuthority)
    }
    if ($devices.Count -ne 1) {
      return New-VirtualDisplayState "failed" (
        "virtualDisplayDeviceCountInvalid") $devices.Count $identitySha256 (
        $false) $devices ([bool]$IncludeRemovalAuthority)
    }
    if (@($devices | Where-Object { [bool]$_.present }).Count -ne 1) {
      return New-VirtualDisplayState "failed" (
        "virtualDisplayDeviceNotPresent") $devices.Count $identitySha256 (
        $false) $devices ([bool]$IncludeRemovalAuthority)
    }
    $driverBindings = @($devices.driverInf)
    $driverBindingVerified = (
      $driverBindings.Count -eq $devices.Count -and
      @($driverBindings | Where-Object {
        [string]::IsNullOrWhiteSpace([string]$_) -or
        [string]$_ -notmatch '^oem[0-9]+\.inf$'
      }).Count -eq 0)
    if (-not $driverBindingVerified) {
      return New-VirtualDisplayState "failed" (
        "virtualDisplayDriverBindingMissing") $devices.Count $identitySha256 (
        $false) $devices ([bool]$IncludeRemovalAuthority)
    }
    $reboot = @($devices | Where-Object { $_.status -cne "OK" }).Count -gt 0
    return New-VirtualDisplayState (
      $(if ($reboot) { "rebootRequired" } else { "available" })) (
      $(if ($reboot) { "virtualDisplayRebootRequired" } else { "available" })) (
      $devices.Count) $identitySha256 $true $devices (
      [bool]$IncludeRemovalAuthority)
  } catch {
    if ($trackChunkInventory) {
      Set-VirtualDisplayInventoryDiagnostic $false
    }
    return New-VirtualDisplayState "failed" (
      "virtualDisplayReadbackFailed") 0 (
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855") (
      $false) @() ([bool]$IncludeRemovalAuthority)
  }
}

function Set-VirtualDisplayFinalizePreReadAuthority($Display) {
  $reason = [string]$script:virtualDisplayNativeInventoryFailureReason
  $failed = $reason -cne "none"
  $script:virtualDisplayFinalizePreReadStage = if ($failed) {
    "failed"
  } else { "completed" }
  $script:virtualDisplayFinalizePreReadReason = if ($failed) {
    $reason
  } else { "none" }
  $script:virtualDisplayFinalizePreReadCleanupState =
    [string]$script:virtualDisplayNativeInventoryCleanupState
  $script:virtualDisplayFinalizePreReadRootPidZero =
    [bool]$script:virtualDisplayNativeInventoryRootPidZero
  $script:virtualDisplayFinalizePreReadJobActiveProcesses =
    [int]$script:virtualDisplayNativeInventoryJobActiveProcesses
  $script:virtualDisplayFinalizePreReadStdoutClosed =
    [bool]$script:virtualDisplayNativeInventoryStdoutClosed
  $script:virtualDisplayFinalizePreReadStderrClosed =
    [bool]$script:virtualDisplayNativeInventoryStderrClosed
  if ($failed) {
    $script:virtualDisplayFinalizePreReadDeviceCount = -1
    $script:virtualDisplayFinalizePreReadPresentDeviceCount = -1
    $script:virtualDisplayFinalizePreReadIdentitySha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayFinalizePreReadDriverBindingVerified = $false
  } else {
    $script:virtualDisplayFinalizePreReadDeviceCount = [int]$Display.deviceCount
    $script:virtualDisplayFinalizePreReadPresentDeviceCount =
      [int]$Display.presentDeviceCount
    $script:virtualDisplayFinalizePreReadIdentitySha256 =
      [string]$Display.uniqueDeviceIdsSha256
    $script:virtualDisplayFinalizePreReadDriverBindingVerified =
      [bool]$Display.driverBindingVerified
  }
  if ($null -ne $script:virtualDisplayDiagnostic) {
    $script:virtualDisplayDiagnostic.finalizePreReadStage =
      $script:virtualDisplayFinalizePreReadStage
    $script:virtualDisplayDiagnostic.finalizePreReadReason =
      $script:virtualDisplayFinalizePreReadReason
    $script:virtualDisplayDiagnostic.finalizePreReadCleanupState =
      $script:virtualDisplayFinalizePreReadCleanupState
    $script:virtualDisplayDiagnostic.finalizePreReadRootPidZero =
      $script:virtualDisplayFinalizePreReadRootPidZero
    $script:virtualDisplayDiagnostic.finalizePreReadJobActiveProcesses =
      $script:virtualDisplayFinalizePreReadJobActiveProcesses
    $script:virtualDisplayDiagnostic.finalizePreReadStdoutClosed =
      $script:virtualDisplayFinalizePreReadStdoutClosed
    $script:virtualDisplayDiagnostic.finalizePreReadStderrClosed =
      $script:virtualDisplayFinalizePreReadStderrClosed
    $script:virtualDisplayDiagnostic.finalizePreReadDeviceCount =
      $script:virtualDisplayFinalizePreReadDeviceCount
    $script:virtualDisplayDiagnostic.finalizePreReadPresentDeviceCount =
      $script:virtualDisplayFinalizePreReadPresentDeviceCount
    $script:virtualDisplayDiagnostic.finalizePreReadIdentitySha256 =
      $script:virtualDisplayFinalizePreReadIdentitySha256
    $script:virtualDisplayDiagnostic.finalizePreReadDriverBindingVerified =
      $script:virtualDisplayFinalizePreReadDriverBindingVerified
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
  if ($env:LIGASE_INSTALL_VALIDATION_HARNESS -ceq "1") {
    return $env:LIGASE_VIRTUAL_DISPLAY_DEPENDENT_DEVICE -ceq "1"
  }
  try {
    return @(Get-VirtualDisplaySnapshot).Count -gt 0
  } catch {
    return $true
  }
}

function Test-ExactBytes([byte[]]$Expected, [byte[]]$Actual) {
  if ($null -eq $Expected -or $null -eq $Actual -or
      $Expected.Length -ne $Actual.Length) {
    return $false
  }
  $different = 0
  for ($index = 0; $index -lt $Expected.Length; $index++) {
    $different = $different -bor ($Expected[$index] -bxor $Actual[$index])
  }
  return $different -eq 0
}

function Write-VirtualDisplayOwnershipMarkerAtomic(
    [string]$Path,
    [byte[]]$Bytes,
    [bool]$EnableValidationFaults = $true) {
  $directory = Split-Path -Parent $Path
  $directoryItem = Get-Item -LiteralPath $directory -Force
  if (($directoryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "virtualDisplayMarkerCommitFailed"
  }
  if (Test-Path -LiteralPath $Path) {
    $markerItem = Get-Item -LiteralPath $Path -Force
    if ($markerItem.PSIsContainer -or
        ($markerItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      throw "virtualDisplayMarkerCommitFailed"
    }
  }

  $temp = Join-Path $directory (
    ".ligase-driver-ownership-{0}.tmp" -f [Guid]::NewGuid().ToString("N"))
  $fault = if ($EnableValidationFaults -and
      $env:LIGASE_INSTALL_VALIDATION_HARNESS -ceq "1") {
    [string]$env:LIGASE_VIRTUAL_DISPLAY_MARKER_FAILURE_STAGE
  } else {
    ""
  }
  try {
    if ($fault -ceq "createTemp") { throw "virtualDisplayMarkerCommitFailed" }
    $stream = [IO.FileStream]::new(
      $temp, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
      [IO.FileShare]::None, 4096,
      [IO.FileOptions]::WriteThrough)
    try {
      if ($fault -ceq "writeTemp") { throw "virtualDisplayMarkerCommitFailed" }
      $stream.Write($Bytes, 0, $Bytes.Length)
      $stream.Flush($true)
    } finally {
      $stream.Dispose()
    }
    $tempItem = Get-Item -LiteralPath $temp -Force
    if (($tempItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      throw "virtualDisplayMarkerCommitFailed"
    }
    $null = Get-Acl -LiteralPath $temp
    if ($fault -ceq "tempReadback" -or
        -not (Test-ExactBytes $Bytes ([IO.File]::ReadAllBytes($temp)))) {
      throw "virtualDisplayMarkerCommitFailed"
    }
    if ($fault -ceq "atomicReplace") { throw "virtualDisplayMarkerCommitFailed" }
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
      [IO.File]::Replace($temp, $Path, $null, $true)
    } else {
      [IO.File]::Move($temp, $Path)
    }
    if ($fault -ceq "finalReadback") {
      throw "virtualDisplayMarkerCommitFailed"
    }
    $finalItem = Get-Item -LiteralPath $Path -Force
    if (($finalItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      throw "virtualDisplayMarkerCommitFailed"
    }
    $null = Get-Acl -LiteralPath $Path
    if (-not (Test-ExactBytes $Bytes ([IO.File]::ReadAllBytes($Path)))) {
      throw "virtualDisplayMarkerCommitFailed"
    }
  } finally {
    if (Test-Path -LiteralPath $temp) {
      Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
    }
  }
}

function Invoke-VirtualDisplayMarkerValidation([string]$Root) {
  $fullRoot = [IO.Path]::GetFullPath($Root)
  if ($env:LIGASE_INSTALL_VALIDATION_HARNESS -cne "1" -or
      [string]$env:LIGASE_VIRTUAL_DISPLAY_VALIDATION_ROOT -cne $fullRoot -or
      [IO.Path]::GetPathRoot($fullRoot) -cne "D:\") {
    throw "virtualDisplayValidationUnavailable"
  }
  $marker = Join-Path $fullRoot ".ligase-driver-ownership.json"
  $sentinel = Join-Path $fullRoot "owned-cert.sentinel"
  $original = if (Test-Path -LiteralPath $marker -PathType Leaf) {
    [IO.File]::ReadAllBytes($marker)
  } else {
    $null
  }
  $newBytes = [Text.UTF8Encoding]::new($false).GetBytes(
    '{"schemaVersion":1,"certificateThumbprint":"VALIDATION","certificateStores":["D_ONLY_SENTINEL"]}')
  $dependent = $env:LIGASE_VIRTUAL_DISPLAY_DEPENDENT_DEVICE -ceq "1"
  $rollbackFailed = $false
  $resultCode = "virtualDisplayMarkerCommitFailed"
  try {
    if ($env:LIGASE_VIRTUAL_DISPLAY_FORCE_MARKER_CHANGED -ceq "1") {
      [IO.File]::WriteAllBytes($marker, $newBytes)
      throw "virtualDisplayMarkerCommitFailed"
    }
    Write-VirtualDisplayOwnershipMarkerAtomic $marker $newBytes
    throw "virtualDisplayMarkerCommitFailed"
  } catch {
    try {
      if ($null -eq $original) {
        if (Test-Path -LiteralPath $marker) {
          Remove-Item -LiteralPath $marker -Force
        }
      } else {
        $liveExact = (Test-Path -LiteralPath $marker -PathType Leaf) -and
          (Test-ExactBytes $original ([IO.File]::ReadAllBytes($marker)))
        if (-not $liveExact) {
          if ($env:LIGASE_VIRTUAL_DISPLAY_COMPENSATION_FAILURE -ceq
              "restoreMarker") {
            throw "validationRestoreFailure"
          }
          Write-VirtualDisplayOwnershipMarkerAtomic $marker $original $false
        }
      }
    } catch {
      $rollbackFailed = $true
    }
    if (Test-Path -LiteralPath $sentinel) {
      if ($dependent) {
        $rollbackFailed = $true
      } else {
        try {
          if ($env:LIGASE_VIRTUAL_DISPLAY_COMPENSATION_FAILURE -ceq
              "removeCert") {
            throw "validationCertificateRemovalFailure"
          }
          Remove-Item -LiteralPath $sentinel -Force
        } catch {
          $rollbackFailed = $true
        }
      }
    }
    $current = if (Test-Path -LiteralPath $marker -PathType Leaf) {
      [IO.File]::ReadAllBytes($marker)
    } else {
      $null
    }
    if (($null -eq $original) -ne ($null -eq $current) -or
        ($null -ne $original -and -not (Test-ExactBytes $original $current)) -or
        @(Get-ChildItem -LiteralPath $fullRoot -Force -Filter (
            ".ligase-driver-ownership-*.tmp")).Count -ne 0 -or
        (-not $dependent -and (Test-Path -LiteralPath $sentinel))) {
      $rollbackFailed = $true
    }
    if ($rollbackFailed) { $resultCode = "virtualDisplayRollbackFailed" }
  }
  return [ordered]@{
    code = $resultCode
    success = $false
    markerRestored = -not $rollbackFailed
    markerWasPresent = $null -ne $original
    tempResidueCount = @(
      Get-ChildItem -LiteralPath $fullRoot -Force -Filter (
        ".ligase-driver-ownership-*.tmp")).Count
    certSentinelPresent = Test-Path -LiteralPath $sentinel
    dependentDevice = $dependent
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
  if ($VirtualDisplaySelected -and $VirtualDisplayOutcome -cne "installed") {
    Fail-FinalInstallReadback "virtualDisplay"
  }
  if ($VirtualDisplaySelected) {
    $display = $VirtualDisplayReadback
    if ($null -eq $display) {
      Fail-FinalInstallReadback "virtualDisplay"
    }
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
  if ($Action -eq "ValidateVirtualDisplayMarkerTransaction") {
    $validationResult = Invoke-VirtualDisplayMarkerValidation $ValidationRoot
    [Console]::Out.WriteLine(($validationResult | ConvertTo-Json -Compress))
    exit 0
  }
  if ($Action -eq "ValidateVirtualDisplayInstallerProcess") {
    $validationResult = Invoke-VirtualDisplayInstallerValidation $ValidationRoot
    [Console]::Out.WriteLine(($validationResult | ConvertTo-Json -Compress))
    exit 0
  }
  if ($Action -eq "ValidateVirtualDisplayReadback") {
    $fullValidationRoot = [IO.Path]::GetFullPath($ValidationRoot)
    if ($env:LIGASE_INSTALL_VALIDATION_HARNESS -cne "1" -or
        [string]$env:LIGASE_VIRTUAL_DISPLAY_READBACK_VALIDATION_ROOT -cne
          $fullValidationRoot -or
        [IO.Path]::GetPathRoot($fullValidationRoot) -cne "D:\") {
      throw "virtualDisplayValidationUnavailable"
    }
    [Console]::Out.WriteLine((
      (Get-VirtualDisplay) | ConvertTo-Json -Compress))
    exit 0
  }
  if ($Action -eq "ValidateVirtualDisplayNativeInventoryHelper") {
    try {
      $projection = @(Invoke-VirtualDisplayInventoryHelper)
      $result = [ordered]@{
        schemaVersion = 1
        state = "available"
        matchingDeviceCount = $projection.Count
        cleanupState = $script:virtualDisplayNativeInventoryCleanupState
        rootPidZero = $script:virtualDisplayNativeInventoryRootPidZero
        jobActiveProcesses =
          $script:virtualDisplayNativeInventoryJobActiveProcesses
        validationStage =
          $script:virtualDisplayNativeInventoryValidationStage
        validationExitCode =
          $script:virtualDisplayNativeInventoryValidationExitCode
        validationStderrLength =
          $script:virtualDisplayNativeInventoryValidationStderrLength
        stdoutClosed = $script:virtualDisplayNativeInventoryStdoutClosed
        stderrClosed = $script:virtualDisplayNativeInventoryStderrClosed
      }
      [Console]::Out.WriteLine(($result | ConvertTo-Json -Compress))
      exit 0
    } catch {
      $result = [ordered]@{
        schemaVersion = 1
        state = "failed"
        matchingDeviceCount = -1
        cleanupState = $script:virtualDisplayNativeInventoryCleanupState
        rootPidZero = $script:virtualDisplayNativeInventoryRootPidZero
        jobActiveProcesses =
          $script:virtualDisplayNativeInventoryJobActiveProcesses
        validationStage =
          $script:virtualDisplayNativeInventoryValidationStage
        validationExitCode =
          $script:virtualDisplayNativeInventoryValidationExitCode
        validationStderrLength =
          $script:virtualDisplayNativeInventoryValidationStderrLength
        stdoutClosed = $script:virtualDisplayNativeInventoryStdoutClosed
        stderrClosed = $script:virtualDisplayNativeInventoryStderrClosed
      }
      [Console]::Out.WriteLine(($result | ConvertTo-Json -Compress))
      exit 18
    }
  }
  if ($Action -eq "ValidateVirtualDisplayRemovalReconciliation") {
    $validationResult =
      Invoke-VirtualDisplayRemovalValidation $ValidationRoot
    [Console]::Out.WriteLine(($validationResult | ConvertTo-Json -Compress))
    exit 0
  }
  if ($Action -eq "ValidateVirtualDisplayTerminalReadback") {
    $fullValidationRoot = [IO.Path]::GetFullPath($ValidationRoot)
    if ($env:LIGASE_INSTALL_VALIDATION_HARNESS -cne "1" -or
        [string]$env:LIGASE_VIRTUAL_DISPLAY_TERMINAL_VALIDATION_ROOT -cne
          $fullValidationRoot -or
        $fullValidationRoot -cne $installRoot -or
        [IO.Path]::GetPathRoot($fullValidationRoot) -cne "D:\") {
      throw "virtualDisplayValidationUnavailable"
    }
    $caseRaw = [IO.File]::ReadAllText(
      (Join-Path $fullValidationRoot "terminal-case.json"),
      [Text.UTF8Encoding]::new($false, $true))
    if (-not [LigaseStrictJson]::HasUniqueProperties($caseRaw)) {
      throw "virtualDisplayValidationUnavailable"
    }
    $caseDocument = $caseRaw | ConvertFrom-Json
    Assert-ClosedProperties $caseDocument @(
      "schemaVersion", "mode") "virtualDisplayTerminalValidation"
    if ($caseDocument.schemaVersion -ne 1 -or
        $caseDocument.mode -isnot [string] -or
        @("zero", "oneBound", "oneUnbound", "unknown", "unavailable") `
          -cnotcontains [string]$caseDocument.mode) {
      throw "virtualDisplayValidationUnavailable"
    }
    $terminalProvider = {
      if ([string]$caseDocument.mode -ceq "unavailable") {
        throw "validationTerminalUnavailable"
      }
      if ([string]$caseDocument.mode -ceq "unknown") {
        return [ordered]@{
          state = "failed"
          machineCode = "virtualDisplayReadbackFailed"
          deviceCount = -1
          uniqueDeviceIdsSha256 = "invalid"
          driverBindingVerified = $false
        }
      }
      $count = if ([string]$caseDocument.mode -ceq "zero") { 0 } else { 1 }
      return [ordered]@{
        state = if ($count -eq 0) { "notInstalled" } else { "available" }
        machineCode = if ($count -eq 0) {
          "virtualDisplayNotInstalled"
        } elseif ([string]$caseDocument.mode -ceq "oneBound") {
          "available"
        } else { "virtualDisplayDriverBindingMissing" }
        deviceCount = $count
        presentDeviceCount = $count
        uniqueDeviceIdsSha256 = if ($count -eq 0) {
          "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        } else {
          "1111111111111111111111111111111111111111111111111111111111111111"
        }
        driverBindingVerified =
          [string]$caseDocument.mode -ceq "oneBound"
      }
    }.GetNewClosure()
    $null = Set-VirtualDisplayTerminalResidualAuthority $terminalProvider
    [Console]::Out.WriteLine(([ordered]@{
      code = "virtualDisplayTerminalReadbackValidated"
      success = $true
      mode = [string]$caseDocument.mode
      observedDeviceCount = [int]$script:virtualDisplayObservedDeviceCount
      presentDeviceCount = [int]$script:virtualDisplayPresentDeviceCount
      uniqueDeviceIdsSha256 =
        [string]$script:virtualDisplayUniqueDeviceIdsSha256
      driverBindingVerified =
        [bool]$script:virtualDisplayDriverBindingVerified
      residualDeviceState = [string]$script:virtualDisplayResidualState
      terminalReadbackState =
        [string]$script:virtualDisplayTerminalReadbackState
      terminalReadbackReason =
        [string]$script:virtualDisplayTerminalReadbackReason
      pnpAccess = $false
      programDataAccess = $false
    } | ConvertTo-Json -Compress))
    exit 0
  }
  if ($Action -eq "ValidateVirtualDisplayTrustedPnPUtil") {
    $fullValidationRoot = [IO.Path]::GetFullPath($ValidationRoot)
    if ($env:LIGASE_INSTALL_VALIDATION_HARNESS -cne "1" -or
        [string]$env:LIGASE_VIRTUAL_DISPLAY_TRUSTED_TOOL_VALIDATION_ROOT -cne
          $fullValidationRoot -or
        $fullValidationRoot -cne $installRoot -or
        [IO.Path]::GetPathRoot($fullValidationRoot) -cne "D:\") {
      throw "virtualDisplayValidationUnavailable"
    }
    $priorSystemRoot = $env:SystemRoot
    $priorPath = $env:PATH
    try {
      $env:SystemRoot = $fullValidationRoot
      $env:PATH = $fullValidationRoot
      $trustedPath = [LigaseFileIdentity]::GetTrustedPnPUtilPath()
    } finally {
      $env:SystemRoot = $priorSystemRoot
      $env:PATH = $priorPath
    }
    $trustedItem = Get-Item -LiteralPath $trustedPath -Force
    $systemDirectory = [IO.Path]::GetFullPath(
      [Environment]::SystemDirectory)
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals(
          [IO.Path]::GetFullPath([IO.Path]::GetDirectoryName($trustedPath)),
          $systemDirectory) -or
        $trustedItem.Name -cne "pnputil.exe" -or
        ($trustedItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne
          0) {
      throw "virtualDisplayTrustedToolUnavailable"
    }
    [Console]::Out.WriteLine(([ordered]@{
      code = "virtualDisplayTrustedToolValidated"
      success = $true
      leaf = [string]$trustedItem.Name
      pathSha256 = Get-ByteSha256 (
        [Text.Encoding]::Unicode.GetBytes(
          ([IO.Path]::GetFullPath($trustedPath).ToUpperInvariant())))
      systemRootIgnored = $true
      pathIgnored = $true
      processStartCount = 0
      environmentRestored = (
        $env:SystemRoot -ceq $priorSystemRoot -and
        $env:PATH -ceq $priorPath)
    } | ConvertTo-Json -Compress))
    exit 0
  }
  if ($Action -eq "ValidateVirtualDisplayDiagnosticProjection") {
    $fullValidationRoot = [IO.Path]::GetFullPath($ValidationRoot)
    if ($env:LIGASE_INSTALL_VALIDATION_HARNESS -cne "1" -or
        [string]$env:LIGASE_VIRTUAL_DISPLAY_DIAGNOSTIC_VALIDATION_ROOT -cne
          $fullValidationRoot -or
        $fullValidationRoot -cne $installRoot -or
        [IO.Path]::GetPathRoot($fullValidationRoot) -cne "D:\") {
      throw "virtualDisplayValidationUnavailable"
    }
    $script:virtualDisplayInstallStage = "completed"
    $script:virtualDisplayReadbackCode = "virtualDisplayDeviceCountInvalid"
    $script:virtualDisplayChildExit = 0
    $script:virtualDisplayRemoveExit = 0
    $script:virtualDisplayRemoveCount = 2
    $script:virtualDisplayProcessCleanup = "completed"
    $script:virtualDisplayMarkerStage = "notAttempted"
    $script:virtualDisplayObservedDeviceCount = 2
    $script:virtualDisplayPresentDeviceCount = 2
    $script:virtualDisplayUniqueDeviceIdsSha256 =
      "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
    $script:virtualDisplayDriverBindingVerified = $false
    $script:virtualDisplayFallbackAttempted = $true
    $script:virtualDisplayFallbackExitCode = 5
    $script:virtualDisplayFallbackStage = "tupleValidation"
    $script:virtualDisplayFallbackReason = "nativeFailure"
    $script:virtualDisplayDeviceRecovery = "failed"
    $script:virtualDisplayResidualState = "multiple"
    $script:virtualDisplayCompensation = "completed"
    $script:virtualDisplayCompensationFailureReason = "none"
    $script:virtualDisplayTerminalReadbackState = "completed"
    $script:virtualDisplayTerminalReadbackReason = "none"
    $script:virtualDisplayCreateInvocationCount = 0
    $script:virtualDisplayCreateInvocationIdSha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayPreCreateIdentitySha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayPostCreateIdentitySha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayPostCreateIdentityState = "notAttempted"
    $script:virtualDisplayPostCreateIdentityReason = "none"
    $original = New-VirtualDisplayDiagnostic "virtualDisplayReadbackFailed" $false
    $crossSpliceCases = @(
      [ordered]@{
        name = "fallbackContradiction"
        values = [ordered]@{
          fallbackAttempted = $false
          fallbackExitCode = 5
          fallbackStage = "completed"
          fallbackReason = "timeout"
        }
      },
      [ordered]@{
        name = "compensationContradiction"
        values = [ordered]@{
          compensationState = "completed"
          compensationFailureReason = "markerRestore"
        }
      },
      [ordered]@{
        name = "terminalContradiction"
        values = [ordered]@{
          terminalReadbackState = "notAttempted"
          terminalReadbackReason = "none"
          observedDeviceCount = 1
          residualDeviceState = "exactOneBound"
          driverBindingVerified = $true
        }
      },
      [ordered]@{
        name = "terminalZeroHashContradiction"
        values = [ordered]@{
          terminalReadbackState = "completed"
          terminalReadbackReason = "none"
          observedDeviceCount = 0
          residualDeviceState = "zero"
          driverBindingVerified = $false
        }
      },
      [ordered]@{
        name = "terminalPositiveHashContradiction"
        values = [ordered]@{
          terminalReadbackState = "completed"
          terminalReadbackReason = "none"
          observedDeviceCount = 1
          uniqueDeviceIdsSha256 =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
          residualDeviceState = "exactOneBound"
          driverBindingVerified = $true
        }
      },
      [ordered]@{
        name = "createInvocationAbsentContradiction"
        values = [ordered]@{
          createInvocationCount = 0
          createInvocationIdSha256 =
            "2222222222222222222222222222222222222222222222222222222222222222"
        }
      },
      [ordered]@{
        name = "createInvocationPresentContradiction"
        values = [ordered]@{
          createInvocationCount = 1
          createInvocationIdSha256 =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        }
      },
      [ordered]@{
        name = "createInvocationPostIdentityContradiction"
        values = [ordered]@{
          createInvocationCount = 1
          createInvocationIdSha256 =
            "2222222222222222222222222222222222222222222222222222222222222222"
          postCreateIdentityState = "completed"
          postCreateIdentityReason = "none"
          postCreateIdentitySha256 =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        }
      },
      [ordered]@{
        name = "createInvocationPostFailureContradiction"
        values = [ordered]@{
          createInvocationCount = 1
          createInvocationIdSha256 =
            "2222222222222222222222222222222222222222222222222222222222222222"
          postCreateIdentityState = "failed"
          postCreateIdentityReason = "unavailable"
          postCreateIdentitySha256 =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        }
      },
      [ordered]@{
        name = "presentCountContradiction"
        values = [ordered]@{
          observedDeviceCount = 0
          presentDeviceCount = 1
          uniqueDeviceIdsSha256 =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
          residualDeviceState = "zero"
          terminalReadbackState = "completed"
          terminalReadbackReason = "none"
        }
      },
      [ordered]@{
        name = "boundNonPresentContradiction"
        values = [ordered]@{
          observedDeviceCount = 1
          presentDeviceCount = 0
          uniqueDeviceIdsSha256 =
            "2222222222222222222222222222222222222222222222222222222222222222"
          driverBindingVerified = $true
          residualDeviceState = "exactOneBound"
          terminalReadbackState = "completed"
          terminalReadbackReason = "none"
        }
      },
      [ordered]@{
        name = "inventoryNotAttemptedContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
        }
      },
      [ordered]@{
        name = "inventoryDeadlineContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "deadline"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 11
          inventoryTotalBatchCount = 12
          inventoryHardwareBatchesCompleted = 11
          inventoryElapsedMilliseconds = 8999
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryCoverageContradiction"
        values = [ordered]@{
          inventoryStage = "driverInf"
          inventoryFailureStage = "coverage"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 11
          inventoryTotalBatchCount = 12
          inventoryHardwareBatchesCompleted = 11
          inventoryDriverBatchesCompleted = 12
          inventoryElapsedMilliseconds = 8000
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryCoverageCountContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "coverage"
          inventoryCoverageStage = "rowCount"
          inventoryCoverageReason = "extra"
          inventoryRequestedCount = 32
          inventoryReturnedCount = 31
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryCoverageIdentityContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "coverage"
          inventoryCoverageStage = "rowIdentity"
          inventoryCoverageReason = "duplicate"
          inventoryRequestedCount = 32
          inventoryReturnedCount = 31
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryCoverageAllDevicesContradiction"
        values = [ordered]@{
          inventoryStage = "allDevices"
          inventoryFailureStage = "coverage"
          inventoryCoverageStage = "rowCount"
          inventoryCoverageReason = "missing"
          inventoryRequestedCount = 32
          inventoryReturnedCount = 31
        }
      },
      [ordered]@{
        name = "inventoryCoverageFirstBatchCountContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "coverage"
          inventoryCoverageStage = "rowCount"
          inventoryCoverageReason = "missing"
          inventoryRequestedCount = 31
          inventoryReturnedCount = 30
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryCoverageLastBatchCountContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "coverage"
          inventoryCoverageStage = "rowCount"
          inventoryCoverageReason = "missing"
          inventoryRequestedCount = 16
          inventoryReturnedCount = 15
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 11
          inventoryTotalBatchCount = 12
          inventoryHardwareBatchesCompleted = 11
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryCleanupContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "cleanup"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryElapsedMilliseconds = 200
          inventoryCleanupState = "completed"
          inventoryRootPidZero = $true
          inventoryJobActiveProcesses = 0
        }
      },
      [ordered]@{
        name = "inventoryCompletedCleanupContradiction"
        values = [ordered]@{
          inventoryStage = "completed"
          inventoryFailureStage = "none"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 11
          inventoryTotalBatchCount = 12
          inventoryHardwareBatchesCompleted = 12
          inventoryDriverBatchesCompleted = 12
          inventoryElapsedMilliseconds = 8000
          inventoryCleanupState = "failed"
          inventoryRootPidZero = $false
          inventoryJobActiveProcesses = -1
        }
      },
      [ordered]@{
        name = "inventoryOutputReasonMissingContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "output"
          inventoryOutputReason = "none"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryOutputReasonCrossSpliceContradiction"
        values = [ordered]@{
          inventoryOutputReason = "nativeExit"
        }
      },
      [ordered]@{
        name = "inventoryOutputCleanupContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "output"
          inventoryOutputReason = "nativeExit"
          inventoryNativeExitCode = 94
          inventoryChildFailureStage = "validationQuota"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "failed"
          inventoryRootPidZero = $false
          inventoryJobActiveProcesses = -1
        }
      },
      [ordered]@{
        name = "inventoryOutputDriverBeforeHardwareContradiction"
        values = [ordered]@{
          inventoryStage = "driverInf"
          inventoryFailureStage = "output"
          inventoryOutputReason = "nativeExit"
          inventoryNativeExitCode = 94
          inventoryChildFailureStage = "validationQuota"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryHardwareBatchesCompleted = 0
          inventoryDriverBatchesCompleted = 0
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryOutputHardwareProgressContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "output"
          inventoryOutputReason = "stderr"
          inventoryNativeExitCode = 0
          inventoryChildFailureStage = "stderr"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 5
          inventoryTotalBatchCount = 12
          inventoryHardwareBatchesCompleted = 0
          inventoryDriverBatchesCompleted = 0
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryNativeExitStageContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "output"
          inventoryOutputReason = "nativeExit"
          inventoryNativeExitCode = 94
          inventoryChildFailureStage = "propertyQuery"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryOutputNativeCodeContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "output"
          inventoryOutputReason = "stderr"
          inventoryNativeExitCode = 94
          inventoryChildFailureStage = "validationQuota"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryResponseIdentityStageContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "output"
          inventoryOutputReason = "nativeExit"
          inventoryNativeExitCode = 103
          inventoryChildFailureStage = "responseIdentityInvalid"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryRequestIdentityStageContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "output"
          inventoryOutputReason = "nativeExit"
          inventoryNativeExitCode = 95
          inventoryChildFailureStage = "requestIdentityInvalid"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryRawNegativeExitContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "output"
          inventoryOutputReason = "nativeExit"
          inventoryNativeExitCode = -532462766
          inventoryChildFailureStage = "hostFailure"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryRawHighExitContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "output"
          inventoryOutputReason = "nativeExit"
          inventoryNativeExitCode = 70000
          inventoryChildFailureStage = "hostFailure"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "completed"
        }
      },
      [ordered]@{
        name = "inventoryDuplicateStatsWrongExitContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "output"
          inventoryOutputReason = "nativeExit"
          inventoryNativeExitCode = 94
          inventoryChildFailureStage = "validationQuota"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "completed"
          inventoryResponseRequestedCount = 32
          inventoryResponseReturnedRowCount = 33
          inventoryResponseUniqueOrdinalCount = 33
          inventoryResponseUniqueOrdinalIgnoreCaseCount = 32
          inventoryResponseDuplicateGroupCount = 1
          inventoryResponseDuplicateMaxMultiplicity = 2
          inventoryResponseCaseOnlyDuplicateCount = 1
          inventoryResponseDataRelation = "conflicting"
        }
      },
      [ordered]@{
        name = "inventoryConflictStatsRelationContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "output"
          inventoryOutputReason = "nativeExit"
          inventoryNativeExitCode = 103
          inventoryChildFailureStage = "responseIdentityConflict"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "completed"
          inventoryResponseRequestedCount = 32
          inventoryResponseReturnedRowCount = 33
          inventoryResponseUniqueOrdinalCount = 33
          inventoryResponseUniqueOrdinalIgnoreCaseCount = 32
          inventoryResponseDuplicateGroupCount = 1
          inventoryResponseDuplicateMaxMultiplicity = 2
          inventoryResponseCaseOnlyDuplicateCount = 1
          inventoryResponseDataRelation = "invalid"
        }
      },
      [ordered]@{
        name = "inventoryInvalidStatsRelationContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "output"
          inventoryOutputReason = "nativeExit"
          inventoryNativeExitCode = 101
          inventoryChildFailureStage = "responseIdentityInvalid"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "completed"
          inventoryResponseRequestedCount = 32
          inventoryResponseReturnedRowCount = 33
          inventoryResponseUniqueOrdinalCount = 33
          inventoryResponseUniqueOrdinalIgnoreCaseCount = 32
          inventoryResponseDuplicateGroupCount = 1
          inventoryResponseDuplicateMaxMultiplicity = 2
          inventoryResponseCaseOnlyDuplicateCount = 1
          inventoryResponseDataRelation = "conflicting"
        }
      },
      [ordered]@{
        name = "inventoryInvalidReasonMissingContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "output"
          inventoryOutputReason = "nativeExit"
          inventoryNativeExitCode = 101
          inventoryChildFailureStage = "responseIdentityInvalid"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "completed"
          inventoryResponseRequestedCount = 32
          inventoryResponseReturnedRowCount = 32
          inventoryResponseUniqueOrdinalCount = 31
          inventoryResponseUniqueOrdinalIgnoreCaseCount = 31
          inventoryResponseDuplicateGroupCount = 0
          inventoryResponseDuplicateMaxMultiplicity = 1
          inventoryResponseCaseOnlyDuplicateCount = 0
          inventoryResponseDataRelation = "invalid"
          inventoryResponseInvalidReason = "none"
          inventoryResponseInvalidCount = 0
        }
      },
      [ordered]@{
        name = "inventoryConflictCarriesInvalidReasonContradiction"
        values = [ordered]@{
          inventoryStage = "hardwareIds"
          inventoryFailureStage = "output"
          inventoryOutputReason = "nativeExit"
          inventoryNativeExitCode = 103
          inventoryChildFailureStage = "responseIdentityConflict"
          inventoryDeviceCount = 369
          inventoryCurrentBatchIndex = 0
          inventoryTotalBatchCount = 12
          inventoryElapsedMilliseconds = 708
          inventoryCleanupState = "completed"
          inventoryResponseRequestedCount = 32
          inventoryResponseReturnedRowCount = 33
          inventoryResponseUniqueOrdinalCount = 32
          inventoryResponseUniqueOrdinalIgnoreCaseCount = 32
          inventoryResponseDuplicateGroupCount = 1
          inventoryResponseDuplicateMaxMultiplicity = 2
          inventoryResponseCaseOnlyDuplicateCount = 0
          inventoryResponseDataRelation = "conflicting"
          inventoryResponseInvalidReason = "empty"
          inventoryResponseInvalidCount = 1
        }
      },
      [ordered]@{
        name = "finalizePreReadCompletedPipeContradiction"
        values = [ordered]@{
          finalizePreReadStage = "completed"
          finalizePreReadReason = "none"
          finalizePreReadCleanupState = "completed"
          finalizePreReadRootPidZero = $true
          finalizePreReadJobActiveProcesses = 0
          finalizePreReadStdoutClosed = $false
          finalizePreReadStderrClosed = $true
          finalizePreReadDeviceCount = 0
          finalizePreReadPresentDeviceCount = 0
        }
      },
      [ordered]@{
        name = "finalizePreReadFailureCleanupContradiction"
        values = [ordered]@{
          finalizePreReadStage = "failed"
          finalizePreReadReason = "timeout"
          finalizePreReadCleanupState = "failed"
          finalizePreReadRootPidZero = $false
          finalizePreReadJobActiveProcesses = -1
          finalizePreReadStdoutClosed = $false
          finalizePreReadStderrClosed = $false
        }
      })
    $crossSpliceRejected = 0
    foreach ($crossCase in $crossSpliceCases) {
      $crossDocument = (
        ($original | ConvertTo-Json -Compress) | ConvertFrom-Json)
      foreach ($property in $crossCase.values.Keys) {
        $crossDocument.$property = $crossCase.values[$property]
      }
      $crossToken = ConvertTo-VirtualDisplayDiagnosticToken $crossDocument
      $tokenRejected = $false
      try {
        $null = ConvertFrom-VirtualDisplayDiagnosticToken $crossToken
      } catch {
        $tokenRejected =
          [string]$_.Exception.Message -ceq "virtualDisplayDiagnosticInvalid"
      }
      $crossPath = Get-VirtualDisplayDiagnosticPath
      [IO.File]::WriteAllText(
        $crossPath, ($crossDocument | ConvertTo-Json -Compress),
        [Text.UTF8Encoding]::new($false))
      $fileRejected = $false
      try {
        $null = Read-VirtualDisplayDiagnostic
      } catch {
        $fileRejected =
          [string]$_.Exception.Message -ceq "virtualDisplayDiagnosticInvalid"
      }
      if (-not $tokenRejected -or -not $fileRejected) {
        throw "virtualDisplayDiagnosticProjectionInvalid"
      }
      $crossSpliceRejected++
    }
    $token = ConvertTo-VirtualDisplayDiagnosticToken $original
    $primaryWriteFailed = $false
    try { Write-VirtualDisplayDiagnostic $original } catch {
      $primaryWriteFailed = $true
    }
    $projected = ConvertFrom-VirtualDisplayDiagnosticToken $token
    $script:virtualDisplayDiagnostic = $projected
    $script:finalFailedField = "virtualDisplay"
    $script:finalComponents.virtualDisplay = "failed"
    $EvidencePhase = "failed"
    $EvidenceSuccess = "false"
    $EvidenceResultCode = "installationFinalReadbackFailed"
    $EvidenceFailedField = "virtualDisplay"
    $EvidenceHelperExit = 0
    $EvidenceRollback = "completed"
    $null = Write-InstallerEvidence
    $lastOutcomePath = Get-InstallerEvidencePath
    $lastOutcomeBytes = [IO.File]::ReadAllBytes($lastOutcomePath)
    $lastOutcome = [Text.UTF8Encoding]::new(
      $false, $true).GetString($lastOutcomeBytes) | ConvertFrom-Json
    if ([string]$lastOutcome.failedField -cne "virtualDisplay" -or
        [string]$lastOutcome.virtualDisplay.resultCode -cne
          "virtualDisplayReadbackFailed" -or
        [string]$lastOutcome.virtualDisplay.readbackCode -cne
          "virtualDisplayDeviceCountInvalid" -or
        [int]$lastOutcome.virtualDisplay.observedDeviceCount -ne 2 -or
        [bool]$lastOutcome.virtualDisplay.driverBindingVerified -or
        [string]$lastOutcome.virtualDisplay.deviceRecovery -cne "failed" -or
        [string]$lastOutcome.virtualDisplay.residualDeviceState -cne
          "multiple" -or
        [string]$lastOutcome.virtualDisplay.compensationState -cne
          "completed" -or
        [string]$lastOutcome.rollback.state -cne "completed") {
      throw "virtualDisplayDiagnosticProjectionInvalid"
    }
    $script:virtualDisplayInstallStage = "completed"
    $script:virtualDisplayReadbackCode = "virtualDisplayDeviceCountInvalid"
    $script:virtualDisplayChildExit = 0
    $script:virtualDisplayRemoveExit = 0
    $script:virtualDisplayRemoveCount = 0
    $script:virtualDisplayFallbackAttempted = $false
    $script:virtualDisplayFallbackExitCode = -1
    $script:virtualDisplayFallbackStage = "notAttempted"
    $script:virtualDisplayFallbackReason = "none"
    $script:virtualDisplayObservedDeviceCount = 1
    $script:virtualDisplayPresentDeviceCount = 1
    $script:virtualDisplayUniqueDeviceIdsSha256 =
      "1111111111111111111111111111111111111111111111111111111111111111"
    $script:virtualDisplayDriverBindingVerified = $false
    $script:virtualDisplayDeviceRecovery = "completed"
    $script:virtualDisplayResidualState = "exactOneUnbound"
    $script:virtualDisplayCompensation = "failed"
    $script:virtualDisplayCompensationFailureReason = "dependentDevice"
    $script:virtualDisplayTerminalReadbackState = "completed"
    $script:virtualDisplayTerminalReadbackReason = "none"
    $script:virtualDisplayCreateInvocationCount = 1
    $script:virtualDisplayCreateInvocationIdSha256 =
      "2222222222222222222222222222222222222222222222222222222222222222"
    $script:virtualDisplayPreCreateIdentitySha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayPostCreateIdentitySha256 =
      "1111111111111111111111111111111111111111111111111111111111111111"
    $script:virtualDisplayPostCreateIdentityState = "completed"
    $script:virtualDisplayPostCreateIdentityReason = "none"
    $postCreateOriginal = New-VirtualDisplayDiagnostic (
      "virtualDisplayRollbackFailed") $false
    $postCreateToken =
      ConvertTo-VirtualDisplayDiagnosticToken $postCreateOriginal
    $postCreatePrimaryWriteFailed = $false
    try { Write-VirtualDisplayDiagnostic $postCreateOriginal } catch {
      $postCreatePrimaryWriteFailed = $true
    }
    $postCreateProjected =
      ConvertFrom-VirtualDisplayDiagnosticToken $postCreateToken
    $script:virtualDisplayDiagnostic = $postCreateProjected
    $null = Write-InstallerEvidence
    $postCreateLastOutcomeBytes = [IO.File]::ReadAllBytes($lastOutcomePath)
    $postCreateLastOutcome = [Text.UTF8Encoding]::new(
      $false, $true).GetString(
        $postCreateLastOutcomeBytes) | ConvertFrom-Json
    if ([string]$postCreateLastOutcome.failedField -cne "virtualDisplay" -or
        [string]$postCreateLastOutcome.virtualDisplay.resultCode -cne
          "virtualDisplayRollbackFailed" -or
        [string]$postCreateLastOutcome.virtualDisplay.installStage -cne
          "completed" -or
        [int]$postCreateLastOutcome.virtualDisplay.removeCount -ne 0 -or
        [bool]$postCreateLastOutcome.virtualDisplay.fallbackAttempted -or
        [int]$postCreateLastOutcome.virtualDisplay.observedDeviceCount -ne 1 -or
        [string]$postCreateLastOutcome.virtualDisplay.deviceRecovery -cne
          "completed" -or
        [string]$postCreateLastOutcome.virtualDisplay.residualDeviceState -cne
          "exactOneUnbound" -or
        [string]$postCreateLastOutcome.virtualDisplay.compensationState -cne
          "failed" -or
        [string]$postCreateLastOutcome.virtualDisplay.compensationFailureReason `
          -cne "dependentDevice" -or
        [string]$postCreateLastOutcome.virtualDisplay.terminalReadbackState `
          -cne "completed" -or
        [int]$postCreateLastOutcome.virtualDisplay.createInvocationCount -ne 1 -or
        [string]$postCreateLastOutcome.virtualDisplay.createInvocationIdSha256 `
          -cne "2222222222222222222222222222222222222222222222222222222222222222" -or
        [string]$postCreateLastOutcome.virtualDisplay.preCreateIdentitySha256 `
          -cne "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" -or
        [string]$postCreateLastOutcome.virtualDisplay.postCreateIdentitySha256 `
          -cne "1111111111111111111111111111111111111111111111111111111111111111" -or
        [string]$postCreateLastOutcome.virtualDisplay.postCreateIdentityState `
          -cne "completed" -or
        [string]$postCreateLastOutcome.virtualDisplay.postCreateIdentityReason `
          -cne "none") {
      throw "virtualDisplayDiagnosticProjectionInvalid"
    }
    $script:virtualDisplayInstallStage = "notStarted"
    $script:virtualDisplayReadbackCode = "notAttempted"
    $script:virtualDisplayObservedDeviceCount = -1
    $script:virtualDisplayPresentDeviceCount = -1
    $script:virtualDisplayUniqueDeviceIdsSha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayDriverBindingVerified = $false
    $script:virtualDisplayDeviceRecovery = "failed"
    $script:virtualDisplayResidualState = "unknown"
    $script:virtualDisplayTerminalReadbackState = "failed"
    $script:virtualDisplayTerminalReadbackReason = "unavailable"
    $script:virtualDisplayPostCreateIdentitySha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayPostCreateIdentityState = "failed"
    $script:virtualDisplayPostCreateIdentityReason = "unavailable"
    $prePostOriginal =
      New-VirtualDisplayDiagnostic "virtualDisplayInstallFailed" $false
    $prePostToken = ConvertTo-VirtualDisplayDiagnosticToken $prePostOriginal
    $prePostProjected =
      ConvertFrom-VirtualDisplayDiagnosticToken $prePostToken
    $prePostPath = Get-VirtualDisplayDiagnosticPath
    [IO.File]::WriteAllText(
      $prePostPath, ($prePostOriginal | ConvertTo-Json -Compress),
      [Text.UTF8Encoding]::new($false))
    $prePostFile = Read-VirtualDisplayDiagnostic
    $script:virtualDisplayDiagnostic = $prePostProjected
    $null = Write-InstallerEvidence
    $prePostLastOutcome = [Text.UTF8Encoding]::new(
      $false, $true).GetString(
        [IO.File]::ReadAllBytes($lastOutcomePath)) | ConvertFrom-Json
    $prePostLastOutcomeSha256 = (
      Get-ByteSha256 (
        [IO.File]::ReadAllBytes($lastOutcomePath))).ToLowerInvariant()
    if ([string]$prePostFile.resultCode -cne "virtualDisplayInstallFailed" -or
        [string]$prePostLastOutcome.virtualDisplay.resultCode -cne
          "virtualDisplayInstallFailed" -or
        [int]$prePostLastOutcome.virtualDisplay.createInvocationCount -ne 1 -or
        [string]$prePostLastOutcome.virtualDisplay.postCreateIdentityState `
          -cne "failed" -or
        [string]$prePostLastOutcome.virtualDisplay.postCreateIdentityReason `
          -cne "unavailable" -or
        [string]$prePostLastOutcome.virtualDisplay.postCreateIdentitySha256 `
          -cne "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855") {
      throw "virtualDisplayDiagnosticProjectionInvalid"
    }
    $script:virtualDisplayObservedDeviceCount = 0
    $script:virtualDisplayPresentDeviceCount = 0
    $script:virtualDisplayUniqueDeviceIdsSha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayResidualState = "zero"
    $script:virtualDisplayTerminalReadbackState = "completed"
    $script:virtualDisplayTerminalReadbackReason = "none"
    $script:virtualDisplayPostCreateIdentitySha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayPostCreateIdentityState = "completed"
    $script:virtualDisplayPostCreateIdentityReason = "none"
    $prePostZeroOriginal =
      New-VirtualDisplayDiagnostic "virtualDisplayInstallFailed" $false
    $prePostZeroToken =
      ConvertTo-VirtualDisplayDiagnosticToken $prePostZeroOriginal
    $prePostZeroProjected =
      ConvertFrom-VirtualDisplayDiagnosticToken $prePostZeroToken
    [IO.File]::WriteAllText(
      $prePostPath, ($prePostZeroOriginal | ConvertTo-Json -Compress),
      [Text.UTF8Encoding]::new($false))
    $prePostZeroFile = Read-VirtualDisplayDiagnostic
    $script:virtualDisplayDiagnostic = $prePostZeroProjected
    $null = Write-InstallerEvidence
    $prePostZeroLastOutcome = [Text.UTF8Encoding]::new(
      $false, $true).GetString(
        [IO.File]::ReadAllBytes($lastOutcomePath)) | ConvertFrom-Json
    if ([string]$prePostZeroFile.resultCode -cne
          "virtualDisplayInstallFailed" -or
        [string]$prePostZeroLastOutcome.virtualDisplay.resultCode -cne
          "virtualDisplayInstallFailed" -or
        [int]$prePostZeroLastOutcome.virtualDisplay.observedDeviceCount -ne 0 -or
        [string]$prePostZeroLastOutcome.virtualDisplay.residualDeviceState -cne
          "zero" -or
        [string]$prePostZeroLastOutcome.virtualDisplay.postCreateIdentityState `
          -cne "completed" -or
        [string]$prePostZeroLastOutcome.virtualDisplay.postCreateIdentityReason `
          -cne "none" -or
        [string]$prePostZeroLastOutcome.virtualDisplay.postCreateIdentitySha256 `
          -cne "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855") {
      throw "virtualDisplayDiagnosticProjectionInvalid"
    }
    $prePostZeroLastOutcomeSha256 = (
      Get-ByteSha256 (
        [IO.File]::ReadAllBytes($lastOutcomePath))).ToLowerInvariant()
    $script:virtualDisplayInstallStage = "deviceRemove"
    $script:virtualDisplayReadbackCode = "notAttempted"
    $script:virtualDisplayRemoveExit = 0
    $script:virtualDisplayRemoveCount = 16
    $script:virtualDisplayObservedDeviceCount = 2
    $script:virtualDisplayPresentDeviceCount = 2
    $script:virtualDisplayUniqueDeviceIdsSha256 =
      "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
    $script:virtualDisplayDriverBindingVerified = $false
    $script:virtualDisplayResidualState = "multiple"
    $script:virtualDisplayTerminalReadbackState = "completed"
    $script:virtualDisplayTerminalReadbackReason = "none"
    $script:virtualDisplayCreateInvocationCount = 0
    $script:virtualDisplayCreateInvocationIdSha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayPreCreateIdentitySha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayPostCreateIdentitySha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayPostCreateIdentityState = "notAttempted"
    $script:virtualDisplayPostCreateIdentityReason = "none"
    $removeOriginal = New-VirtualDisplayDiagnostic (
      "virtualDisplayDeviceRemoveFailed") $false
    $removeToken = ConvertTo-VirtualDisplayDiagnosticToken $removeOriginal
    $removePrimaryWriteFailed = $false
    try { Write-VirtualDisplayDiagnostic $removeOriginal } catch {
      $removePrimaryWriteFailed = $true
    }
    $removeProjected =
      ConvertFrom-VirtualDisplayDiagnosticToken $removeToken
    $script:virtualDisplayDiagnostic = $removeProjected
    $null = Write-InstallerEvidence
    $removeLastOutcomeBytes = [IO.File]::ReadAllBytes($lastOutcomePath)
    $removeLastOutcome = [Text.UTF8Encoding]::new(
      $false, $true).GetString($removeLastOutcomeBytes) | ConvertFrom-Json
    if ([string]$removeLastOutcome.failedField -cne "virtualDisplay" -or
        [string]$removeLastOutcome.virtualDisplay.resultCode -cne
          "virtualDisplayDeviceRemoveFailed" -or
        [string]$removeLastOutcome.virtualDisplay.installStage -cne
          "deviceRemove" -or
        [int]$removeLastOutcome.virtualDisplay.removeExitCode -ne 0 -or
        [int]$removeLastOutcome.virtualDisplay.removeCount -ne 16) {
      throw "virtualDisplayDiagnosticProjectionInvalid"
    }
    $script:virtualDisplayInstallStage = "notStarted"
    $script:virtualDisplayReadbackCode = "notAttempted"
    $script:virtualDisplayChildExit = -1
    $script:virtualDisplayRemoveExit = -1
    $script:virtualDisplayRemoveCount = 0
    $script:virtualDisplayProcessCleanup = "notRequired"
    $script:virtualDisplayMarkerStage = "notAttempted"
    $script:virtualDisplayObservedDeviceCount = -1
    $script:virtualDisplayPresentDeviceCount = -1
    $script:virtualDisplayUniqueDeviceIdsSha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayDriverBindingVerified = $false
    $script:virtualDisplayFallbackAttempted = $false
    $script:virtualDisplayFallbackExitCode = -1
    $script:virtualDisplayFallbackStage = "notAttempted"
    $script:virtualDisplayFallbackReason = "none"
    $script:virtualDisplayDeviceRecovery = "notAttempted"
    $script:virtualDisplayResidualState = "unknown"
    $script:virtualDisplayCompensation = "completed"
    $script:virtualDisplayCompensationFailureReason = "none"
    $script:virtualDisplayTerminalReadbackState = "failed"
    $script:virtualDisplayTerminalReadbackReason = "invalid"
    $script:virtualDisplayCreateInvocationCount = 0
    $script:virtualDisplayCreateInvocationIdSha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayPreCreateIdentitySha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayPostCreateIdentitySha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $script:virtualDisplayPostCreateIdentityState = "notAttempted"
    $script:virtualDisplayPostCreateIdentityReason = "none"
    $script:virtualDisplayInventoryStage = "hardwareIds"
    $script:virtualDisplayInventoryFailureStage = "coverage"
    $script:virtualDisplayInventoryOutputReason = "none"
    $script:virtualDisplayInventoryNativeExitCode = -1
    $script:virtualDisplayInventoryChildFailureStage = "none"
    $script:virtualDisplayInventoryCoverageStage = "rowCount"
    $script:virtualDisplayInventoryCoverageReason = "missing"
    $script:virtualDisplayInventoryRequestedCount = 32
    $script:virtualDisplayInventoryReturnedCount = 31
    $script:virtualDisplayInventoryDeviceCount = 369
    $script:virtualDisplayInventoryCurrentBatchIndex = 0
    $script:virtualDisplayInventoryTotalBatchCount = 12
    $script:virtualDisplayInventoryHardwareBatchesCompleted = 0
    $script:virtualDisplayInventoryDriverBatchesCompleted = 0
    $script:virtualDisplayInventoryElapsedMilliseconds = 983
    $script:virtualDisplayInventoryRunBudgetMilliseconds = 9000
    $script:virtualDisplayInventoryHardCapMilliseconds = 10000
    $script:virtualDisplayInventoryCleanupState = "completed"
    $script:virtualDisplayInventoryRootPidZero = $true
    $script:virtualDisplayInventoryJobActiveProcesses = 0
    $inventoryOriginal = New-VirtualDisplayDiagnostic (
      "virtualDisplayDeviceRemoveReadbackFailed") $false
    $inventoryToken = ConvertTo-VirtualDisplayDiagnosticToken $inventoryOriginal
    $inventoryProjected =
      ConvertFrom-VirtualDisplayDiagnosticToken $inventoryToken
    $inventoryPath = Get-VirtualDisplayDiagnosticPath
    [IO.File]::WriteAllText(
      $inventoryPath, ($inventoryOriginal | ConvertTo-Json -Compress),
      [Text.UTF8Encoding]::new($false))
    $inventoryFile = Read-VirtualDisplayDiagnostic
    $script:virtualDisplayDiagnostic = $inventoryProjected
    $null = Write-InstallerEvidence
    $inventoryLastOutcomeBytes = [IO.File]::ReadAllBytes($lastOutcomePath)
    $inventoryLastOutcome = [Text.UTF8Encoding]::new(
      $false, $true).GetString($inventoryLastOutcomeBytes) | ConvertFrom-Json
    if ([string]$inventoryFile.inventoryStage -cne "hardwareIds" -or
        [string]$inventoryProjected.inventoryFailureStage -cne "coverage" -or
        [string]$inventoryProjected.inventoryCoverageStage -cne "rowCount" -or
        [string]$inventoryProjected.inventoryCoverageReason -cne "missing" -or
        [int]$inventoryProjected.inventoryRequestedCount -ne 32 -or
        [int]$inventoryProjected.inventoryReturnedCount -ne 31 -or
        [string]$inventoryLastOutcome.virtualDisplay.resultCode -cne
          "virtualDisplayDeviceRemoveReadbackFailed" -or
        [string]$inventoryLastOutcome.virtualDisplay.inventoryStage -cne
          "hardwareIds" -or
        [int]$inventoryLastOutcome.virtualDisplay.inventoryDeviceCount -ne 369 -or
        [int]$inventoryLastOutcome.virtualDisplay.inventoryCurrentBatchIndex -ne 0 -or
        [int]$inventoryLastOutcome.virtualDisplay.inventoryTotalBatchCount -ne 12 -or
        [int]$inventoryLastOutcome.virtualDisplay.inventoryHardwareBatchesCompleted -ne 0 -or
        [int]$inventoryLastOutcome.virtualDisplay.inventoryDriverBatchesCompleted -ne 0 -or
        [int]$inventoryLastOutcome.virtualDisplay.inventoryElapsedMilliseconds -ne 983 -or
        [string]$inventoryLastOutcome.virtualDisplay.inventoryCleanupState -cne
          "completed" -or
        -not [bool]$inventoryLastOutcome.virtualDisplay.inventoryRootPidZero -or
        [int]$inventoryLastOutcome.virtualDisplay.inventoryJobActiveProcesses -ne 0 -or
        [string]$inventoryLastOutcome.virtualDisplay.terminalReadbackState -cne
          "failed" -or
        [string]$inventoryLastOutcome.virtualDisplay.terminalReadbackReason -cne
          "invalid") {
      throw "virtualDisplayDiagnosticProjectionInvalid"
    }
    $inventoryLastOutcomeSha256 = Get-ByteSha256 $inventoryLastOutcomeBytes
    $inventoryOutputReasonsPersisted = 0
    $outputPersistenceCases = @(
      @{ name = "nativeExit"; reason = "nativeExit"; code = 94;
        stage = "validationQuota" },
      @{ name = "hostNegative"; reason = "nativeExit"; code = 98;
        stage = "hostFailure" },
      @{ name = "hostHigh"; reason = "nativeExit"; code = 98;
        stage = "hostFailure" },
      @{ name = "requestDuplicate"; reason = "nativeExit"; code = 95;
        stage = "requestIdentityDuplicate" },
      @{ name = "requestInvalid"; reason = "nativeExit"; code = 102;
        stage = "requestIdentityInvalid" },
      @{ name = "responseUnknown"; reason = "nativeExit"; code = 96;
        stage = "responseIdentityUnknown" },
      @{ name = "responseConflict"; reason = "nativeExit"; code = 103;
        stage = "responseIdentityConflict"; relation = "conflicting" },
      @{ name = "responseInvalid"; reason = "nativeExit"; code = 101;
        stage = "responseIdentityInvalid"; relation = "invalid";
        invalidReason = "empty"; invalidCount = 1 },
      @{ name = "stderr"; reason = "stderr"; code = 0; stage = "stderr" },
      @{ name = "invokeFailure"; reason = "invokeFailure"; code = -1;
        stage = "processInvoke" })
    foreach ($outputCase in $outputPersistenceCases) {
      $outputReason = [string]$outputCase.reason
      $outputOriginal = (
        ($inventoryOriginal | ConvertTo-Json -Compress) | ConvertFrom-Json)
      $outputOriginal.inventoryFailureStage = "output"
      $outputOriginal.inventoryOutputReason = $outputReason
      $outputOriginal.inventoryNativeExitCode = [int]$outputCase.code
      $outputOriginal.inventoryChildFailureStage = [string]$outputCase.stage
      $outputOriginal.inventoryCoverageStage = "none"
      $outputOriginal.inventoryCoverageReason = "none"
      $outputOriginal.inventoryRequestedCount = -1
      $outputOriginal.inventoryReturnedCount = -1
      if ($outputCase.ContainsKey("relation")) {
        $outputOriginal.inventoryResponseRequestedCount = 32
        $isInvalidResponse =
          [string]$outputCase.relation -ceq "invalid"
        $outputOriginal.inventoryResponseReturnedRowCount = if (
          $isInvalidResponse) { 32 } else { 33 }
        $outputOriginal.inventoryResponseUniqueOrdinalCount = if (
          $isInvalidResponse) { 31 } else { 33 }
        $outputOriginal.inventoryResponseUniqueOrdinalIgnoreCaseCount = if (
          $isInvalidResponse) { 31 } else { 32 }
        $outputOriginal.inventoryResponseDuplicateGroupCount = if (
          $isInvalidResponse) { 0 } else { 1 }
        $outputOriginal.inventoryResponseDuplicateMaxMultiplicity = if (
          $isInvalidResponse) { 1 } else { 2 }
        $outputOriginal.inventoryResponseCaseOnlyDuplicateCount = if (
          $isInvalidResponse) { 0 } else { 1 }
        $outputOriginal.inventoryResponseDataRelation =
          [string]$outputCase.relation
        if ($outputCase.ContainsKey("invalidReason")) {
          $outputOriginal.inventoryResponseInvalidReason =
            [string]$outputCase.invalidReason
          $outputOriginal.inventoryResponseInvalidCount =
            [int]$outputCase.invalidCount
        }
      }
      $outputToken = ConvertTo-VirtualDisplayDiagnosticToken $outputOriginal
      $outputProjected = ConvertFrom-VirtualDisplayDiagnosticToken $outputToken
      [IO.File]::WriteAllText(
        $inventoryPath, ($outputOriginal | ConvertTo-Json -Compress),
        [Text.UTF8Encoding]::new($false))
      $outputFile = Read-VirtualDisplayDiagnostic
      $script:virtualDisplayDiagnostic = $outputProjected
      $null = Write-InstallerEvidence
      $outputOutcome = [IO.File]::ReadAllText(
        $lastOutcomePath, [Text.UTF8Encoding]::new($false, $true)) |
          ConvertFrom-Json
      if ([string]$outputFile.inventoryOutputReason -cne $outputReason -or
          [string]$outputProjected.inventoryFailureStage -cne "output" -or
          [int]$outputOutcome.virtualDisplay.inventoryNativeExitCode -ne
            [int]$outputOriginal.inventoryNativeExitCode -or
          [string]$outputOutcome.virtualDisplay.inventoryChildFailureStage -cne
            [string]$outputOriginal.inventoryChildFailureStage -or
          [string]$outputOutcome.virtualDisplay.inventoryOutputReason -cne
            $outputReason -or
          [string]$outputOutcome.virtualDisplay.resultCode -cne
            "virtualDisplayDeviceRemoveReadbackFailed") {
        throw "virtualDisplayDiagnosticProjectionInvalid"
      }
      $inventoryOutputReasonsPersisted++
    }
    $script:virtualDisplayInventoryFailureStage = "deadline"
    $script:virtualDisplayInventoryOutputReason = "none"
    $script:virtualDisplayInventoryNativeExitCode = -1
    $script:virtualDisplayInventoryChildFailureStage = "none"
    $script:virtualDisplayInventoryCoverageStage = "none"
    $script:virtualDisplayInventoryCoverageReason = "none"
    $script:virtualDisplayInventoryRequestedCount = -1
    $script:virtualDisplayInventoryReturnedCount = -1
    $script:virtualDisplayInventoryCurrentBatchIndex = 11
    $script:virtualDisplayInventoryHardwareBatchesCompleted = 12
    $script:virtualDisplayInventoryElapsedMilliseconds = 9000
    $postLoopDeadlineOriginal = New-VirtualDisplayDiagnostic (
      "virtualDisplayDeviceRemoveReadbackFailed") $false
    $postLoopDeadlineToken = ConvertTo-VirtualDisplayDiagnosticToken (
      $postLoopDeadlineOriginal)
    $postLoopDeadlineProjected = ConvertFrom-VirtualDisplayDiagnosticToken (
      $postLoopDeadlineToken)
    [IO.File]::WriteAllText(
      $inventoryPath,
      ($postLoopDeadlineOriginal | ConvertTo-Json -Compress),
      [Text.UTF8Encoding]::new($false))
    $postLoopDeadlineFile = Read-VirtualDisplayDiagnostic
    $script:virtualDisplayDiagnostic = $postLoopDeadlineProjected
    $null = Write-InstallerEvidence
    $postLoopDeadlineOutcomeBytes = [IO.File]::ReadAllBytes($lastOutcomePath)
    $postLoopDeadlineOutcome = [Text.UTF8Encoding]::new(
      $false, $true).GetString(
        $postLoopDeadlineOutcomeBytes) | ConvertFrom-Json
    if ([string]$postLoopDeadlineFile.inventoryFailureStage -cne "deadline" -or
        [string]$postLoopDeadlineProjected.inventoryCoverageStage -cne "none" -or
        [int]$postLoopDeadlineProjected.inventoryRequestedCount -ne -1 -or
        [string]$postLoopDeadlineOutcome.virtualDisplay.inventoryFailureStage `
          -cne "deadline" -or
        [int]$postLoopDeadlineOutcome.virtualDisplay.inventoryHardwareBatchesCompleted `
          -ne 12) {
      throw "virtualDisplayDiagnosticProjectionInvalid"
    }
    $postLoopDeadlineOutcomeSha256 = Get-ByteSha256 (
      $postLoopDeadlineOutcomeBytes)
    $preReadOriginal = (
      ($original | ConvertTo-Json -Compress) | ConvertFrom-Json)
    $preReadOriginal.finalizePreReadStage = "failed"
    $preReadOriginal.finalizePreReadReason = "timeout"
    $preReadOriginal.finalizePreReadCleanupState = "completed"
    $preReadOriginal.finalizePreReadRootPidZero = $true
    $preReadOriginal.finalizePreReadJobActiveProcesses = 0
    $preReadOriginal.finalizePreReadStdoutClosed = $true
    $preReadOriginal.finalizePreReadStderrClosed = $true
    $preReadOriginal.finalizePreReadDeviceCount = -1
    $preReadOriginal.finalizePreReadPresentDeviceCount = -1
    $preReadOriginal.finalizePreReadIdentitySha256 =
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    $preReadOriginal.finalizePreReadDriverBindingVerified = $false
    $preReadToken = ConvertTo-VirtualDisplayDiagnosticToken $preReadOriginal
    $preReadProjected = ConvertFrom-VirtualDisplayDiagnosticToken $preReadToken
    [IO.File]::WriteAllText(
      $inventoryPath, ($preReadOriginal | ConvertTo-Json -Compress),
      [Text.UTF8Encoding]::new($false))
    $preReadFile = Read-VirtualDisplayDiagnostic
    $script:virtualDisplayDiagnostic = $preReadProjected
    $script:finalFailedField = "virtualDisplay"
    $null = Write-InstallerEvidence
    $preReadOutcomeBytes = [IO.File]::ReadAllBytes($lastOutcomePath)
    $preReadOutcome = [Text.UTF8Encoding]::new(
      $false, $true).GetString($preReadOutcomeBytes) | ConvertFrom-Json
    if ([string]$preReadFile.finalizePreReadReason -cne "timeout" -or
        [string]$preReadProjected.resultCode -cne
          [string]$original.resultCode -or
        [string]$preReadOutcome.failedField -cne "virtualDisplay" -or
        [string]$preReadOutcome.virtualDisplay.resultCode -cne
          [string]$original.resultCode -or
        [string]$preReadOutcome.virtualDisplay.finalizePreReadStage -cne
          "failed" -or
        [string]$preReadOutcome.virtualDisplay.finalizePreReadReason -cne
          "timeout" -or
        [string]$preReadOutcome.virtualDisplay.finalizePreReadCleanupState -cne
          "completed" -or
        -not [bool]$preReadOutcome.virtualDisplay.finalizePreReadRootPidZero -or
        [int]$preReadOutcome.virtualDisplay.finalizePreReadJobActiveProcesses -ne 0 -or
        -not [bool]$preReadOutcome.virtualDisplay.finalizePreReadStdoutClosed -or
        -not [bool]$preReadOutcome.virtualDisplay.finalizePreReadStderrClosed) {
      throw "virtualDisplayDiagnosticProjectionInvalid"
    }
    $preReadOutcomeSha256 = Get-ByteSha256 $preReadOutcomeBytes
    [Console]::Out.WriteLine(([ordered]@{
      code = "virtualDisplayDiagnosticProjectionValidated"
      success = $true
      crossSpliceRejected = $crossSpliceRejected
      primaryWriteFailed = $primaryWriteFailed
      resultCode = [string]$projected.resultCode
      readbackCode = [string]$projected.readbackCode
      observedDeviceCount = [int]$projected.observedDeviceCount
      driverBindingVerified = [bool]$projected.driverBindingVerified
      deviceRecovery = [string]$projected.deviceRecovery
      residualDeviceState = [string]$projected.residualDeviceState
      compensationState = [string]$projected.compensationState
      transactionRollback = [string]$lastOutcome.rollback.state
      tokenLength = [int]$token.Length
      tokenSha256 = Get-ByteSha256 (
        [Text.Encoding]::ASCII.GetBytes($token))
      lastOutcomeSha256 = Get-ByteSha256 $lastOutcomeBytes
      inventoryStage = [string]$inventoryProjected.inventoryStage
      inventoryFailureStage =
        [string]$inventoryProjected.inventoryFailureStage
      inventoryOutputReason =
        [string]$inventoryProjected.inventoryOutputReason
      inventoryNativeExitCode =
        [int]$inventoryProjected.inventoryNativeExitCode
      inventoryChildFailureStage =
        [string]$inventoryProjected.inventoryChildFailureStage
      inventoryCoverageStage =
        [string]$inventoryProjected.inventoryCoverageStage
      inventoryCoverageReason =
        [string]$inventoryProjected.inventoryCoverageReason
      inventoryRequestedCount =
        [int]$inventoryProjected.inventoryRequestedCount
      inventoryReturnedCount =
        [int]$inventoryProjected.inventoryReturnedCount
      inventoryDeviceCount = [int]$inventoryProjected.inventoryDeviceCount
      inventoryHardwareBatchesCompleted =
        [int]$inventoryProjected.inventoryHardwareBatchesCompleted
      inventoryDriverBatchesCompleted =
        [int]$inventoryProjected.inventoryDriverBatchesCompleted
      inventoryCleanupState =
        [string]$inventoryProjected.inventoryCleanupState
      inventoryLastOutcomeSha256 = $inventoryLastOutcomeSha256
      inventoryOutputReasonsPersisted = $inventoryOutputReasonsPersisted
      postLoopDeadlineFailureStage =
        [string]$postLoopDeadlineProjected.inventoryFailureStage
      postLoopDeadlineCoverageStage =
        [string]$postLoopDeadlineProjected.inventoryCoverageStage
      postLoopDeadlineLastOutcomeSha256 = $postLoopDeadlineOutcomeSha256
      finalizePreReadStage =
        [string]$preReadProjected.finalizePreReadStage
      finalizePreReadReason =
        [string]$preReadProjected.finalizePreReadReason
      finalizePreReadCleanupState =
        [string]$preReadProjected.finalizePreReadCleanupState
      finalizePreReadRootPidZero =
        [bool]$preReadProjected.finalizePreReadRootPidZero
      finalizePreReadJobActiveProcesses =
        [int]$preReadProjected.finalizePreReadJobActiveProcesses
      finalizePreReadStdoutClosed =
        [bool]$preReadProjected.finalizePreReadStdoutClosed
      finalizePreReadStderrClosed =
        [bool]$preReadProjected.finalizePreReadStderrClosed
      finalizePreReadPrimaryResultCode =
        [string]$preReadOutcome.virtualDisplay.resultCode
      finalizePreReadLastOutcomeSha256 = $preReadOutcomeSha256
      removeResultCode = [string]$removeProjected.resultCode
      removeInstallStage = [string]$removeProjected.installStage
      removeExitCode = [int]$removeProjected.removeExitCode
      removeCount = [int]$removeProjected.removeCount
      removePrimaryWriteFailed = $removePrimaryWriteFailed
      removeTokenLength = [int]$removeToken.Length
      removeLastOutcomeSha256 = Get-ByteSha256 $removeLastOutcomeBytes
      postCreateResultCode = [string]$postCreateProjected.resultCode
      postCreateRemoveCount = [int]$postCreateProjected.removeCount
      postCreateFallbackAttempted =
        [bool]$postCreateProjected.fallbackAttempted
      postCreateObservedDeviceCount =
        [int]$postCreateProjected.observedDeviceCount
      postCreateDeviceRecovery =
        [string]$postCreateProjected.deviceRecovery
      postCreateResidualDeviceState =
        [string]$postCreateProjected.residualDeviceState
      postCreateCompensationState =
        [string]$postCreateProjected.compensationState
      postCreateCompensationFailureReason =
        [string]$postCreateProjected.compensationFailureReason
      postCreateTerminalReadbackState =
        [string]$postCreateProjected.terminalReadbackState
      postCreateTerminalReadbackReason =
        [string]$postCreateProjected.terminalReadbackReason
      postCreateInvocationCount =
        [int]$postCreateProjected.createInvocationCount
      postCreateInvocationIdSha256 =
        [string]$postCreateProjected.createInvocationIdSha256
      postCreatePreIdentitySha256 =
        [string]$postCreateProjected.preCreateIdentitySha256
      postCreatePostIdentitySha256 =
        [string]$postCreateProjected.postCreateIdentitySha256
      postCreateIdentityState =
        [string]$postCreateProjected.postCreateIdentityState
      postCreateIdentityReason =
        [string]$postCreateProjected.postCreateIdentityReason
      prePostResultCode = [string]$prePostProjected.resultCode
      prePostIdentityState =
        [string]$prePostProjected.postCreateIdentityState
      prePostIdentityReason =
        [string]$prePostProjected.postCreateIdentityReason
      prePostLastOutcomeSha256 =
        $prePostLastOutcomeSha256
      prePostZeroResultCode = [string]$prePostZeroProjected.resultCode
      prePostZeroIdentityState =
        [string]$prePostZeroProjected.postCreateIdentityState
      prePostZeroObservedDeviceCount =
        [int]$prePostZeroProjected.observedDeviceCount
      prePostZeroLastOutcomeSha256 = $prePostZeroLastOutcomeSha256
      postCreatePrimaryWriteFailed = $postCreatePrimaryWriteFailed
      postCreateLastOutcomeSha256 =
        Get-ByteSha256 $postCreateLastOutcomeBytes
    } | ConvertTo-Json -Compress))
    exit 0
  }
  $manifest = Read-Manifest
  $artifacts = Test-Artifacts $manifest
  $script:finalComponents.artifacts = "verified"
  $virtualDisplay = if ($Action -ceq "FinalizeInstall") {
    $null
  } else { Get-VirtualDisplay }
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
      if ($VirtualDisplaySelected) {
        $script:virtualDisplayDiagnostic = if (-not (
            [string]::IsNullOrWhiteSpace($VirtualDisplayDiagnosticToken))) {
          ConvertFrom-VirtualDisplayDiagnosticToken (
            $VirtualDisplayDiagnosticToken)
        } elseif (Test-Path -LiteralPath (
            Get-VirtualDisplayDiagnosticPath) -PathType Leaf) {
          Read-VirtualDisplayDiagnostic
        } else { $null }
        $virtualDisplay = Get-VirtualDisplay
        Set-VirtualDisplayFinalizePreReadAuthority $virtualDisplay
        if ($null -eq $script:virtualDisplayDiagnostic -and
            $script:virtualDisplayFinalizePreReadStage -ceq "failed") {
          $script:virtualDisplayReadbackCode = "virtualDisplayReadbackFailed"
          $script:virtualDisplayDiagnostic = New-VirtualDisplayDiagnostic (
            "virtualDisplayReadbackFailed") $false
          Set-VirtualDisplayFinalizePreReadAuthority $virtualDisplay
        }
        if ($script:virtualDisplayFinalizePreReadStage -ceq "failed") {
          $script:finalFailedField = "virtualDisplay"
          throw "virtualDisplayReadbackFailed"
        }
        if ($null -ne $script:virtualDisplayDiagnostic -and
            [bool]$script:virtualDisplayDiagnostic.success -ne
              ($VirtualDisplayOutcome -ceq "installed")) {
          $script:finalFailedField = "virtualDisplay"
          throw "virtualDisplayDiagnosticInvalid"
        }
      }
      $final = Assert-FinalInstallReadback $manifest $virtualDisplay
      $EvidencePhase = "succeeded"
      $EvidenceSuccess = "true"
      $EvidenceResultCode = "installed"
      $EvidenceFirewall = "configured"
      $EvidenceInstallResidue = "nonEmpty"
      $EvidenceDataRootResidue = "nonEmpty"
      $script:shortcutRollback = $null
      Remove-InstallTransaction
      $null = Write-InstallerEvidence
      # NSIS intentionally performs a byte-exact final success comparison.
      # Windows PowerShell does not preserve ordinary hashtable insertion
      # order, so keep this terminal projection explicitly ordered.
      Write-Outcome "installationFinalized" $true ([ordered]@{
        dataRootState = "existing"
        firewallState = [string]$final.firewall.state
      })
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
    $priorDiagnosticPath = Get-VirtualDisplayDiagnosticPath
    if (Test-Path -LiteralPath $priorDiagnosticPath) {
      Remove-Item -LiteralPath $priorDiagnosticPath -Force
    }
    if (Test-Path -LiteralPath $priorDiagnosticPath) {
      throw "virtualDisplayDiagnosticUnavailable"
    }
    $thumbprint = [string]$manifest.virtualDisplay.certificateThumbprint
    if ([string]$manifest.virtualDisplay.installerTool -cne
          "Deployment/Drivers/sudovda/nefconc.exe" -or
        [string]$manifest.virtualDisplay.installerToolSha256 -cne
          "19a113297eafefd796aa91c1a64d199628d9c58dc53928899d2e5d6a68074efe" -or
        [string]$manifest.virtualDisplay.installerToolSignerThumbprint -cne
          "1F431092EC96A80B41AB5317F53AC02EA6F9B89B") {
      throw "virtualDisplayInstallerToolUnavailable"
    }
    $installerToolPath = Join-Path $installRoot (
      [string]$manifest.virtualDisplay.installerTool)
    if (-not (Test-Path -LiteralPath $installerToolPath -PathType Leaf) -or
        (Get-Item -LiteralPath $installerToolPath).Length -ne 586152 -or
        (Get-FileHash -Algorithm SHA256 -LiteralPath $installerToolPath).Hash -cne
          "19A113297EAFEFD796AA91C1A64D199628D9C58DC53928899D2E5D6A68074EFE") {
      throw "virtualDisplayInstallerToolUnavailable"
    }
    $installerToolSignature =
      Get-AuthenticodeSignature -LiteralPath $installerToolPath
    if ($installerToolSignature.Status -ne "Valid" -or
        $null -eq $installerToolSignature.SignerCertificate -or
        $installerToolSignature.SignerCertificate.Thumbprint -cne
          "1F431092EC96A80B41AB5317F53AC02EA6F9B89B" -or
        $null -eq $installerToolSignature.TimeStamperCertificate) {
      throw "virtualDisplayInstallerToolUnavailable"
    }
    $ownershipPath = Join-Path $installRoot (
      "Deployment/Drivers/sudovda/.ligase-driver-ownership.json")
    $ownershipBytes = if (Test-Path -LiteralPath $ownershipPath -PathType Leaf) {
      [IO.File]::ReadAllBytes($ownershipPath)
    } else {
      $null
    }
    $priorOwnedStores = @()
    if ($null -ne $ownershipBytes) {
      $ownershipRaw = [Text.Encoding]::UTF8.GetString($ownershipBytes)
      try {
        if (-not [LigaseStrictJson]::HasUniqueProperties($ownershipRaw)) {
          throw "virtualDisplayOwnershipInvalid"
        }
        $ownership = $ownershipRaw | ConvertFrom-Json
        $ownershipProperties = @($ownership.PSObject.Properties.Name)
        if ($ownershipProperties.Count -ne 3 -or
            $ownershipProperties -cnotcontains "schemaVersion" -or
            $ownershipProperties -cnotcontains "certificateThumbprint" -or
            $ownershipProperties -cnotcontains "certificateStores" -or
            $ownership.schemaVersion -ne 1 -or
            [string]$ownership.certificateThumbprint -cne $thumbprint) {
          throw "virtualDisplayOwnershipInvalid"
        }
        $priorOwnedStores = @($ownership.certificateStores)
        foreach ($store in $priorOwnedStores) {
          if ($store -isnot [string] -or
              $store -notin @(
                "LocalMachine\Root", "LocalMachine\TrustedPublisher",
                "CurrentUser\Root", "CurrentUser\TrustedPublisher")) {
            throw "virtualDisplayOwnershipInvalid"
          }
        }
      } catch {
        throw "virtualDisplayOwnershipInvalid"
      }
    }
    $before = @(Get-DriverCertificateLocations $thumbprint)
    $ownedStores = @()
    $failureCode = "virtualDisplayInstallFailed"
    try {
      $virtualDisplayInstallerPath = Join-Path $installRoot (
        [string]$manifest.virtualDisplay.installer)
      $snapshotProvider = { Get-VirtualDisplay -IncludeRemovalAuthority }
      $removeInvoker = {
        param([string]$Path)
        $priorAction = $env:LIGASE_VDISPLAY_ACTION
        try {
          $env:LIGASE_VDISPLAY_ACTION = "removeOne"
          Invoke-VirtualDisplayInstaller $Path
        } finally {
          if ($null -eq $priorAction) {
            Remove-Item Env:\LIGASE_VDISPLAY_ACTION -ErrorAction SilentlyContinue
          } else {
            $env:LIGASE_VDISPLAY_ACTION = $priorAction
          }
        }
      }
      $fallbackInvoker = {
        param([string]$Path, [string]$InstanceId)
        if ($InstanceId -cnotmatch '(?i)^ROOT\\DISPLAY\\[0-9A-F]{4}$') {
          $script:virtualDisplayFallbackStage = "tupleValidation"
          $script:virtualDisplayFallbackReason = "tupleInvalid"
          throw "virtualDisplayDeviceRemoveFallbackFailed"
        }
        $priorAction = $env:LIGASE_VDISPLAY_ACTION
        $priorInstance = $env:LIGASE_VDISPLAY_INSTANCE_ID
        $priorPnPUtil = $env:LIGASE_VDISPLAY_PNPUTIL
        try {
          $script:virtualDisplayFallbackStage = "trustedToolResolve"
          try {
            $trustedPnPUtil = [LigaseFileIdentity]::GetTrustedPnPUtilPath()
          } catch {
            $script:virtualDisplayFallbackReason = "trustedToolUnavailable"
            throw
          }
          $env:LIGASE_VDISPLAY_ACTION = "removeInstance"
          $env:LIGASE_VDISPLAY_INSTANCE_ID = $InstanceId
          $env:LIGASE_VDISPLAY_PNPUTIL = $trustedPnPUtil
          $script:virtualDisplayFallbackStage = "processInvoke"
          try {
            Invoke-VirtualDisplayInstaller $Path
          } catch {
            $script:virtualDisplayFallbackReason = switch (
                [string]$_.Exception.Message) {
              "virtualDisplayInstallerTimeout" { "timeout" }
              "virtualDisplayInstallerOutputInvalid" { "outputInvalid" }
              "virtualDisplayInstallerOutputOverflow" { "outputOverflow" }
              "virtualDisplayInstallerOutputUnavailable" {
                "outputUnavailable"
              }
              "virtualDisplayInstallerCleanupFailed" {
                "processStartOrCleanup"
              }
              default { "unknown" }
            }
            throw
          }
        } finally {
          if ($null -eq $priorAction) {
            Remove-Item Env:\LIGASE_VDISPLAY_ACTION -ErrorAction SilentlyContinue
          } else { $env:LIGASE_VDISPLAY_ACTION = $priorAction }
          if ($null -eq $priorInstance) {
            Remove-Item Env:\LIGASE_VDISPLAY_INSTANCE_ID `
              -ErrorAction SilentlyContinue
          } else { $env:LIGASE_VDISPLAY_INSTANCE_ID = $priorInstance }
          if ($null -eq $priorPnPUtil) {
            Remove-Item Env:\LIGASE_VDISPLAY_PNPUTIL `
              -ErrorAction SilentlyContinue
          } else { $env:LIGASE_VDISPLAY_PNPUTIL = $priorPnPUtil }
        }
      }
      $failureCode = "virtualDisplayDeviceRemoveFailed"
      try {
        $removedDeviceCount = Invoke-VirtualDisplayRemovalReconciliation `
          $virtualDisplayInstallerPath $snapshotProvider $removeInvoker `
          $fallbackInvoker
      } catch {
        if ([string]$_.Exception.Message -in @(
            "virtualDisplayDeviceRemoveFailed",
            "virtualDisplayDeviceRemoveReadbackFailed",
            "virtualDisplayDeviceRemoveSettleFailed",
            "virtualDisplayDeviceZeroProofFailed",
            "virtualDisplayDeviceRemoveFallbackFailed",
            "virtualDisplayInstallerCleanupFailed",
            "virtualDisplayInstallerOutputInvalid",
            "virtualDisplayInstallerOutputOverflow",
            "virtualDisplayInstallerOutputUnavailable",
            "virtualDisplayInstallerTimeout")) {
          $failureCode = [string]$_.Exception.Message
        }
        throw
      }
      $failureCode = "virtualDisplayInstallFailed"
      $script:virtualDisplayPreCreateIdentitySha256 =
        [string]$script:virtualDisplayUniqueDeviceIdsSha256
      $createInvocationId = [Guid]::NewGuid().ToString("N")
      $script:virtualDisplayCreateInvocationIdSha256 = (
        Get-ByteSha256 (
          [Text.Encoding]::ASCII.GetBytes($createInvocationId))).ToLowerInvariant()
      $script:virtualDisplayCreateInvocationCount = 1
      $script:virtualDisplayPostCreateIdentityState = "notAttempted"
      $script:virtualDisplayPostCreateIdentityReason = "none"
      $priorAction = $env:LIGASE_VDISPLAY_ACTION
      try {
        $env:LIGASE_VDISPLAY_ACTION = "install"
        $installerResult =
          Invoke-VirtualDisplayInstaller $virtualDisplayInstallerPath
      } finally {
        if ($null -eq $priorAction) {
          Remove-Item Env:\LIGASE_VDISPLAY_ACTION -ErrorAction SilentlyContinue
        } else {
          $env:LIGASE_VDISPLAY_ACTION = $priorAction
        }
      }
      $after = @(Get-DriverCertificateLocations $thumbprint)
      $ownedStores = @($after | Where-Object { $before -notcontains $_ })
      Assert-VirtualDisplayInstallerTuple $installerResult "install"
      $script:virtualDisplayRemoveExit = 0
      $script:virtualDisplayRemoveCount = $removedDeviceCount

      $displayReadback = Get-VirtualDisplay
      $script:virtualDisplayReadbackCode =
        [string]$displayReadback.machineCode
      $null = Set-VirtualDisplayTerminalResidualAuthority {
        $displayReadback
      }
      Set-VirtualDisplayPostCreateIdentityAuthority
      $failureCode = "virtualDisplayReadbackFailed"
      if ($displayReadback.state -notin @("available", "rebootRequired") -or
          -not [bool]$displayReadback.driverBindingVerified) {
        throw "virtualDisplayReadbackFailed"
      }
      $markerJson = [ordered]@{
        schemaVersion = 1
        certificateThumbprint = $thumbprint
        certificateStores = @(
          $priorOwnedStores + $ownedStores | Sort-Object -Unique)
      } | ConvertTo-Json -Compress
      $markerBytes = [Text.UTF8Encoding]::new($false).GetBytes($markerJson)
      $failureCode = "virtualDisplayMarkerCommitFailed"
      $script:virtualDisplayMarkerStage = "commit"
      Write-VirtualDisplayOwnershipMarkerAtomic $ownershipPath $markerBytes
      $script:virtualDisplayMarkerStage = "completed"
    } catch {
      $rollbackFailed = $false
      $compensationFailures = [Collections.Generic.List[string]]::new()
      try {
        if ($null -eq $ownershipBytes) {
          if (Test-Path -LiteralPath $ownershipPath) {
            Remove-Item -LiteralPath $ownershipPath -Force
          }
          if (Test-Path -LiteralPath $ownershipPath) {
            $rollbackFailed = $true
            $compensationFailures.Add("markerRestore")
          }
        } else {
          $markerAlreadyExact =
            (Test-Path -LiteralPath $ownershipPath -PathType Leaf) -and
            (Test-ExactBytes $ownershipBytes (
                [IO.File]::ReadAllBytes($ownershipPath)))
          if (-not $markerAlreadyExact) {
            Write-VirtualDisplayOwnershipMarkerAtomic `
              $ownershipPath $ownershipBytes $false
          }
          if (-not (Test-Path -LiteralPath $ownershipPath -PathType Leaf) -or
              -not (Test-ExactBytes $ownershipBytes (
                [IO.File]::ReadAllBytes($ownershipPath)))) {
            $rollbackFailed = $true
            $compensationFailures.Add("markerRestore")
          }
        }
      } catch {
        $rollbackFailed = $true
        $compensationFailures.Add("markerRestore")
      }
      $dependentVirtualDisplay = $true
      try {
        $dependentVirtualDisplay = [bool](Test-DependentVirtualDisplay)
      } catch {
        $rollbackFailed = $true
        $compensationFailures.Add("dependentDevice")
      }
      if ($dependentVirtualDisplay) {
        if ($ownedStores.Count -gt 0) {
          $rollbackFailed = $true
          $compensationFailures.Add("dependentDevice")
        }
      } else {
        foreach ($store in $ownedStores) {
          try {
            Remove-Item -LiteralPath "Cert:\$store\$thumbprint" -Force
          } catch {
            $rollbackFailed = $true
            $compensationFailures.Add("certificateRemove")
          }
        }
      }
      foreach ($store in $ownedStores) {
        try {
          $certificateRemains =
            Test-Path -LiteralPath "Cert:\$store\$thumbprint"
        } catch {
          $certificateRemains = $true
        }
        if ($certificateRemains) {
          $rollbackFailed = $true
          $compensationFailures.Add("certificateResidue")
        }
      }
      try {
        $tempResidue = @(Get-ChildItem -LiteralPath (
            Split-Path -Parent $ownershipPath) -Force -Filter (
            ".ligase-driver-ownership-*.tmp"))
      } catch {
        $tempResidue = @("unknown")
      }
      if ($tempResidue.Count -ne 0) {
        $rollbackFailed = $true
        $compensationFailures.Add("temporaryResidue")
      }
      $null = Set-VirtualDisplayTerminalResidualAuthority {
        Get-VirtualDisplay
      }
      Set-VirtualDisplayPostCreateIdentityAuthority
      $script:virtualDisplayCompensation = if ($rollbackFailed) {
        "failed"
      } else { "completed" }
      $uniqueCompensationFailures = @(
        $compensationFailures | Sort-Object -Unique)
      $script:virtualDisplayCompensationFailureReason = if (
          $uniqueCompensationFailures.Count -eq 0) {
        "none"
      } elseif ($uniqueCompensationFailures.Count -eq 1) {
        [string]$uniqueCompensationFailures[0]
      } else { "multiple" }
      if ($rollbackFailed) { throw "virtualDisplayRollbackFailed" }
      throw $failureCode
    }
    $script:virtualDisplayDiagnostic =
      New-VirtualDisplayDiagnostic "virtualDisplayInstalled" $true
    Write-VirtualDisplayDiagnostic $script:virtualDisplayDiagnostic
    Write-Outcome "virtualDisplayInstalled" $true
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
  if ($Action -eq "InstallVirtualDisplay") {
    $script:virtualDisplayDiagnostic =
      New-VirtualDisplayDiagnostic $code $false
    $diagnosticToken =
      ConvertTo-VirtualDisplayDiagnosticToken $script:virtualDisplayDiagnostic
    try {
      Write-VirtualDisplayDiagnostic $script:virtualDisplayDiagnostic
    } catch {
      # Keep the primary failure as authority. The bounded ASCII token is
      # independently validated by FinalizeInstall before last-outcome writes.
    }
    [Console]::Out.WriteLine($diagnosticToken)
    exit 10
  }
  Write-Outcome $code $false
  exit 10
} finally {
  if ($held) { $mutex.ReleaseMutex() }
  $mutex.Dispose()
}
