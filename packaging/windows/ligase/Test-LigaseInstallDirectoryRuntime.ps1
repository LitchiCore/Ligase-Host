[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string] $MakeNsis,
  [Parameter(Mandatory)]
  [string] $OutputRoot,
  [string] $DotNet = "dotnet.exe",
  [ValidateSet(
    "all","secondaryTerminateFailure","secondaryWaitFailure")]
  [string] $InstallerProcessCaseFilter = "all",
  [switch] $StopAfterInstallerProcessCases
)

$ErrorActionPreference = "Stop"

$root = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $root) {
  if (@(Get-ChildItem -LiteralPath $root -Force).Count -ne 0) {
    throw "harnessOutputRootNotClean"
  }
} else {
  New-Item -ItemType Directory -Path $root | Out-Null
}
$compatibilityProcessStartCount = 0
function Get-CompatibleSha256([AllowEmptyCollection()][byte[]]$Bytes) {
  if ($null -eq $Bytes) { throw "shaInputNull" }
  $sha = [Security.Cryptography.SHA256]::Create()
  try {
    $hash = $sha.ComputeHash($Bytes)
  } finally {
    $sha.Dispose()
  }
  return [BitConverter]::ToString($hash).Replace("-", "")
}
$emptyBytes = [byte[]]::new(0)
if ((Get-CompatibleSha256 $emptyBytes) -cne
    "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855" -or
    (Get-CompatibleSha256 (
      [Text.Encoding]::ASCII.GetBytes("abc"))) -cne
    "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD") {
  throw "shaCompatibilitySelfTestFailed"
}
$compatibilityBytes = [Text.UTF8Encoding]::new(
  $false, $true).GetBytes("bounded-host-evidence-compatibility")
$compatibilityPath = Join-Path $root "evidence-compatibility.json"
$compatibilityTemp = Join-Path $root (
  "." + [guid]::NewGuid().ToString("N") + ".compat.tmp")
$compatibilityStream = [IO.FileStream]::new(
  $compatibilityTemp, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
  [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
try {
  $compatibilityStream.Write(
    $compatibilityBytes, 0, $compatibilityBytes.Length)
  $compatibilityStream.Flush($true)
} finally {
  $compatibilityStream.Dispose()
}
[IO.File]::Move($compatibilityTemp, $compatibilityPath)
$compatibilityReadback = [IO.File]::ReadAllBytes($compatibilityPath)
if ([Convert]::ToBase64String($compatibilityReadback) -cne
      [Convert]::ToBase64String($compatibilityBytes) -or
    (Get-CompatibleSha256 $compatibilityReadback) -cne
      (Get-CompatibleSha256 $compatibilityBytes) -or
    $compatibilityProcessStartCount -ne 0) {
  throw "evidenceCompatibilitySelfTestFailed"
}
$harness = Join-Path $root "LigaseInstallDirectoryHarness.exe"
$harnessDiagnostic = Join-Path $root "harness-runtime.diagnostic"
$resolver = Join-Path $PSScriptRoot "Resolve-LigaseInstallDirectory.ps1"
$finalizationStub = Join-Path $root "finalization-stub.ps1"
@'
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Mode)
$ErrorActionPreference = "Stop"
switch ($Mode) {
  "success" {
    [Console]::Out.WriteLine(
      '{"code":"installationFinalized","success":true,"dataRootState":"existing","firewallState":"configured"}')
    exit 0
  }
  "extra" {
    [Console]::Out.WriteLine(
      '{"code":"installationFinalized","success":true,"dataRootState":"existing","firewallState":"configured"}')
    [Console]::Out.WriteLine('unexpected')
    exit 0
  }
  "malformed" {
    [Console]::Out.WriteLine('{"code":"installationFinalized"')
    exit 0
  }
  "nonzero" {
    [Console]::Out.WriteLine(
      '{"code":"installationFinalized","success":true,"dataRootState":"existing","firewallState":"configured"}')
    exit 18
  }
  default { exit 19 }
}
'@ | Set-Content -LiteralPath $finalizationStub -Encoding UTF8
& $MakeNsis `
  "/DOutputFile=$harness" `
  "/DResolverScript=$resolver" `
  "/DHarnessDiagnosticPath=$harnessDiagnostic" `
  "/DFinalizationStub=$finalizationStub" `
  (Join-Path $PSScriptRoot "LigaseInstallDirectoryHarness.nsi") | Out-Null
if ($LASTEXITCODE -ne 0) { throw "harnessCompileFailed" }

$argumentListRunnerRoot = Join-Path $root "argument-list-runner"
$argumentListRunnerOutput = Join-Path $argumentListRunnerRoot "out"
New-Item -ItemType Directory -Path $argumentListRunnerRoot -Force | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (
  Join-Path $argumentListRunnerRoot "ArgumentListRunner.csproj") -Encoding UTF8
@'
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

if (args.Length < 1)
    return 90;

if (args[0] == "--emit")
{
    if (args.Length != 2)
        return 92;
    var output = Console.OpenStandardOutput();
    var error = Console.OpenStandardError();
    static byte[] Repeat(byte value, int count)
    {
        var bytes = new byte[count];
        Array.Fill(bytes, value);
        return bytes;
    }
    static void Write(Stream stream, byte[] bytes)
    {
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }
    switch (args[1])
    {
        case "asciiOverflowStdout":
            Write(output, Repeat(0x78, 4096));
            Thread.Sleep(60000);
            return 0;
        case "asciiOverflowStderr":
            Write(error, Repeat(0x78, 4096));
            Thread.Sleep(60000);
            return 0;
        case "utf8BoundaryStdout":
        {
            var bytes = Repeat(0x61, 513);
            bytes[511] = 0xC3;
            bytes[512] = 0xA9;
            Write(output, bytes);
            Thread.Sleep(60000);
            return 0;
        }
        case "utf8BoundaryStderr":
        {
            var bytes = Repeat(0x61, 513);
            bytes[511] = 0xC3;
            bytes[512] = 0xA9;
            Write(error, bytes);
            Thread.Sleep(60000);
            return 0;
        }
        case "internalInvalidStdout":
        {
            var bytes = Repeat(0x61, 600);
            bytes[4] = 0xFF;
            Write(output, bytes);
            Thread.Sleep(60000);
            return 0;
        }
        case "internalInvalidStderr":
        {
            var bytes = Repeat(0x61, 600);
            bytes[4] = 0xFF;
            Write(error, bytes);
            Thread.Sleep(60000);
            return 0;
        }
        case "invalidUtf8Stdout":
            Write(output, new byte[] { 0xFF });
            return 0;
        case "invalidUtf8Stderr":
            Write(error, new byte[] { 0xFF });
            return 0;
        case "incompleteUtf8Stdout":
            Write(output, new byte[] { 0xC3 });
            return 0;
        case "incompleteUtf8Stderr":
            Write(error, new byte[] { 0xC3 });
            return 0;
        case "dualOverflow":
            Write(output, Repeat(0x78, 4096));
            Write(error, Repeat(0x78, 4096));
            Thread.Sleep(60000);
            return 0;
        case "jsonExtra":
            Write(output, Encoding.UTF8.GetBytes(
                "{\"code\":\"one\"}\n{\"code\":\"two\"}\n"));
            return 0;
        case "jsonMalformed":
            Write(output, Encoding.UTF8.GetBytes("{\n"));
            return 0;
        case "hang":
            Thread.Sleep(60000);
            return 0;
        case "delayedSentinel":
            Thread.Sleep(10000);
            Write(output, Encoding.ASCII.GetBytes("sentinel"));
            return 0;
        case "treeInheritedPipe":
        {
            var self = Environment.GetCommandLineArgs()[0];
            var child = new System.Diagnostics.ProcessStartInfo(
                Environment.ProcessPath!)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            child.ArgumentList.Add(self);
            child.ArgumentList.Add("--emit");
            child.ArgumentList.Add("delayedSentinel");
            System.Diagnostics.Process.Start(child)?.Dispose();
            Thread.Sleep(60000);
            return 0;
        }
        case "exitZero":
            return 0;
        default:
            return 92;
    }
}

if (args[0] == "--bounded-capture")
{
    if (args.Length < 7 ||
        !int.TryParse(args[1], out var runTimeoutMs) ||
        !int.TryParse(args[2], out var cleanupReserveMs) ||
        !int.TryParse(args[3], out var maxBytes) ||
        runTimeoutMs < 1 || runTimeoutMs > 115000 ||
        cleanupReserveMs < 1 || cleanupReserveMs > 5000 ||
        runTimeoutMs + cleanupReserveMs > 120000 ||
        maxBytes < 1 || maxBytes > 65536 ||
        args[4] is not (
            "none" or "accountingFault" or "executableResolveFault" or
            "pipeFault" or "jobFault" or "attributeFault" or "createFault" or
            "assignFault" or "resumeFault" or "managedHandoffFault" or
            "processWrapperFault" or "stdoutSafeHandleFault" or
            "stdoutStreamFault" or "stderrSafeHandleFault" or
            "stderrStreamFault" or "stdoutWriteCloseFault" or
            "stderrWriteCloseFault" or "threadCloseFault"))
        return 92;
    var hardCapMs = runTimeoutMs + cleanupReserveMs;
    var fault = args[4];

    var deadline = System.Diagnostics.Stopwatch.StartNew();
    NativeJobProcess nativeStarted;
    try
    {
        nativeStarted = NativeJobProcess.Start(
            args[5], args.Skip(6).ToArray(), fault);
    }
    catch (NativeStartException failure)
    {
        var emptySha =
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "boundedProcessV1",
            startStage = failure.Stage,
            startCode = failure.Code,
            exitCode = -1,
            timedOut = false,
            overflow = false,
            pipeFault = false,
            killAttempted = true,
            cleanupCompleted = failure.CleanupProven,
            jobEmpty = failure.JobActiveProcesses == 0,
            jobActiveProcesses = failure.JobActiveProcesses,
            pid = failure.CleanupProven ? 0 : failure.Pid,
            elapsedMilliseconds = deadline.ElapsedMilliseconds,
            hardCapMilliseconds = hardCapMs,
            stdoutOverflow = false,
            stderrOverflow = false,
            stdoutRawLength = 0,
            stderrRawLength = 0,
            stdoutRawSha = emptySha,
            stderrRawSha = emptySha,
            stdoutDecoderState = "closed",
            stderrDecoderState = "closed",
            stdoutPendingTailLength = 0,
            stderrPendingTailLength = 0,
            stdoutText = "",
            stderrText = "",
        }));
        return failure.CleanupProven ? 95 : 93;
    }
    using var nativeBounded = nativeStarted;
    var processBounded = nativeBounded.Process;
    var pid = processBounded.Id;
    var overflowSignal = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    async Task<(byte[] Bytes, bool Overflow, long RawLength, string RawSha)> ReadBoundedAsync(
        Stream stream)
    {
        using var stored = new MemoryStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128];
        var overflow = false;
        long rawLength = 0;
        while (true)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0)
                break;
            hash.AppendData(buffer, 0, count);
            rawLength += count;
            var remaining = maxBytes - (int)stored.Length;
            if (remaining > 0)
                stored.Write(buffer, 0, Math.Min(remaining, count));
            if (count > remaining)
            {
                overflow = true;
                overflowSignal.TrySetResult(true);
            }
        }
        return (
            stored.ToArray(), overflow, rawLength,
            BitConverter.ToString(hash.GetHashAndReset()).Replace("-", ""));
    }

    (string Text, string State, int PendingTailLength) DecodeBounded(
        byte[] bytes, bool overflow)
    {
        var strict = new UTF8Encoding(false, true);
        try
        {
            return (strict.GetString(bytes), "closed", 0);
        }
        catch (DecoderFallbackException)
        {
            if (overflow)
            {
                for (var tail = 1; tail <= Math.Min(3, bytes.Length); tail++)
                {
                    try
                    {
                        return (
                            strict.GetString(bytes, 0, bytes.Length - tail),
                            "pendingTail", tail);
                    }
                    catch (DecoderFallbackException) { }
                }
            }
            return ("", "invalid", 0);
        }
    }

    var stdoutTask = ReadBoundedAsync(nativeBounded.StandardOutput);
    var stderrTask = ReadBoundedAsync(nativeBounded.StandardError);
    var waitTask = processBounded.WaitForExitAsync();
    var readTask = Task.WhenAll(stdoutTask, stderrTask);
    var timeoutTask = Task.Delay(runTimeoutMs);
    var first = await Task.WhenAny(
        waitTask, timeoutTask, overflowSignal.Task, readTask);
    var timedOut = first == timeoutTask;
    var overflowDetected = first == overflowSignal.Task;
    var pipeFault = first == readTask && readTask.IsFaulted;
    var killAttempted = false;
    if (timedOut || overflowDetected || pipeFault)
    {
        killAttempted = true;
        nativeBounded.Terminate();
    }

    var remaining = Math.Max(
        0, hardCapMs - (int)deadline.ElapsedMilliseconds);
    var cleanupDeadline = Task.Delay(remaining);
    var waitCompleted = await Task.WhenAny(waitTask, cleanupDeadline) == waitTask;
    var readsCompleted = await Task.WhenAny(
        readTask, cleanupDeadline) != cleanupDeadline;
    var exited = false;
    try { exited = processBounded.HasExited; } catch { }
    var activeProcessCount = nativeBounded.ActiveProcessCount();
    while (activeProcessCount != 0 &&
           deadline.ElapsedMilliseconds < hardCapMs)
    {
        var pollRemaining = Math.Max(
            0, hardCapMs - (int)deadline.ElapsedMilliseconds);
        if (pollRemaining == 0)
            break;
        await Task.Delay(Math.Min(10, pollRemaining));
        activeProcessCount = nativeBounded.ActiveProcessCount();
    }
    var actualJobEmpty = activeProcessCount == 0;
    var jobEmpty = fault == "accountingFault" ? false : actualJobEmpty;
    var cleanupCompleted =
        waitCompleted && readsCompleted && exited && jobEmpty;
    var stdout = stdoutTask.IsCompletedSuccessfully
        ? stdoutTask.Result
        : (Bytes: Array.Empty<byte>(), Overflow: false, RawLength: 0L, RawSha: "");
    var stderr = stderrTask.IsCompletedSuccessfully
        ? stderrTask.Result
        : (Bytes: Array.Empty<byte>(), Overflow: false, RawLength: 0L, RawSha: "");
    var overflow = overflowDetected || stdout.Overflow || stderr.Overflow;
    var stdoutDecoded = DecodeBounded(stdout.Bytes, stdout.Overflow);
    var stderrDecoded = DecodeBounded(stderr.Bytes, stderr.Overflow);
    if (stdoutDecoded.State == "invalid" || stderrDecoded.State == "invalid")
        pipeFault = true;
    var exitCode = exited ? processBounded.ExitCode : -1;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schema = "boundedProcessV1",
        startStage = "none",
        startCode = 0,
        exitCode,
        timedOut,
        overflow,
        pipeFault,
        killAttempted,
        cleanupCompleted,
        jobEmpty,
        jobActiveProcesses = activeProcessCount,
        pid = exited && actualJobEmpty ? 0 : pid,
        elapsedMilliseconds = deadline.ElapsedMilliseconds,
        hardCapMilliseconds = hardCapMs,
        stdoutOverflow = stdout.Overflow,
        stderrOverflow = stderr.Overflow,
        stdoutRawLength = stdout.RawLength,
        stderrRawLength = stderr.RawLength,
        stdoutRawSha = stdout.RawSha,
        stderrRawSha = stderr.RawSha,
        stdoutDecoderState = stdoutDecoded.State,
        stderrDecoderState = stderrDecoded.State,
        stdoutPendingTailLength = stdoutDecoded.PendingTailLength,
        stderrPendingTailLength = stderrDecoded.PendingTailLength,
        stdoutText = stdoutDecoded.Text,
        stderrText = stderrDecoded.Text,
    }));
    return cleanupCompleted ? 0 : 93;
}

var start = new System.Diagnostics.ProcessStartInfo(args[0])
{
    UseShellExecute = false,
    CreateNoWindow = true,
};
foreach (var argument in args.Skip(1))
    start.ArgumentList.Add(argument);
using var process = System.Diagnostics.Process.Start(start);
if (process is null)
    return 91;
await process.WaitForExitAsync();
return process.ExitCode;

sealed class NativeJobProcess : IDisposable
{
    const uint CreateSuspended = 0x00000004;
    const uint CreateNoWindow = 0x08000000;
    const uint ExtendedStartupInfoPresent = 0x00080000;
    const uint StartfUseStdHandles = 0x00000100;
    const uint HandleFlagInherit = 0x00000001;
    const uint KillOnClose = 0x00002000;
    const int ExtendedLimit = 9;
    const int BasicAccounting = 1;
    static readonly IntPtr HandleListAttribute = new(0x00020002);

    [StructLayout(LayoutKind.Sequential)]
    struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        public short ShowWindow, ReservedSize;
        public IntPtr ReservedPointer, StandardInput, StandardOutput, StandardError;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct StartupInfoEx
    {
        public StartupInfo Startup;
        public IntPtr AttributeList;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimit
    {
        public long ProcessTime, JobTime;
        public uint LimitFlags;
        public UIntPtr MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct IoCounters
    {
        public ulong ReadOps, WriteOps, OtherOps;
        public ulong ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimitInfo
    {
        public BasicLimit Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct AccountingInfo
    {
        public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CreatePipe(
        out IntPtr read, out IntPtr write,
        ref SecurityAttributes attributes, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetSystemDirectoryW(
        [Out] StringBuilder buffer, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcessW(
        string application, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment,
        string? currentDirectory, ref StartupInfoEx startup,
        out ProcessInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool InitializeProcThreadAttributeList(
        IntPtr list, int count, uint flags, ref UIntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool UpdateProcThreadAttribute(
        IntPtr list, uint flags, IntPtr attribute, IntPtr value,
        UIntPtr size, IntPtr previous, IntPtr returnSize);
    [DllImport("kernel32.dll")]
    static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(
        IntPtr job, int informationClass, IntPtr information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateJobObject(IntPtr job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool QueryInformationJobObject(
        IntPtr job, int informationClass, IntPtr information,
        uint length, out uint returned);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    public System.Diagnostics.Process Process { get; private set; } = null!;
    public FileStream StandardOutput { get; private set; } = null!;
    public FileStream StandardError { get; private set; } = null!;
    public static bool LastCleanupProven { get; private set; }
    public static int LastCleanupPid { get; private set; }
    public static uint LastJobActiveProcesses { get; private set; }
    IntPtr job;
    IntPtr processHandle;

    static string Quote(string value)
    {
        if (value.Length > 0 &&
            !value.Any(c => char.IsWhiteSpace(c) || c == '"'))
            return value;
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"')
            {
                result.Append('\\', slashes * 2 + 1).Append('"');
                slashes = 0;
                continue;
            }
            result.Append('\\', slashes).Append(character);
            slashes = 0;
        }
        result.Append('\\', slashes * 2).Append('"');
        return result.ToString();
    }

    static bool IsClosedExecutableFile(string path)
    {
        if (!Path.IsPathFullyQualified(path) ||
            !string.Equals(
                Path.GetFullPath(path), path,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
            return false;
        var attributes = File.GetAttributes(path);
        return (attributes & (
            FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
    }

    static string ResolveTrustedExecutable(string executable)
    {
        if (string.Equals(
            executable, "powershell.exe", StringComparison.Ordinal))
        {
            var systemDirectory = new StringBuilder(32768);
            var length = GetSystemDirectoryW(
                systemDirectory, (uint)systemDirectory.Capacity);
            if (length == 0 || length >= (uint)systemDirectory.Capacity)
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "systemDirectoryUnavailable");
            var candidate = Path.GetFullPath(Path.Combine(
                systemDirectory.ToString(),
                "WindowsPowerShell", "v1.0", "powershell.exe"));
            if (!IsClosedExecutableFile(candidate))
                throw new InvalidOperationException(
                    "trustedPowerShellUnavailable");
            return candidate;
        }
        var currentHost = Environment.ProcessPath;
        if (currentHost is null ||
            !Path.IsPathFullyQualified(executable) ||
            !string.Equals(
                Path.GetFullPath(executable), Path.GetFullPath(currentHost),
                StringComparison.OrdinalIgnoreCase) ||
            !IsClosedExecutableFile(executable))
            throw new InvalidOperationException(
                "untrustedExecutableIdentity");
        return executable;
    }

    static void CloseRawHandle(ref IntPtr handle, string failureName)
    {
        if (handle == IntPtr.Zero)
            return;
        if (!CloseHandle(handle))
            throw new Win32Exception(
                Marshal.GetLastWin32Error(), failureName);
        handle = IntPtr.Zero;
    }

    static void CloseRawAfterFailure(
        ref IntPtr handle, ref string stage, ref int code)
    {
        if (handle == IntPtr.Zero)
            return;
        if (CloseHandle(handle))
        {
            handle = IntPtr.Zero;
            return;
        }
        stage = "managedHandoff";
        var closeCode = Marshal.GetLastWin32Error();
        code = closeCode == 0 ? 20018 : closeCode;
    }

    public static NativeJobProcess Start(
        string executable, string[] arguments, string fault)
    {
        IntPtr stdoutRead = IntPtr.Zero, stdoutWrite = IntPtr.Zero;
        IntPtr stderrRead = IntPtr.Zero, stderrWrite = IntPtr.Zero;
        IntPtr job = IntPtr.Zero, attributes = IntPtr.Zero;
        IntPtr handleList = IntPtr.Zero;
        var information = new ProcessInformation();
        var processCreated = false;
        var assigned = false;
        System.Diagnostics.Process? managedProcess = null;
        SafeFileHandle? managedStdoutHandle = null;
        SafeFileHandle? managedStderrHandle = null;
        FileStream? managedStdoutStream = null;
        FileStream? managedStderrStream = null;
        var stage = "executableResolve";
        try
        {
            LastCleanupProven = false;
            LastCleanupPid = 0;
            LastJobActiveProcesses = uint.MaxValue;
            if (fault == "executableResolveFault")
                throw new InvalidOperationException(
                    "validationExecutableResolveFault");
            var resolvedExecutable = ResolveTrustedExecutable(executable);
            stage = "pipe";
            var security = new SecurityAttributes {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                InheritHandle = true
            };
            if (fault == "pipeFault")
                throw new InvalidOperationException("validationPipeFault");
            if (!CreatePipe(out stdoutRead, out stdoutWrite, ref security, 0) ||
                !CreatePipe(out stderrRead, out stderrWrite, ref security, 0) ||
                !SetHandleInformation(stdoutRead, HandleFlagInherit, 0) ||
                !SetHandleInformation(stderrRead, HandleFlagInherit, 0))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "nativePipeFailed");
            stage = "job";
            if (fault == "jobFault")
                throw new InvalidOperationException("validationJobFault");
            job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "nativeJobFailed");
            var limits = new ExtendedLimitInfo();
            limits.Basic.LimitFlags = KillOnClose;
            var limitsSize = Marshal.SizeOf<ExtendedLimitInfo>();
            var limitsPointer = Marshal.AllocHGlobal(limitsSize);
            try
            {
                Marshal.StructureToPtr(limits, limitsPointer, false);
                if (!SetInformationJobObject(
                    job, ExtendedLimit, limitsPointer, (uint)limitsSize))
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(), "nativeJobLimitFailed");
            }
            finally { Marshal.FreeHGlobal(limitsPointer); }

            stage = "attribute";
            if (fault == "attributeFault")
                throw new InvalidOperationException("validationAttributeFault");
            UIntPtr attributeSize = UIntPtr.Zero;
            InitializeProcThreadAttributeList(
                IntPtr.Zero, 1, 0, ref attributeSize);
            attributes = Marshal.AllocHGlobal(
                checked((int)attributeSize.ToUInt64()));
            if (!InitializeProcThreadAttributeList(
                attributes, 1, 0, ref attributeSize))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "nativeAttributeFailed");
            handleList = Marshal.AllocHGlobal(IntPtr.Size * 2);
            Marshal.WriteIntPtr(handleList, 0, stdoutWrite);
            Marshal.WriteIntPtr(handleList, IntPtr.Size, stderrWrite);
            if (!UpdateProcThreadAttribute(
                attributes, 0, HandleListAttribute, handleList,
                new UIntPtr((uint)(IntPtr.Size * 2)),
                IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "nativeHandleListFailed");
            var startup = new StartupInfoEx {
                Startup = new StartupInfo {
                    Size = Marshal.SizeOf<StartupInfoEx>(),
                    Flags = StartfUseStdHandles,
                    StandardOutput = stdoutWrite,
                    StandardError = stderrWrite
                },
                AttributeList = attributes
            };
            var command = new StringBuilder(Quote(resolvedExecutable));
            foreach (var argument in arguments)
                command.Append(' ').Append(Quote(argument));
            stage = "create";
            if (fault == "createFault")
                throw new InvalidOperationException("validationCreateFault");
            if (!CreateProcessW(
                resolvedExecutable, command, IntPtr.Zero, IntPtr.Zero, true,
                CreateSuspended | CreateNoWindow | ExtendedStartupInfoPresent,
                IntPtr.Zero, null, ref startup, out information))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "nativeCreateFailed");
            processCreated = true;
            LastCleanupPid = checked((int)information.ProcessId);
            stage = "assign";
            if (fault == "assignFault")
                throw new InvalidOperationException("nativeAssignFault");
            if (!AssignProcessToJobObject(job, information.Process))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "nativeAssignFailed");
            assigned = true;
            stage = "managedHandoff";
            if (fault == "managedHandoffFault")
                throw new InvalidOperationException(
                    "validationManagedHandoffFault");

            managedProcess = System.Diagnostics.Process.GetProcessById(
                checked((int)information.ProcessId));
            if (fault == "processWrapperFault")
                throw new InvalidOperationException(
                    "validationProcessWrapperFault");

            managedStdoutHandle = new SafeFileHandle(stdoutRead, true);
            stdoutRead = IntPtr.Zero;
            if (fault == "stdoutSafeHandleFault")
                throw new InvalidOperationException(
                    "validationStdoutSafeHandleFault");
            managedStdoutStream = new FileStream(
                managedStdoutHandle, FileAccess.Read);
            managedStdoutHandle = null;
            if (fault == "stdoutStreamFault")
                throw new InvalidOperationException(
                    "validationStdoutStreamFault");

            managedStderrHandle = new SafeFileHandle(stderrRead, true);
            stderrRead = IntPtr.Zero;
            if (fault == "stderrSafeHandleFault")
                throw new InvalidOperationException(
                    "validationStderrSafeHandleFault");
            managedStderrStream = new FileStream(
                managedStderrHandle, FileAccess.Read);
            managedStderrHandle = null;
            if (fault == "stderrStreamFault")
                throw new InvalidOperationException(
                    "validationStderrStreamFault");

            if (fault == "stdoutWriteCloseFault")
                throw new InvalidOperationException(
                    "validationStdoutWriteCloseFault");
            CloseRawHandle(ref stdoutWrite, "nativeStdoutWriteCloseFailed");
            if (fault == "stderrWriteCloseFault")
                throw new InvalidOperationException(
                    "validationStderrWriteCloseFault");
            CloseRawHandle(ref stderrWrite, "nativeStderrWriteCloseFailed");

            stage = "resume";
            if (fault == "resumeFault")
                throw new InvalidOperationException("nativeResumeFault");
            if (ResumeThread(information.Thread) == uint.MaxValue)
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "nativeResumeFailed");

            stage = "managedHandoff";
            if (fault == "threadCloseFault")
                throw new InvalidOperationException(
                    "validationThreadCloseFault");
            CloseRawHandle(
                ref information.Thread, "nativeThreadCloseFailed");

            var value = new NativeJobProcess();
            value.job = job;
            value.processHandle = information.Process;
            value.Process = managedProcess;
            value.StandardOutput = managedStdoutStream;
            value.StandardError = managedStderrStream;
            managedProcess = null;
            managedStdoutStream = null;
            managedStderrStream = null;
            job = IntPtr.Zero;
            information.Process = IntPtr.Zero;
            LastCleanupProven = true;
            LastCleanupPid = 0;
            LastJobActiveProcesses = 0;
            return value;
        }
        catch (Exception failure)
        {
            if (!processCreated)
            {
                LastCleanupProven = true;
                LastCleanupPid = 0;
                LastJobActiveProcesses = 0;
            }
            else
            {
                if (assigned)
                    TerminateJobObject(job, 18);
                else
                    TerminateProcess(information.Process, 18);
                var signaled =
                    WaitForSingleObject(information.Process, 5000) == 0;
                var active = assigned ? ActiveCount(job) : 0;
                var empty = !assigned || active == 0;
                LastJobActiveProcesses = active;
                LastCleanupProven = signaled && empty;
                if (LastCleanupProven) LastCleanupPid = 0;
            }
            var code = failure is Win32Exception win32
                ? win32.NativeErrorCode : 20016;
            try { managedStdoutStream?.Dispose(); }
            catch { stage = "managedHandoff"; code = 20018; }
            managedStdoutStream = null;
            try { managedStderrStream?.Dispose(); }
            catch { stage = "managedHandoff"; code = 20018; }
            managedStderrStream = null;
            try { managedStdoutHandle?.Dispose(); }
            catch { stage = "managedHandoff"; code = 20018; }
            managedStdoutHandle = null;
            try { managedStderrHandle?.Dispose(); }
            catch { stage = "managedHandoff"; code = 20018; }
            managedStderrHandle = null;
            try { managedProcess?.Dispose(); }
            catch { stage = "managedHandoff"; code = 20018; }
            managedProcess = null;
            CloseRawAfterFailure(ref stdoutRead, ref stage, ref code);
            CloseRawAfterFailure(ref stdoutWrite, ref stage, ref code);
            CloseRawAfterFailure(ref stderrRead, ref stage, ref code);
            CloseRawAfterFailure(ref stderrWrite, ref stage, ref code);
            CloseRawAfterFailure(
                ref information.Thread, ref stage, ref code);
            throw new NativeStartException(
                stage, code, LastCleanupProven, LastCleanupPid,
                LastJobActiveProcesses);
        }
        finally
        {
            managedStdoutStream?.Dispose();
            managedStderrStream?.Dispose();
            managedStdoutHandle?.Dispose();
            managedStderrHandle?.Dispose();
            managedProcess?.Dispose();
            if (attributes != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }
            if (handleList != IntPtr.Zero) Marshal.FreeHGlobal(handleList);
            foreach (var handle in new[] {
                stdoutRead, stdoutWrite, stderrRead, stderrWrite,
                information.Process, information.Thread, job })
                if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }

    static uint ActiveCount(IntPtr job)
    {
        var size = Marshal.SizeOf<AccountingInfo>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(
                job, BasicAccounting, pointer, (uint)size, out _))
                return uint.MaxValue;
            return Marshal.PtrToStructure<AccountingInfo>(
                pointer).ActiveProcesses;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    public uint ActiveProcessCount() => ActiveCount(job);
    public bool Terminate() => TerminateJobObject(job, 18);
    public void Dispose()
    {
        StandardOutput?.Dispose();
        StandardError?.Dispose();
        Process?.Dispose();
        if (processHandle != IntPtr.Zero) CloseHandle(processHandle);
        if (job != IntPtr.Zero) CloseHandle(job);
        processHandle = job = IntPtr.Zero;
    }
}

sealed class NativeStartException : Exception
{
    public string Stage { get; }
    public int Code { get; }
    public bool CleanupProven { get; }
    public int Pid { get; }
    public uint JobActiveProcesses { get; }

    public NativeStartException(
        string stage, int code, bool cleanupProven, int pid,
        uint jobActiveProcesses) : base("nativeStartFailed")
    {
        Stage = stage;
        Code = code;
        CleanupProven = cleanupProven;
        Pid = pid;
        JobActiveProcesses = jobActiveProcesses;
    }
}

sealed class LigaseJob : IDisposable
{
    const uint KillOnClose = 0x00002000;
    const int ExtendedLimit = 9;
    const int BasicAccounting = 1;
    IntPtr handle;

    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimit
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
    struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimitInfo
    {
        public BasicLimit BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct BasicAccountingInfo
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
    static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(
        IntPtr job, int informationClass, IntPtr information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateJobObject(IntPtr job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool QueryInformationJobObject(
        IntPtr job, int informationClass, IntPtr information,
        uint length, out uint returned);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr value);

    public LigaseJob()
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("jobCreateFailed");
        var info = new ExtendedLimitInfo();
        info.BasicLimitInformation.LimitFlags = KillOnClose;
        var size = Marshal.SizeOf<ExtendedLimitInfo>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            if (!SetInformationJobObject(
                    handle, ExtendedLimit, pointer, (uint)size))
            {
                CloseHandle(handle);
                handle = IntPtr.Zero;
                throw new InvalidOperationException("jobLimitFailed");
            }
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    public bool Assign(System.Diagnostics.Process process) =>
        AssignProcessToJobObject(handle, process.Handle);
    public bool Terminate() => TerminateJobObject(handle, 18);
    public uint ActiveProcessCount()
    {
        var size = Marshal.SizeOf<BasicAccountingInfo>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(
                    handle, BasicAccounting, pointer, (uint)size, out _))
                return uint.MaxValue;
            return Marshal.PtrToStructure<BasicAccountingInfo>(
                pointer).ActiveProcesses;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    public void Dispose()
    {
        if (handle != IntPtr.Zero)
        {
            CloseHandle(handle);
            handle = IntPtr.Zero;
        }
    }
}
'@ | Set-Content -LiteralPath (
  Join-Path $argumentListRunnerRoot "Program.cs") -Encoding UTF8
& $DotNet build (
  Join-Path $argumentListRunnerRoot "ArgumentListRunner.csproj") `
  -c Release -o $argumentListRunnerOutput --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "argumentListRunnerBuildFailed" }
$argumentListRunner = Join-Path $argumentListRunnerOutput "ArgumentListRunner.dll"
$argumentListRunnerSourceSha = Get-CompatibleSha256 (
  [IO.File]::ReadAllBytes((Join-Path $argumentListRunnerRoot "Program.cs")))
$argumentListRunnerBinarySha = Get-CompatibleSha256 (
  [IO.File]::ReadAllBytes($argumentListRunner))
$emitterSelfTests = @(
  @("--emit", "unknown"),
  @("--emit", "hang", "extra"),
  @("--emit"))
foreach ($emitterArgs in $emitterSelfTests) {
  $savedEmitterErrorAction = $ErrorActionPreference
  try {
    $ErrorActionPreference = "Continue"
    $emitterOutput = @(& $DotNet $argumentListRunner @emitterArgs)
    $emitterExit = $LASTEXITCODE
  } finally {
    $ErrorActionPreference = $savedEmitterErrorAction
  }
  if ($emitterExit -ne 92 -or $emitterOutput.Count -ne 0) {
    throw "byteEmitterArgumentValidationFailed"
  }
}
$boundedHostCases = @()
$boundedHostEvidenceRoot = Join-Path $OutputRoot "bounded-host-evidence"
New-Item -ItemType Directory -Path $boundedHostEvidenceRoot | Out-Null
function Test-BoundedHostCaseEvidence([string]$Raw) {
  $expected = @(
    "schema", "caseId", "nativeExit", "result", "stage",
    "startStage", "startCode",
    "stdoutRawLength", "stdoutRawSha", "stdoutOverflow",
    "stdoutDecoderState", "stdoutPendingTailLength",
    "stderrRawLength", "stderrRawSha", "stderrOverflow",
    "stderrDecoderState", "stderrPendingTailLength",
    "timedOut", "cleanupState", "rootPidZero", "descendantPidZero",
    "jobActiveProcesses", "elapsedMilliseconds", "hardCapMilliseconds",
    "environmentRestored")
  $names = @([regex]::Matches(
    $Raw, '"(?<name>[A-Za-z][A-Za-z0-9]*)"\s*:') |
    ForEach-Object { $_.Groups["name"].Value })
  if ($names.Count -ne $expected.Count -or
      @($names | Sort-Object -Unique).Count -ne $expected.Count -or
      @($names | Where-Object { $expected -cnotcontains $_ }).Count -ne 0) {
    return $false
  }
  try { $value = $Raw | ConvertFrom-Json } catch { return $false }
  return [string]$value.schema -ceq "boundedHostCaseEvidenceV1" -and
    [string]$value.caseId -cmatch "^[A-Za-z][A-Za-z0-9]{0,63}$" -and
    [int]$value.nativeExit -in @(0, 93, 95) -and
    [string]$value.result -cin @("observed") -and
    [string]$value.stage -cin @("boundedHostProcess") -and
    [string]$value.startStage -cin @(
      "none","executableResolve","pipe","job","attribute","create","assign","resume",
      "managedHandoff") -and
    [int]$value.startCode -ge 0 -and
    [int64]$value.stdoutRawLength -ge 0 -and
    [int64]$value.stderrRawLength -ge 0 -and
    [string]$value.stdoutRawSha -cmatch "^[A-F0-9]{64}$" -and
    [string]$value.stderrRawSha -cmatch "^[A-F0-9]{64}$" -and
    [string]$value.stdoutDecoderState -cin @("closed","pendingTail","invalid") -and
    [string]$value.stderrDecoderState -cin @("closed","pendingTail","invalid") -and
    [int]$value.stdoutPendingTailLength -ge 0 -and
    [int]$value.stdoutPendingTailLength -le 3 -and
    [int]$value.stderrPendingTailLength -ge 0 -and
    [int]$value.stderrPendingTailLength -le 3 -and
    [string]$value.cleanupState -cin @("completed","failed") -and
    [uint64]$value.jobActiveProcesses -le [uint32]::MaxValue -and
    [int64]$value.elapsedMilliseconds -ge 0 -and
    [int64]$value.hardCapMilliseconds -ge 2
}
function Write-BoundedHostCaseEvidence(
  [Parameter(Mandatory)][System.Collections.IDictionary]$Case
) {
  $path = Join-Path $boundedHostEvidenceRoot (
    ([string]$Case.name) + ".first.json")
  if (Test-Path -LiteralPath $path) { throw "gateEvidenceUnavailable" }
  $observation = [ordered]@{
    schema = "boundedHostCaseEvidenceV1"
    caseId = [string]$Case.name
    nativeExit = [int]$Case.runnerExit
    result = "observed"
    stage = "boundedHostProcess"
    startStage = [string]$Case.startStage
    startCode = [int]$Case.startCode
    stdoutRawLength = [int64]$Case.stdoutRawLength
    stdoutRawSha = [string]$Case.stdoutRawSha
    stdoutOverflow = [bool]$Case.stdoutOverflow
    stdoutDecoderState = [string]$Case.stdoutDecoderState
    stdoutPendingTailLength = [int]$Case.stdoutPendingTailLength
    stderrRawLength = [int64]$Case.stderrRawLength
    stderrRawSha = [string]$Case.stderrRawSha
    stderrOverflow = [bool]$Case.stderrOverflow
    stderrDecoderState = [string]$Case.stderrDecoderState
    stderrPendingTailLength = [int]$Case.stderrPendingTailLength
    timedOut = [bool]$Case.timedOut
    cleanupState = if ($Case.cleanupCompleted) { "completed" } else { "failed" }
    rootPidZero = [bool]($Case.pid -eq 0)
    descendantPidZero = [bool]($Case.pid -eq 0 -and $Case.jobActiveProcesses -eq 0)
    jobActiveProcesses = [uint64]$Case.jobActiveProcesses
    elapsedMilliseconds = [int64]$Case.elapsedMilliseconds
    hardCapMilliseconds = [int64]$Case.hardCapMilliseconds
    environmentRestored = [bool]$Case.environmentRestored
  }
  $raw = $observation | ConvertTo-Json -Compress
  if (-not (Test-BoundedHostCaseEvidence $raw)) {
    throw "gateEvidenceUnavailable"
  }
  $temp = Join-Path $boundedHostEvidenceRoot (
    "." + [guid]::NewGuid().ToString("N") + ".tmp")
  try {
    $bytes = [Text.UTF8Encoding]::new($false, $true).GetBytes($raw)
    $stream = [IO.FileStream]::new(
      $temp, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
      [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
    try {
      $stream.Write($bytes, 0, $bytes.Length)
      $stream.Flush($true)
    } finally { $stream.Dispose() }
    [IO.File]::Move($temp, $path)
    $readback = [IO.File]::ReadAllBytes($path)
    $readbackRaw = [Text.UTF8Encoding]::new(
      $false, $true).GetString($readback)
    if (-not (Test-BoundedHostCaseEvidence $readbackRaw) -or
        -not [Linq.Enumerable]::SequenceEqual(
          [byte[]]$bytes, [byte[]]$readback)) {
      throw "gateEvidenceUnavailable"
    }
    return Get-CompatibleSha256 $readback
  } catch {
    if (Test-Path -LiteralPath $temp) {
      Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
    }
    throw "gateEvidenceUnavailable"
  }
}
function Throw-BoundedHostCaseFailure(
  [System.Collections.IDictionary]$Case, [string]$Reason) {
  throw "$Reason caseId=$($Case.name) evidenceSha=$($Case.evidenceSha)"
}
function Invoke-BoundedHostFixture(
  [Parameter(Mandatory)][string]$Name,
  [ValidateSet(
    "asciiOverflowStdout", "asciiOverflowStderr",
    "utf8BoundaryStdout", "utf8BoundaryStderr",
    "internalInvalidStdout", "internalInvalidStderr",
    "invalidUtf8Stdout", "invalidUtf8Stderr",
    "incompleteUtf8Stdout", "incompleteUtf8Stderr",
    "dualOverflow", "jsonExtra", "jsonMalformed", "hang",
    "treeInheritedPipe", "exitZero")]
  [string]$Mode = "exitZero",
  [int]$TimeoutMilliseconds = 2000,
  [int]$CleanupReserveMilliseconds = 2000,
  [int]$MaxBytes = 512,
  [ValidateSet(
    "runnerHost", "powershellCaseDrift", "powershellAbsolute",
    "unknownName")]
  [string]$ExecutableIdentity = "runnerHost",
  [ValidateSet(
    "none", "accountingFault", "executableResolveFault", "pipeFault", "jobFault",
    "attributeFault", "createFault", "assignFault", "resumeFault",
    "managedHandoffFault", "processWrapperFault",
    "stdoutSafeHandleFault", "stdoutStreamFault",
    "stderrSafeHandleFault", "stderrStreamFault",
    "stdoutWriteCloseFault", "stderrWriteCloseFault", "threadCloseFault")]
  [string]$Fault = "none"
) {
  $saved = $ErrorActionPreference
  try {
    $ErrorActionPreference = "Continue"
    $fixtureExecutable = switch -CaseSensitive ($ExecutableIdentity) {
      "runnerHost" { $DotNet }
      "powershellCaseDrift" { "POWERSHELL.EXE" }
      "powershellAbsolute" {
        Join-Path ([Environment]::SystemDirectory) (
          "WindowsPowerShell\v1.0\powershell.exe")
      }
      "unknownName" { "cmd.exe" }
    }
    $fixtureArguments = if ($ExecutableIdentity -ceq "runnerHost") {
      @($argumentListRunner, "--emit", $Mode)
    } else { @("--closed-validation-argument") }
    $output = @(
      & $DotNet $argumentListRunner --bounded-capture `
        $TimeoutMilliseconds $CleanupReserveMilliseconds $MaxBytes $Fault `
        $fixtureExecutable @fixtureArguments)
    $exit = $LASTEXITCODE
  } finally {
    $ErrorActionPreference = $saved
  }
  if ($output.Count -ne 1) {
    throw "boundedHostFixtureEnvelopeInvalid:$Name"
  }
  $envelopeRaw = [string]$output[0]
  $envelopeExpectedNames = @(
    "schema","startStage","startCode","exitCode","timedOut","overflow",
    "pipeFault","killAttempted","cleanupCompleted","jobEmpty",
    "jobActiveProcesses","pid","elapsedMilliseconds","hardCapMilliseconds",
    "stdoutOverflow","stderrOverflow","stdoutRawLength","stderrRawLength",
    "stdoutRawSha","stderrRawSha","stdoutDecoderState","stderrDecoderState",
    "stdoutPendingTailLength","stderrPendingTailLength","stdoutText",
    "stderrText")
  $envelopeNames = @([regex]::Matches(
    $envelopeRaw, '"(?<name>[A-Za-z][A-Za-z0-9]*)"\s*:') |
    ForEach-Object { $_.Groups["name"].Value })
  if ($envelopeNames.Count -ne $envelopeExpectedNames.Count -or
      @($envelopeNames | Sort-Object -Unique).Count -ne
        $envelopeExpectedNames.Count -or
      @($envelopeNames | Where-Object {
        $envelopeExpectedNames -cnotcontains $_
      }).Count -ne 0) {
    throw "boundedHostFixtureEnvelopeInvalid:$Name"
  }
  try {
    $envelope = $envelopeRaw | ConvertFrom-Json
  } catch {
    throw "boundedHostFixtureEnvelopeInvalid:$Name"
  }
  if ([string]$envelope.schema -cne "boundedProcessV1" -or
      [string]$envelope.startStage -cnotin @(
        "none","executableResolve","pipe","job","attribute","create","assign","resume",
        "managedHandoff") -or
      $envelope.startCode.GetType() -ne [int] -or
      [int]$envelope.startCode -lt 0) {
    throw "boundedHostFixtureEnvelopeInvalid:$Name"
  }
  $case = [ordered]@{
    name = $Name
    runnerExit = $exit
    startStage = [string]$envelope.startStage
    startCode = [int]$envelope.startCode
    timedOut = [bool]$envelope.timedOut
    overflow = [bool]$envelope.overflow
    cleanupCompleted = [bool]$envelope.cleanupCompleted
    jobEmpty = [bool]$envelope.jobEmpty
    pid = [int]$envelope.pid
    jobActiveProcesses = [uint64]$envelope.jobActiveProcesses
    elapsedMilliseconds = [int64]$envelope.elapsedMilliseconds
    hardCapMilliseconds = [int64]$envelope.hardCapMilliseconds
    pipeFault = [bool]$envelope.pipeFault
    stdoutOverflow = [bool]$envelope.stdoutOverflow
    stderrOverflow = [bool]$envelope.stderrOverflow
    stdoutRawLength = [int64]$envelope.stdoutRawLength
    stderrRawLength = [int64]$envelope.stderrRawLength
    stdoutRawSha = [string]$envelope.stdoutRawSha
    stderrRawSha = [string]$envelope.stderrRawSha
    stdoutDecoderState = [string]$envelope.stdoutDecoderState
    stderrDecoderState = [string]$envelope.stderrDecoderState
    stdoutPendingTailLength = [int]$envelope.stdoutPendingTailLength
    stderrPendingTailLength = [int]$envelope.stderrPendingTailLength
    stdout = [string]$envelope.stdoutText
    stderr = [string]$envelope.stderrText
    rejected = $false
    environmentRestored = [bool]($ErrorActionPreference -ceq $saved)
  }
  $case["evidenceSha"] = Write-BoundedHostCaseEvidence -Case $case
  return $case
}

$hostHang = Invoke-BoundedHostFixture -Name "hostHang" `
  -TimeoutMilliseconds 500 `
  -Mode "hang"
if ($hostHang.runnerExit -ne 0 -or -not $hostHang.timedOut -or
    -not $hostHang.cleanupCompleted -or -not $hostHang.jobEmpty -or
    $hostHang.pid -ne 0 -or $hostHang.elapsedMilliseconds -gt 2500) {
  Throw-BoundedHostCaseFailure $hostHang "boundedHostHangFixtureFailed"
}
$boundedHostCases += $hostHang

$hostEarlyChild = Invoke-BoundedHostFixture -Name "hostEarlyChild" `
  -TimeoutMilliseconds 500 -Mode "treeInheritedPipe"
if ($hostEarlyChild.runnerExit -ne 0 -or
    -not $hostEarlyChild.timedOut -or
    -not $hostEarlyChild.cleanupCompleted -or
    -not $hostEarlyChild.jobEmpty -or
    $hostEarlyChild.jobActiveProcesses -ne 0 -or
    $hostEarlyChild.pid -ne 0 -or
    $hostEarlyChild.stdoutRawLength -ne 0 -or
    -not [string]::IsNullOrEmpty($hostEarlyChild.stdout) -or
    $hostEarlyChild.elapsedMilliseconds -gt 2500) {
  Throw-BoundedHostCaseFailure $hostEarlyChild `
    "boundedHostEarlyChildFixtureFailed"
}
$boundedHostCases += $hostEarlyChild

$hostOverflow = Invoke-BoundedHostFixture -Name "hostOverflow" `
  -TimeoutMilliseconds 2000 -MaxBytes 512 `
  -Mode "asciiOverflowStdout"
if ($hostOverflow.runnerExit -ne 0 -or -not $hostOverflow.overflow -or
    -not $hostOverflow.cleanupCompleted -or -not $hostOverflow.jobEmpty -or
    $hostOverflow.pid -ne 0 -or $hostOverflow.elapsedMilliseconds -gt 4000 -or
    $hostOverflow.stdoutRawLength -ne 4096 -or
    $hostOverflow.stdoutRawSha -cne
      "A2E659DACB4691E887AC0139F8893D04764EE197D70FB73D3190D56113D18E3E" -or
    $hostOverflow.stdoutDecoderState -cne "closed" -or
    [Text.Encoding]::UTF8.GetByteCount($hostOverflow.stdout) -ne 512) {
  Throw-BoundedHostCaseFailure $hostOverflow "boundedHostOverflowFixtureFailed"
}
$boundedHostCases += $hostOverflow

$hostUtf8Boundary = Invoke-BoundedHostFixture -Name "hostUtf8Boundary" `
  -TimeoutMilliseconds 2000 -MaxBytes 512 `
  -Mode "utf8BoundaryStdout"
if ($hostUtf8Boundary.runnerExit -ne 0 -or
    -not $hostUtf8Boundary.overflow -or $hostUtf8Boundary.pipeFault -or
    $hostUtf8Boundary.stdoutDecoderState -cne "pendingTail" -or
    $hostUtf8Boundary.stdoutPendingTailLength -ne 1 -or
    [Text.Encoding]::UTF8.GetByteCount($hostUtf8Boundary.stdout) -ne 511 -or
    $hostUtf8Boundary.stdoutRawLength -ne 513 -or
    $hostUtf8Boundary.stdoutRawSha -cne
      "702817C4CEAADAB11365B98FD054EE71190C8F0D1F4DC6FAB047A357BEF1A828" -or
    -not $hostUtf8Boundary.cleanupCompleted -or
    $hostUtf8Boundary.pid -ne 0 -or
    $hostUtf8Boundary.elapsedMilliseconds -gt 4000) {
  Throw-BoundedHostCaseFailure $hostUtf8Boundary "boundedHostUtf8BoundaryFixtureFailed"
}
$boundedHostCases += $hostUtf8Boundary

$hostInvalidOverflow = Invoke-BoundedHostFixture -Name "hostInvalidOverflow" `
  -TimeoutMilliseconds 2000 -MaxBytes 512 `
  -Mode "internalInvalidStdout"
if ($hostInvalidOverflow.runnerExit -ne 0 -or
    -not $hostInvalidOverflow.overflow -or
    -not $hostInvalidOverflow.pipeFault -or
    $hostInvalidOverflow.stdoutRawLength -ne 600 -or
    $hostInvalidOverflow.stdoutRawSha -cne
      "FF0B0CB6C5A35AC9D0B59A718D0979ABAF3FE6612EB178C918A37AFCCDA4BBD1" -or
    $hostInvalidOverflow.stdoutDecoderState -cne "invalid" -or
    -not $hostInvalidOverflow.cleanupCompleted -or
    $hostInvalidOverflow.pid -ne 0 -or
    $hostInvalidOverflow.elapsedMilliseconds -gt 4000) {
  Throw-BoundedHostCaseFailure $hostInvalidOverflow "boundedHostInvalidOverflowFixtureFailed"
}
$boundedHostCases += $hostInvalidOverflow

$hostInvalidUtf8 = Invoke-BoundedHostFixture -Name "hostInvalidUtf8" `
  -Mode "invalidUtf8Stdout"
if ($hostInvalidUtf8.runnerExit -ne 0 -or $hostInvalidUtf8.overflow -or
    -not $hostInvalidUtf8.pipeFault -or
    $hostInvalidUtf8.stdoutDecoderState -cne "invalid" -or
    -not $hostInvalidUtf8.cleanupCompleted -or $hostInvalidUtf8.pid -ne 0) {
  Throw-BoundedHostCaseFailure $hostInvalidUtf8 "boundedHostInvalidUtf8FixtureFailed"
}
$boundedHostCases += $hostInvalidUtf8

$hostIncompleteUtf8 = Invoke-BoundedHostFixture -Name "hostIncompleteUtf8" `
  -Mode "incompleteUtf8Stdout"
if ($hostIncompleteUtf8.runnerExit -ne 0 -or $hostIncompleteUtf8.overflow -or
    -not $hostIncompleteUtf8.pipeFault -or
    $hostIncompleteUtf8.stdoutDecoderState -cne "invalid" -or
    -not $hostIncompleteUtf8.cleanupCompleted -or
    $hostIncompleteUtf8.pid -ne 0) {
  Throw-BoundedHostCaseFailure $hostIncompleteUtf8 "boundedHostIncompleteUtf8FixtureFailed"
}
$boundedHostCases += $hostIncompleteUtf8

$hostDualOverflow = Invoke-BoundedHostFixture -Name "hostDualOverflow" `
  -TimeoutMilliseconds 2000 -MaxBytes 512 `
  -Mode "dualOverflow"
if ($hostDualOverflow.runnerExit -ne 0 -or
    -not $hostDualOverflow.overflow -or $hostDualOverflow.pipeFault -or
    $hostDualOverflow.stdoutRawLength -lt 512 -or
    $hostDualOverflow.stderrRawLength -lt 512 -or
    $hostDualOverflow.stdoutDecoderState -cne "closed" -or
    $hostDualOverflow.stderrDecoderState -cne "closed" -or
    -not $hostDualOverflow.cleanupCompleted -or
    $hostDualOverflow.pid -ne 0 -or
    $hostDualOverflow.elapsedMilliseconds -gt 4000) {
  Throw-BoundedHostCaseFailure $hostDualOverflow "boundedHostDualOverflowFixtureFailed"
}
$boundedHostCases += $hostDualOverflow

$hostNoRecord = Invoke-BoundedHostFixture -Name "hostNoRecord" `
  -Mode "exitZero"
if ($hostNoRecord.runnerExit -ne 0 -or
    -not $hostNoRecord.cleanupCompleted -or
    -not $hostNoRecord.jobEmpty -or
    $hostNoRecord.jobActiveProcesses -ne 0 -or
    $hostNoRecord.pid -ne 0 -or
    $hostNoRecord.stdoutRawLength -ne 0 -or
    -not [string]::IsNullOrEmpty($hostNoRecord.stdout)) {
  Throw-BoundedHostCaseFailure $hostNoRecord "boundedHostNoRecordFixtureFailed"
}
$hostNoRecord["rejected"] = $true
$boundedHostCases += $hostNoRecord

$hostExtra = Invoke-BoundedHostFixture -Name "hostExtraJson" `
  -Mode "jsonExtra"
$extraRecords = @($hostExtra.stdout -split "\r?\n" | Where-Object {
  -not [string]::IsNullOrWhiteSpace($_)
})
if ($hostExtra.runnerExit -ne 0 -or
    -not $hostExtra.cleanupCompleted -or $hostExtra.pid -ne 0 -or
    $hostExtra.elapsedMilliseconds -gt 4000 -or
    $extraRecords.Count -ne 2) {
  Throw-BoundedHostCaseFailure $hostExtra "boundedHostExtraJsonFixtureFailed"
}
$hostExtra["rejected"] = $true
$boundedHostCases += $hostExtra

$hostMalformed = Invoke-BoundedHostFixture -Name "hostMalformedJson" `
  -Mode "jsonMalformed"
$malformedRejected = $false
try {
  $null = $hostMalformed.stdout.Trim() | ConvertFrom-Json
} catch {
  $malformedRejected = $true
}
if ($hostMalformed.runnerExit -ne 0 -or
    -not $hostMalformed.cleanupCompleted -or $hostMalformed.pid -ne 0 -or
    $hostMalformed.elapsedMilliseconds -gt 4000 -or
    -not $malformedRejected) {
  Throw-BoundedHostCaseFailure $hostMalformed "boundedHostMalformedJsonFixtureFailed"
}
$hostMalformed["rejected"] = $true
$boundedHostCases += $hostMalformed

$hostCleanupFault = Invoke-BoundedHostFixture -Name "hostCleanupFault" `
  -Fault "accountingFault" -Mode "exitZero"
if ($hostCleanupFault.runnerExit -ne 93 -or
    $hostCleanupFault.cleanupCompleted -or $hostCleanupFault.jobEmpty -or
    $hostCleanupFault.pid -ne 0 -or
    $hostCleanupFault.elapsedMilliseconds -gt 4000) {
  Throw-BoundedHostCaseFailure $hostCleanupFault "boundedHostCleanupFaultFixtureFailed"
}
$boundedHostCases += $hostCleanupFault

foreach ($startFault in @(
  "executableResolveFault", "pipeFault", "jobFault", "attributeFault", "createFault",
  "assignFault", "resumeFault", "managedHandoffFault",
  "processWrapperFault", "stdoutSafeHandleFault", "stdoutStreamFault",
  "stderrSafeHandleFault", "stderrStreamFault", "stdoutWriteCloseFault",
  "stderrWriteCloseFault", "threadCloseFault")) {
  $hostStartFault = Invoke-BoundedHostFixture `
    -Name ("host" + $startFault) -Fault $startFault -Mode "exitZero"
  if ($hostStartFault.runnerExit -ne 95 -or
      -not $hostStartFault.cleanupCompleted -or
      -not $hostStartFault.jobEmpty -or
      $hostStartFault.jobActiveProcesses -ne 0 -or
      $hostStartFault.pid -ne 0 -or
      $hostStartFault.startStage -cne $(if ($startFault -cin @(
        "managedHandoffFault", "processWrapperFault",
        "stdoutSafeHandleFault", "stdoutStreamFault",
        "stderrSafeHandleFault", "stderrStreamFault",
        "stdoutWriteCloseFault", "stderrWriteCloseFault",
        "threadCloseFault")) { "managedHandoff" } else {
          $startFault.Substring(0, $startFault.Length - "Fault".Length)
        }) -or
      $hostStartFault.startCode -ne 20016 -or
      $hostStartFault.elapsedMilliseconds -gt 4000) {
    Throw-BoundedHostCaseFailure $hostStartFault `
      "boundedHostStartFaultFixtureFailed"
  }
  $boundedHostCases += $hostStartFault
}
foreach ($identityCase in @(
  "powershellCaseDrift", "powershellAbsolute", "unknownName")) {
  $hostIdentityFailure = Invoke-BoundedHostFixture `
    -Name ("host" + $identityCase) -ExecutableIdentity $identityCase
  if ($hostIdentityFailure.runnerExit -ne 95 -or
      $hostIdentityFailure.startStage -cne "executableResolve" -or
      $hostIdentityFailure.startCode -ne 20016 -or
      -not $hostIdentityFailure.cleanupCompleted -or
      -not $hostIdentityFailure.jobEmpty -or
      $hostIdentityFailure.jobActiveProcesses -ne 0 -or
      $hostIdentityFailure.pid -ne 0) {
    Throw-BoundedHostCaseFailure $hostIdentityFailure `
      "boundedHostExecutableIdentityFixtureFailed"
  }
  $boundedHostCases += $hostIdentityFailure
}

$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
$managementScript = Join-Path $PSScriptRoot "Manage-LigaseInstallation.ps1"
$installerScript = Join-Path $PSScriptRoot "LigaseHost.nsi"
$managementSource = [IO.File]::ReadAllText($managementScript)
$installerSource = [IO.File]::ReadAllText($installerScript)
$installCalls = [regex]::Matches(
  $installerSource,
  '-Action Install -InstallDirectory .*?-ConfigureFirewall \$3 \$4')
if ($installCalls.Count -ne 3 -or
    [regex]::Matches(
      $installerSource,
      'SectionGetFlags \$\{LIGASE_SECTION_VIRTUAL_DISPLAY\} \$2').Count -ne 2) {
  throw "virtualDisplaySelectionTransactionIdentityNotFrozen"
}
$virtualReadbackIndex = $managementSource.IndexOf(
  '$displayReadback = Get-VirtualDisplay', [StringComparison]::Ordinal)
$virtualMarkerIndex = $managementSource.IndexOf(
  'Write-VirtualDisplayOwnershipMarkerAtomic $ownershipPath',
  $virtualReadbackIndex, [StringComparison]::Ordinal)
if ($virtualReadbackIndex -lt 0 -or
    $virtualMarkerIndex -le $virtualReadbackIndex -or
    $managementSource.IndexOf(
      '"virtualDisplayReadbackFailed"', [StringComparison]::Ordinal) -lt 0 -or
    $managementSource.IndexOf(
      '"virtualDisplayRollbackFailed"', [StringComparison]::Ordinal) -lt 0 -or
    $managementSource.IndexOf(
      '"virtualDisplayMarkerCommitFailed"', [StringComparison]::Ordinal) -lt 0) {
  throw "virtualDisplaySuccessWithoutDeviceDriverReadback"
}
foreach ($token in @(
    'Invoke-VirtualDisplayInstaller',
    'ValidateVirtualDisplayInstallerProcess',
    'jobProcess.StandardOutput.ReadAsync',
    'jobProcess.StandardError.ReadAsync',
    '[byte[]]::new(512)',
    '[Text.UTF8Encoding]::new($false, $true)',
    'CREATE_SUSPENDED',
    'STARTUPINFOEX',
    'PROC_THREAD_ATTRIBUTE_HANDLE_LIST',
    'InitializeProcThreadAttributeList',
    'UpdateProcThreadAttribute',
    'DeleteProcThreadAttributeList',
    'AssignProcessToJobObject',
    'ResumeThread',
    'JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE',
    'TerminateJobObject',
    'HasNoActiveProcesses',
    'SecondaryContainment',
    'virtualDisplayInstallerToolUnavailable',
    'virtualDisplayCertificateRootFailed',
    'virtualDisplayCertificatePublisherFailed',
    'virtualDisplayDeviceRemoveFailed',
    'virtualDisplayDeviceCreateFailed',
    'virtualDisplayDriverPackageInstallFailed',
    'virtualDisplayInstallerTimeout',
    'virtualDisplayInstallerOutputOverflow',
    'installStage = [string]$script:virtualDisplayInstallStage',
    'stdoutSha256 = [string]$script:virtualDisplayStdoutSha256')) {
  if ($managementSource.IndexOf($token, [StringComparison]::Ordinal) -lt 0) {
    throw "virtualDisplayClosedInstallDiagnosticMissing:$token"
  }
}
$boundedInstallerStart = $managementSource.IndexOf(
  'function Invoke-VirtualDisplayInstaller(',
  [StringComparison]::Ordinal)
$boundedInstallerEnd = $managementSource.IndexOf(
  'function Assert-VirtualDisplayInstallerTuple',
  $boundedInstallerStart, [StringComparison]::Ordinal)
if ($boundedInstallerStart -lt 0 -or
    $boundedInstallerEnd -le $boundedInstallerStart) {
  throw "virtualDisplayInstallerBoundedScopeMissing"
}
$boundedInstallerSource = $managementSource.Substring(
  $boundedInstallerStart, $boundedInstallerEnd - $boundedInstallerStart)
if ($boundedInstallerSource.IndexOf(
    'StandardOutput.ReadToEndAsync', [StringComparison]::Ordinal) -ge 0 -or
    $boundedInstallerSource.IndexOf(
    'StandardError.ReadToEndAsync', [StringComparison]::Ordinal) -ge 0) {
  throw "virtualDisplayInstallerUnboundedReadPresent"
}
$driverInstallerSource = [IO.File]::ReadAllText((Join-Path $sourceRoot (
  "src_assets/windows/drivers/sudovda/install.bat")))
$driverInfSource = [IO.File]::ReadAllText((Join-Path $sourceRoot (
  "src_assets/windows/drivers/sudovda/SudoVDA.inf")))
foreach ($token in @(
    '[SourceDisksFiles]', 'SudoVDA.dll=1', 'CopyFiles=UMDriverCopy',
    'ServiceBinary=%12%\UMDF\SudoVDA.dll',
    'DriverVer = 07/14/2025,1.10.9.289')) {
  if ($driverInfSource.IndexOf(
      $token, [StringComparison]::Ordinal) -lt 0) {
    throw "virtualDisplayDriverInfClosureMissing:$token"
  }
}
foreach ($token in @(
    'if not exist "%NEFCON%"',
    'stage=certificateRoot', 'stage=certificatePublisher',
    'stage=deviceCreate', 'stage=driverPackageInstall', 'stage=completed',
    'exit /b 20', 'exit /b 21', 'exit /b 22', 'exit /b 23',
    'exit /b 24', 'popd', 'exit /b 0')) {
  if ($driverInstallerSource.IndexOf(
      $token, [StringComparison]::Ordinal) -lt 0) {
    throw "virtualDisplayInstallerStepContractMissing:$token"
  }
}
$buildSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot (
  "Build-LigaseInstaller.ps1")))
foreach ($token in @(
    '[string]$NefconExecutable', '586152',
    '19A113297EAFEFD796AA91C1A64D199628D9C58DC53928899D2E5D6A68074EFE',
    '1F431092EC96A80B41AB5317F53AC02EA6F9B89B',
    '[string]$SudoVdaDriverBinary', '[switch]$ValidateExternalInputsOnly',
    '83216', '47EE263CB5DE9382C6630A2D7F3DAFEC4A49419F953BEEC869CA5DD0C460FF63',
    '3C918FC73525AD8B1521B6DB26B71F694277CC49', '0x8664',
    'virtualDisplayDriverBinaryUnavailable',
    'virtualDisplayDriverBinarySizeMismatch',
    'virtualDisplayDriverBinaryArchitectureMismatch',
    'virtualDisplayDriverBinaryHashMismatch',
    'virtualDisplayDriverBinarySignatureInvalid',
    'virtualDisplayDriverVersionMismatch',
    'virtualDisplayDriverInfClosureInvalid',
    'driverBinarySha256', 'driverBinaryArchitecture', 'driverVersion',
    'installerToolSha256', 'Deployment/Drivers/sudovda/nefconc.exe',
    'Deployment/Drivers/sudovda/SudoVDA.dll')) {
  if ($buildSource.IndexOf($token, [StringComparison]::Ordinal) -lt 0) {
    throw "virtualDisplayInstallerBuildInputGateMissing:$token"
  }
}
$markerTransactionTokens = @(
  '[IO.File]::Replace($temp, $Path, $null, $true)',
  '[IO.File]::Move($temp, $Path)', '$stream.Flush($true)',
  'Test-ExactBytes $Bytes ([IO.File]::ReadAllBytes($Path))',
  '$ownershipPath $ownershipBytes $false',
  '".ligase-driver-ownership-*.tmp"',
  '"createTemp"', '"writeTemp"', '"tempReadback"', '"atomicReplace"',
  '"finalReadback"')
foreach ($token in $markerTransactionTokens) {
  if ($managementSource.IndexOf(
      $token, [StringComparison]::Ordinal) -lt 0) {
    throw "virtualDisplayMarkerTransactionTokenMissing:$token"
  }
}
$transactionReadbackTokens = @(
  '"rawRead"', '"rawShape"', '"jsonParse"', '"schemaValidation"',
  '"freshnessValidation"', '"identityValidation"', '"shortcutValidation"',
  '"cleanup"', '"missing"', '"duplicateProperty"', '"malformedJson"',
  '"missingProperty"', '"unknownProperty"', '"wrongType"', '"stale"',
  '"helperFailure"', '"identityMismatch"',
  '"shortcutSnapshotInvalid"', '"cleanupFailed"')
foreach ($token in $transactionReadbackTokens) {
  if ($managementSource.IndexOf(
      $token, [StringComparison]::Ordinal) -lt 0) {
    throw "transactionReadbackDiagnosticTokenMissing:$token"
  }
}
$sourceContractResults = @(
  [ordered]@{
    name = "virtual-display-selection-transaction-identity"
    passed = $true
  },
  [ordered]@{
    name = "virtual-display-device-driver-readback-before-marker"
    passed = $true
  },
  [ordered]@{
    name = "virtual-display-marker-atomic-commit-compensation"
    passed = $true
  },
  [ordered]@{
    name = "transaction-readback-closed-stage-reason"
    passed = $true
  })

function Invoke-FinalizationStackFixture(
  [string]$Mode,
  [string]$ExpectedVerdict,
  [int]$ExpectedHelperExit,
  [int]$ExpectedNativeExit
) {
  $resultFile = Join-Path $root "finalization-$Mode.result"
  & $DotNet $argumentListRunner $harness "/ResultFile=$resultFile" `
    "/FinalizationMode=$Mode"
  $nativeExit = $LASTEXITCODE
  if ($nativeExit -ne $ExpectedNativeExit) {
    throw "finalizationNativeExitMismatch:$Mode"
  }
  $lines = @(Get-Content -LiteralPath $resultFile -Encoding Unicode)
  if ($lines.Count -lt 3 -or
      $lines[0] -cne $ExpectedVerdict -or
      [int]$lines[1] -ne $ExpectedHelperExit) {
    throw "finalizationStackProjectionMismatch:$Mode"
  }
  [ordered]@{
    name = "finalization-$Mode"
    passed = $true
    verdict = [string]$lines[0]
    helperExit = [int]$lines[1]
  }
}

$finalizationStackResults = @(
  Invoke-FinalizationStackFixture `
    -Mode "success" -ExpectedVerdict "passed" `
    -ExpectedHelperExit 0 -ExpectedNativeExit 0
  Invoke-FinalizationStackFixture `
    -Mode "extra" -ExpectedVerdict "failed" `
    -ExpectedHelperExit 0 -ExpectedNativeExit 10
  Invoke-FinalizationStackFixture `
    -Mode "malformed" -ExpectedVerdict "failed" `
    -ExpectedHelperExit 0 -ExpectedNativeExit 10
  Invoke-FinalizationStackFixture `
    -Mode "nonzero" -ExpectedVerdict "failed" `
    -ExpectedHelperExit 18 -ExpectedNativeExit 10
)

function New-TestShortcut(
  [string]$Path,
  [string]$Target,
  [string]$WorkingDirectory,
  [string]$Arguments = ""
) {
  New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force |
    Out-Null
  $shell = New-Object -ComObject WScript.Shell
  try {
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Arguments = $Arguments
    $shortcut.Save()
  } finally {
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
  }
}

function Invoke-ShortcutFixture(
  [string]$InstallRoot,
  [string]$CommonPrograms,
  [string]$CommonDesktop,
  [string]$LegacyPrograms,
  [string]$LegacyDesktop,
  [ValidateSet("", "write", "readback")]
  [string]$FailureStage = "",
  [string]$TransactionRoot = "",
  [switch]$ConfigureFirewall,
  [switch]$ValidateTransaction,
  [switch]$DesktopSelected
) {
  $arguments = @(
    "-NoProfile",
    "-NonInteractive",
    "-ExecutionPolicy", "Bypass",
    "-File", $managementScript,
    "-Action", $(if ($ValidateTransaction) {
      "ValidateInstallTransaction"
    } else {
      "ReconcileShortcuts"
    }),
    "-InstallDirectory", $InstallRoot,
    "-ShortcutCommonProgramsRoot", $CommonPrograms,
    "-ShortcutCommonDesktopRoot", $CommonDesktop,
    "-ShortcutLegacyProgramsRoot", $LegacyPrograms,
    "-ShortcutLegacyDesktopRoot", $LegacyDesktop)
  if (-not [string]::IsNullOrWhiteSpace($TransactionRoot)) {
    $arguments += @("-InstallTransactionRoot", $TransactionRoot)
  }
  if ($ConfigureFirewall) { $arguments += "-ConfigureFirewall" }
  if ($DesktopSelected) { $arguments += "-DesktopShortcutSelected" }
  $previous = $env:LIGASE_INSTALL_VALIDATION_HARNESS
  $previousFailure = $env:LIGASE_SHORTCUT_FAILURE_STAGE
  try {
    $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
    $env:LIGASE_SHORTCUT_FAILURE_STAGE = $FailureStage
    $output = @(& powershell.exe @arguments 2>&1)
    return [ordered]@{
      exitCode = $LASTEXITCODE
      output = if ($output.Count -gt 0) { [string]$output[-1] } else { "" }
    }
  } finally {
    $env:LIGASE_INSTALL_VALIDATION_HARNESS = $previous
    $env:LIGASE_SHORTCUT_FAILURE_STAGE = $previousFailure
  }
}

function Resolve-TransactionFixturePhysicalPath([string]$Path) {
  $fullPath = [IO.Path]::GetFullPath($Path)
  $cursor = $fullPath
  while (-not (Test-Path -LiteralPath $cursor)) {
    $parent = [IO.Path]::GetDirectoryName($cursor)
    if ([string]::IsNullOrWhiteSpace($parent) -or $parent -ceq $cursor) {
      throw "transactionFixturePhysicalRootUnavailable"
    }
    $cursor = $parent
  }
  while (-not [string]::IsNullOrWhiteSpace($cursor)) {
    $item = Get-Item -LiteralPath $cursor -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      $targets = @($item.Target)
      if ($targets.Count -ne 1 -or
          -not [IO.Path]::IsPathRooted([string]$targets[0])) {
        throw "transactionFixturePhysicalRootInvalid"
      }
      $target = [IO.Path]::GetFullPath([string]$targets[0]).TrimEnd('\')
      if (-not (Test-Path -LiteralPath $target -PathType Container)) {
        throw "transactionFixturePhysicalRootUnavailable"
      }
      $sourcePrefix = $item.FullName.TrimEnd('\')
      if (-not $fullPath.StartsWith(
          $sourcePrefix + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "transactionFixturePhysicalRootInvalid"
      }
      return Join-Path $target $fullPath.Substring($sourcePrefix.Length + 1)
    }
    $parent = [IO.Path]::GetDirectoryName($cursor)
    if ([string]::IsNullOrWhiteSpace($parent) -or $parent -ceq $cursor) {
      break
    }
    $cursor = $parent
  }
  return $fullPath
}

function Get-ShortcutFixtureSnapshot([string[]]$Paths) {
  $result = [ordered]@{}
  foreach ($path in $Paths) {
    $result[$path] = if (Test-Path -LiteralPath $path -PathType Leaf) {
      [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))
    } else { $null }
  }
  return ($result | ConvertTo-Json -Compress)
}

Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class LigaseRawProcess
{
    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static int Run(string application, string commandLine)
    {
        var startup = new STARTUPINFO();
        startup.cb = Marshal.SizeOf<STARTUPINFO>();
        PROCESS_INFORMATION process;
        if (!CreateProcessW(
            application,
            new StringBuilder(commandLine),
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            0,
            IntPtr.Zero,
            null,
            ref startup,
            out process))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (WaitForSingleObject(process.hProcess, 10000) != 0)
                throw new InvalidOperationException("harnessProcessTimeout");
            uint exitCode;
            if (!GetExitCodeProcess(process.hProcess, out exitCode))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return unchecked((int)exitCode);
        }
        finally
        {
            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
        }
    }
}

public static class LigaseReadinessProbe
{
    public static System.Threading.Thread ScheduleAppend(
        string path,
        int delayMilliseconds,
        string value)
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(delayMilliseconds);
            System.IO.File.AppendAllText(
                path,
                value,
                new System.Text.UTF8Encoding(false));
        });
        thread.IsBackground = true;
        thread.Start();
        return thread;
    }
}
"@

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

function Read-ClosedTextLines([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    return [ordered]@{ closed = $false; lines = @(); identity = "" }
  }
  try {
    $stream = [IO.File]::Open(
      $Path,
      [IO.FileMode]::Open,
      [IO.FileAccess]::Read,
      [IO.FileShare]::None)
    try {
      $raw = [byte[]]::new($stream.Length)
      $offset = 0
      while ($offset -lt $raw.Length) {
        $read = $stream.Read($raw, $offset, $raw.Length - $offset)
        if ($read -eq 0) { throw [IO.EndOfStreamException]::new() }
        $offset += $read
      }
      $hasher = [Security.Cryptography.SHA256]::Create()
      try {
        $contentHash = [BitConverter]::ToString(
          $hasher.ComputeHash($raw)).Replace("-", "")
      } finally {
        $hasher.Dispose()
      }
      [void]$stream.Seek(0, [IO.SeekOrigin]::Begin)
      $reader = [IO.StreamReader]::new(
        $stream,
        [Text.Encoding]::Default,
        $true)
      try {
        $lines = @()
        while (-not $reader.EndOfStream) {
          $lines += $reader.ReadLine()
        }
        return [ordered]@{
          closed = $true
          lines = @($lines)
          identity = "$($raw.Length):$contentHash"
        }
      } finally {
        $reader.Dispose()
      }
    } finally {
      $stream.Dispose()
    }
  } catch [IO.IOException] {
    return [ordered]@{ closed = $false; lines = @(); identity = "" }
  }
}

function Read-SharedTextLines([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    return @()
  }
  try {
    $stream = [IO.File]::Open(
      $Path,
      [IO.FileMode]::Open,
      [IO.FileAccess]::Read,
      [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
      $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::Default, $true)
      try {
        $lines = @()
        while (-not $reader.EndOfStream) {
          $lines += $reader.ReadLine()
        }
        return @($lines)
      } finally {
        $reader.Dispose()
      }
    } finally {
      $stream.Dispose()
    }
  } catch [IO.IOException] {
    return @()
  }
}

function Read-ClosedResultState([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    return [ordered]@{
      state = "absent"
      closed = $true
      lines = @()
      identity = "absent"
    }
  }
  $snapshot = Read-ClosedTextLines $Path
  if (-not $snapshot.closed) {
    return [ordered]@{
      state = "busy"
      closed = $false
      lines = @()
      identity = "busy"
    }
  }
  return [ordered]@{
    state = "present"
    closed = $true
    lines = @($snapshot.lines)
    identity = "present:$($snapshot.identity)"
  }
}

function Wait-HarnessReadiness(
  [string]$DiagnosticPath,
  [string]$ResultPath,
  [bool]$ShouldSucceed,
  [int]$NativeExitCode,
  [int]$TimeoutMilliseconds = 20000,
  [int]$QuietMilliseconds = 250,
  [scriptblock]$QuietWindowMutation = $null
) {
  $timer = [Diagnostics.Stopwatch]::StartNew()
  $diagnosticLines = @()
  $resultLines = @()
  $resultClosed = $false
  $progressCount = 0
  $diagnosticClosed = $false
  $readySnapshot = $null
  $resultState = "absent"
  do {
    $progressLines = @(Read-SharedTextLines $DiagnosticPath)
    $progressCount = @($progressLines).Count
    $expectedCountReached = if ($ShouldSucceed) {
      @($progressLines).Count -ge 4
    } else {
      @($progressLines).Count -ge 1
    }
    if ($expectedCountReached) {
      $closedDiagnostic = Read-ClosedTextLines $DiagnosticPath
      $closedResult = Read-ClosedResultState $ResultPath
      $resultState = $closedResult.state
      $closedCountReached = if ($ShouldSucceed) {
        @($closedDiagnostic.lines).Count -ge 4
      } else {
        @($closedDiagnostic.lines).Count -ge 1
      }
      $diagnosticClosed = $closedDiagnostic.closed
      $resultClosed = $closedResult.closed
      $resultStateAllowed = if ($ShouldSucceed) {
        $closedResult.state -ceq "present"
      } else {
        $closedResult.state -ceq "absent"
      }
      if ($closedDiagnostic.closed -and
          $closedResult.closed -and
          $resultStateAllowed -and
          $closedCountReached) {
        $firstFingerprint =
          "$($closedDiagnostic.identity)|$($closedResult.identity)"
        if ($null -ne $QuietWindowMutation) {
          & $QuietWindowMutation
          $QuietWindowMutation = $null
        }
        $remainingMilliseconds =
          $TimeoutMilliseconds - [int]$timer.ElapsedMilliseconds
        if ($remainingMilliseconds -le 0) { break }
        Start-Sleep -Milliseconds (
          [Math]::Min($QuietMilliseconds, $remainingMilliseconds))
        if ($timer.ElapsedMilliseconds -ge $TimeoutMilliseconds) { break }
        $secondDiagnostic = Read-ClosedTextLines $DiagnosticPath
        $secondResult = Read-ClosedResultState $ResultPath
        $secondResultStateAllowed = if ($ShouldSucceed) {
          $secondResult.state -ceq "present"
        } else {
          $secondResult.state -ceq "absent"
        }
        if ($secondDiagnostic.closed -and
            $secondResult.closed -and
            $secondResultStateAllowed) {
          $diagnosticLines = @($secondDiagnostic.lines)
          $resultLines = @($secondResult.lines)
          $secondFingerprint =
            "$($secondDiagnostic.identity)|$($secondResult.identity)"
          if ([string]::Equals(
              [string]$secondFingerprint,
              [string]$firstFingerprint,
              [StringComparison]::Ordinal)) {
          $readySnapshot = [ordered]@{
            timedOut = $false
            diagnosticLines = @($diagnosticLines)
            resultLines = @($resultLines)
            resultClosed = $resultClosed
            resultState = $secondResult.state
            elapsedMilliseconds = $timer.ElapsedMilliseconds
          }
          break
          }
        }
      }
    }
    if ($null -ne $readySnapshot) { break }
    Start-Sleep -Milliseconds 50
  } while ($timer.ElapsedMilliseconds -lt $TimeoutMilliseconds)
  if ($null -ne $readySnapshot) { return $readySnapshot }
  $resultExists = Test-Path -LiteralPath $ResultPath -PathType Leaf
  return [ordered]@{
    timedOut = $true
    diagnosticLines = @($diagnosticLines)
    resultLines = @($resultLines)
    progressCount = $progressCount
    resultClosed = $resultClosed
    resultState = $resultState
    elapsedMilliseconds = $timer.ElapsedMilliseconds
    safeDiagnostic = "phase=readiness;count=$progressCount;" +
      "nativeExit=$NativeExitCode;resultExists=" +
      $resultExists.ToString().ToLowerInvariant() +
      ";diagnosticClosed=$($diagnosticClosed.ToString().ToLowerInvariant())" +
      ";resultClosed=$($resultClosed.ToString().ToLowerInvariant())"
  }
}

function Invoke-Harness(
  [Parameter(Mandatory)][string] $Name,
  [Parameter(Mandatory)][string[]] $Arguments,
  [Parameter(Mandatory)][bool] $ShouldSucceed,
  [ValidateSet(
    "PowerShellDirect",
    "PowerShellStartProcess",
    "PowerShellStartProcessUnsafe",
    "ProcessStartInfo",
    "RawWin32")]
  [string] $LaunchMode = "PowerShellDirect",
  [string] $ExpectedInstallDirectory = "",
  [string] $ExpectedDataRoot = "",
  [string] $ExpectedDataRootPattern = "",
  [string] $ExpectedDataRootMode = "",
  [string] $ExpectedDataRootSource = "",
  [int] $ExpectedNativeExitCode = -1,
  [string[]] $ForbiddenPaths = @()
) {
  $caseRoot = Join-Path $root (
    "cases\$Name-$([Guid]::NewGuid().ToString('N'))")
  if (Test-Path -LiteralPath $caseRoot) {
    throw "harnessCaseRootAlreadyExists:$Name"
  }
  New-Item -ItemType Directory -Path $caseRoot | Out-Null
  $result = Join-Path $caseRoot "result.txt"
  if (Test-Path -LiteralPath $harnessDiagnostic) {
    Remove-Item -LiteralPath $harnessDiagnostic -Force
  }
  $hasTestOperator = @($Arguments | Where-Object {
    $_.StartsWith("/TestOperatorLocalAppData=", [StringComparison]::OrdinalIgnoreCase)
  }).Count -gt 0
  $nativeArguments = @($Arguments)
  if (-not $hasTestOperator) {
    $defaultOperatorLocal = Join-Path $caseRoot "operator-local"
    New-Item -ItemType Directory -Path $defaultOperatorLocal | Out-Null
    $nativeArguments += "/TestOperatorLocalAppData=$defaultOperatorLocal"
  }
  $nativeArguments += "/ResultFile=$result"
  $nativeExitCode = 255
  switch ($LaunchMode) {
    "PowerShellDirect" {
      & $harness @nativeArguments
      $nativeExitCode = $LASTEXITCODE
    }
    "PowerShellStartProcess" {
      $serialized = ($nativeArguments |
        ForEach-Object { ConvertTo-WindowsCommandLineArgument $_ }) -join " "
      $process = Start-Process -FilePath $harness `
        -ArgumentList $serialized -PassThru -Wait -WindowStyle Hidden
      $nativeExitCode = $process.ExitCode
    }
    "PowerShellStartProcessUnsafe" {
      $process = Start-Process -FilePath $harness `
        -ArgumentList $nativeArguments -PassThru -Wait -WindowStyle Hidden
      $nativeExitCode = $process.ExitCode
    }
    "ProcessStartInfo" {
      & $DotNet $argumentListRunner $harness @nativeArguments
      $nativeExitCode = $LASTEXITCODE
      if ($LASTEXITCODE -notin 0,12,13,14,15,16,17,18) {
        throw "argumentListRunnerFailed:$LASTEXITCODE"
      }
    }
    "RawWin32" {
      $allArguments = @($harness) + $nativeArguments
      $commandLine = ($allArguments |
        ForEach-Object { ConvertTo-WindowsCommandLineArgument $_ }) -join " "
      $nativeExitCode = [LigaseRawProcess]::Run($harness, $commandLine)
    }
  }
  $readiness = Wait-HarnessReadiness `
    -DiagnosticPath $harnessDiagnostic `
    -ResultPath $result `
    -ShouldSucceed $ShouldSucceed `
    -NativeExitCode $nativeExitCode
  $diagnosticLines = @($readiness.diagnosticLines)
  if ($readiness.timedOut) {
    throw "harnessReadinessTimeout:${Name}:$($readiness.safeDiagnostic)"
  }
  $resolverCodes = @($diagnosticLines | ForEach-Object {
    $parts = $_ -split '\|', 3
    if (@($parts).Count -ge 2 -and $parts[1] -match '^[0-9]+$') {
      [int]$parts[1]
    }
  })
  $exitCode = if (@($resolverCodes).Count -gt 0) {
    $resolverCodes[-1]
  } else {
    255
  }
  $exists = $readiness.resultState -ceq "present"
  if ($ShouldSucceed) {
    if (@($resolverCodes).Count -ne 4 -or
        @($resolverCodes.Where({ $_ -ne 0 })).Count -ne 0 -or
        -not $exists) {
      $detail = if (Test-Path -LiteralPath $harnessDiagnostic) {
        "phase=validation;count=$(@($diagnosticLines).Count);" +
          "nativeExit=$nativeExitCode;resultExists=$($exists.ToString().ToLowerInvariant())"
      } else {
        "phase=validation;count=0;nativeExit=$nativeExitCode;resultExists=false"
      }
      throw "harnessPositiveFailed:${Name}:$detail"
    }
    $actual = @($readiness.resultLines)
    if (@($actual).Count -ne 4 -or
        -not $actual[0].Equals(
          $ExpectedInstallDirectory,
          [StringComparison]::OrdinalIgnoreCase) -or
        (($ExpectedDataRootPattern.Length -eq 0 -and
          -not $actual[1].Equals(
            $ExpectedDataRoot,
            [StringComparison]::OrdinalIgnoreCase)) -or
         ($ExpectedDataRootPattern.Length -gt 0 -and
          $actual[1] -notmatch $ExpectedDataRootPattern)) -or
        ($ExpectedDataRootMode.Length -gt 0 -and
          $actual[2] -cne $ExpectedDataRootMode) -or
        ($ExpectedDataRootSource.Length -gt 0 -and
          -not $actual[3].Equals(
            $ExpectedDataRootSource,
            [StringComparison]::OrdinalIgnoreCase))) {
      throw "harnessResultMismatch:$Name"
    }
  } elseif (@($resolverCodes).Count -lt 1 -or
      $resolverCodes[0] -eq 0 -or
      $exists) {
    throw "harnessNegativeAccepted:$Name"
  }
  if ($ExpectedNativeExitCode -ge 0 -and
      $nativeExitCode -ne $ExpectedNativeExitCode) {
    throw "harnessNativeExitMismatch:${Name}:$nativeExitCode"
  }
  foreach ($forbiddenPath in $ForbiddenPaths) {
    if (Test-Path -LiteralPath $forbiddenPath) {
      throw "harnessNegativeWroteTarget:$Name"
    }
  }
  [ordered]@{
    name = $Name
    exitCode = $exitCode
    nativeExitCode = $nativeExitCode
    resultCreated = $exists
  }
}

$readinessNegativeRoot = Join-Path $root "readiness-negative-self-test"
New-Item -ItemType Directory -Path $readinessNegativeRoot | Out-Null
$readinessNegativeDiagnostic = Join-Path $readinessNegativeRoot "diagnostic.txt"
$readinessNegativeResult = Join-Path $readinessNegativeRoot "result.txt"
[IO.File]::WriteAllLines(
  $readinessNegativeDiagnostic,
  @("one", "two", "three"),
  [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines(
  $readinessNegativeResult,
  @("one", "two", "three", "four"),
  [Text.UTF8Encoding]::new($false))
$readinessNegative = Wait-HarnessReadiness `
  -DiagnosticPath $readinessNegativeDiagnostic `
  -ResultPath $readinessNegativeResult `
  -ShouldSucceed $true `
  -NativeExitCode 0 `
  -TimeoutMilliseconds 250
if (-not $readinessNegative.timedOut -or
    $readinessNegative.progressCount -ne 3) {
  throw "harnessReadinessNegativeAccepted"
}

$readinessLateWriterRoot = Join-Path $root "readiness-late-writer-self-test"
New-Item -ItemType Directory -Path $readinessLateWriterRoot | Out-Null
$readinessLateDiagnostic = Join-Path $readinessLateWriterRoot "diagnostic.txt"
$readinessLateResult = Join-Path $readinessLateWriterRoot "result.txt"
[IO.File]::WriteAllLines(
  $readinessLateDiagnostic,
  @("one", "two", "three", "four"),
  [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines(
  $readinessLateResult,
  @("one", "two", "three", "four"),
  [Text.UTF8Encoding]::new($false))
$lateWriterMutation = {
  [IO.File]::AppendAllText(
    $readinessLateDiagnostic,
    "five`n",
    [Text.UTF8Encoding]::new($false))
}
$readinessLate = Wait-HarnessReadiness `
  -DiagnosticPath $readinessLateDiagnostic `
  -ResultPath $readinessLateResult `
  -ShouldSucceed $true `
  -NativeExitCode 0 `
  -TimeoutMilliseconds 5000 `
  -QuietMilliseconds 1500 `
  -QuietWindowMutation $lateWriterMutation
if ($readinessLate.timedOut -or
    @($readinessLate.diagnosticLines).Count -ne 5 -or
    [int]$readinessLate.elapsedMilliseconds -gt 5000) {
  throw "harnessReadinessLateWriterAccepted"
}

$readinessLateResultRoot = Join-Path $root "readiness-late-result-self-test"
New-Item -ItemType Directory -Path $readinessLateResultRoot | Out-Null
$readinessLateResultDiagnostic = Join-Path $readinessLateResultRoot "diagnostic.txt"
$readinessLateResultPath = Join-Path $readinessLateResultRoot "result.txt"
[IO.File]::WriteAllLines(
  $readinessLateResultDiagnostic,
  @("negative"),
  [Text.UTF8Encoding]::new($false))
$lateResultWriter = [LigaseReadinessProbe]::ScheduleAppend(
  $readinessLateResultPath,
  100,
  "unexpected`n")
$readinessLateResult = Wait-HarnessReadiness `
  -DiagnosticPath $readinessLateResultDiagnostic `
  -ResultPath $readinessLateResultPath `
  -ShouldSucceed $false `
  -NativeExitCode 18 `
  -TimeoutMilliseconds 500
$lateResultWriter.Join()
if (-not $readinessLateResult.timedOut -or
    $readinessLateResult.resultState -cne "present") {
  throw "harnessReadinessLateResultAccepted"
}

$readinessAbsentRoot = Join-Path $root "readiness-absent-result-self-test"
New-Item -ItemType Directory -Path $readinessAbsentRoot | Out-Null
$readinessAbsentDiagnostic = Join-Path $readinessAbsentRoot "diagnostic.txt"
$readinessAbsentResult = Join-Path $readinessAbsentRoot "result.txt"
[IO.File]::WriteAllLines(
  $readinessAbsentDiagnostic,
  @("negative"),
  [Text.UTF8Encoding]::new($false))
$readinessAbsent = Wait-HarnessReadiness `
  -DiagnosticPath $readinessAbsentDiagnostic `
  -ResultPath $readinessAbsentResult `
  -ShouldSucceed $false `
  -NativeExitCode 18 `
  -TimeoutMilliseconds 1000
if ($readinessAbsent.timedOut -or
    $readinessAbsent.resultState -cne "absent") {
  throw "harnessReadinessAbsentResultRejected"
}

$readinessPresentRoot = Join-Path $root "readiness-present-result-self-test"
New-Item -ItemType Directory -Path $readinessPresentRoot | Out-Null
$readinessPresentDiagnostic = Join-Path $readinessPresentRoot "diagnostic.txt"
$readinessPresentResult = Join-Path $readinessPresentRoot "result.txt"
[IO.File]::WriteAllLines(
  $readinessPresentDiagnostic,
  @("negative"),
  [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines(
  $readinessPresentResult,
  @("unexpected"),
  [Text.UTF8Encoding]::new($false))
$readinessPresent = Wait-HarnessReadiness `
  -DiagnosticPath $readinessPresentDiagnostic `
  -ResultPath $readinessPresentResult `
  -ShouldSucceed $false `
  -NativeExitCode 18 `
  -TimeoutMilliseconds 500
if (-not $readinessPresent.timedOut -or
    $readinessPresent.resultState -cne "present") {
  throw "harnessReadinessPresentResultAccepted"
}

function Invoke-FailureFlowHarness(
  [Parameter(Mandatory)]
  [ValidateSet(
    "helperFailure",
    "migrationFailure",
    "integrationFailure",
    "rollbackFailure",
    "silentProvisional")]
  [string] $FailureMode,
  [Parameter(Mandatory)][string] $ExpectedCode,
  [Parameter(Mandatory)][string] $ExpectedRollback,
  [Parameter(Mandatory)][string] $ExpectedFailedField
) {
  $caseRoot = Join-Path $root "failure-flow-$FailureMode"
  New-Item -ItemType Directory -Path $caseRoot -Force | Out-Null
  $result = Join-Path $caseRoot "result.txt"
  $evidence = Join-Path $caseRoot "last-outcome.json"
  $nativeArguments = @(
    "/InstallDirectory=$(Join-Path $caseRoot 'program')",
    "/DataRoot=$(Join-Path $caseRoot 'data')",
    "/ResultFile=$result",
    "/EvidenceFile=$evidence",
    "/FailureMode=$FailureMode")
  $serialized = ($nativeArguments |
    ForEach-Object { ConvertTo-WindowsCommandLineArgument $_ }) -join " "
  $process = Start-Process -FilePath $harness `
    -ArgumentList $serialized -PassThru -Wait -WindowStyle Hidden
  $nativeExitCode = $process.ExitCode
  if ($nativeExitCode -eq 0) {
    throw "failureFlowNativeExitWasZero:$FailureMode"
  }
  $deadline = [DateTime]::UtcNow.AddSeconds(5)
  while (
    [DateTime]::UtcNow -lt $deadline -and
    (-not (Test-Path -LiteralPath $result -PathType Leaf) -or
     -not (Test-Path -LiteralPath $evidence -PathType Leaf))
  ) {
    Start-Sleep -Milliseconds 50
  }
  if (-not (Test-Path -LiteralPath $result -PathType Leaf) -or
      -not (Test-Path -LiteralPath $evidence -PathType Leaf)) {
    throw "failureFlowEvidenceMissing:$FailureMode"
  }
  $lines = @([IO.File]::ReadAllLines($result, [Text.Encoding]::Unicode))
  if (@($lines).Count -ne 3 -or
      $lines[0] -cne "failed" -or
      $lines[1] -cne $ExpectedCode -or
      $lines[2] -cne $ExpectedRollback) {
    throw "failureFlowResultMismatch:$FailureMode"
  }
  $document = [IO.File]::ReadAllText(
    $evidence, [Text.Encoding]::Unicode) | ConvertFrom-Json
  if (
    $document.schemaVersion -ne 1 -or
    $document.phase -cne "failed" -or
    $document.success -ne $false -or
    $document.resultCode -cne $ExpectedCode -or
    $document.failedField -cne $ExpectedFailedField -or
    $document.helper.exitCode -ne 10 -or
    $document.rollback.state -cne $ExpectedRollback -or
    $document.firewall.state -cne "notChecked" -or
    $document.displayedSuccess -ne $false -or
    $document.displayedFailure -ne $true
  ) {
    throw "failureFlowEvidenceMismatch:$FailureMode"
  }
  if ((Test-Path -LiteralPath (Join-Path $caseRoot "program")) -or
      (Test-Path -LiteralPath (Join-Path $caseRoot "data"))) {
    throw "failureFlowUnexpectedProductWrite:$FailureMode"
  }
  [ordered]@{
    name = "normal-pages-$FailureMode"
    nativeExitCode = $nativeExitCode
    machineFailure = $true
    displayedSuccess = $false
    displayedFailure = $true
    evidenceReadable = $true
    rollback = $ExpectedRollback
    failedField = $ExpectedFailedField
  }
}

$chineseProgramPath =
  "D:\" + ([string][char]0x7A0B) + ([char]0x5E8F) +
  ([char]0x6587) + ([char]0x4EF6) + "\Ligase Host"
$chineseDataPath =
  "D:\" + ([string][char]0x4E3B) + ([char]0x673A) +
  ([char]0x6570) + ([char]0x636E) + "\Ligase Host"
$spaceProgramPath = 'D:\Program Files\Ligase Host Harness'
$existingInstall = Join-Path $root "existing-install"
$existingData = Join-Path $root "existing-data"
New-Item -ItemType Directory -Path $existingInstall, $existingData -Force |
  Out-Null
[IO.File]::WriteAllText(
  (Join-Path $existingInstall "ligase-bootstrap.json"),
  (@{ schemaVersion = 1; dataRoot = $existingData } |
    ConvertTo-Json -Compress),
  [Text.UTF8Encoding]::new($false))

$results = @(
  Invoke-Harness `
    -Name "upgrade-rejects-inherited-existing-acl" `
    -Arguments @(
      "/InstallDirectory=$existingInstall",
      "/DataRoot=$existingData") `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "upgrade-rejects-data-root-change" `
    -Arguments @(
      "/InstallDirectory=$existingInstall",
      "/DataRoot=$(Join-Path $root 'different-data')") `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "fresh-session-stable-programdata-uuid-next-back-next" `
    -Arguments @("/InstallDirectory=$spaceProgramPath") `
    -ShouldSucceed $true `
    -ExpectedInstallDirectory $spaceProgramPath `
    -ExpectedDataRootPattern ('^' +
      [regex]::Escape((Join-Path $env:ProgramData 'Ligase Host\Instances\')) +
      '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')
  Invoke-Harness `
    -Name "custom-chinese-path-next-back-next" `
    -Arguments @(
      "/InstallDirectory=$chineseProgramPath",
      "/DataRoot=$chineseDataPath") `
    -ShouldSucceed $true `
    -ExpectedInstallDirectory $chineseProgramPath `
    -ExpectedDataRoot $chineseDataPath
  Invoke-Harness `
    -Name "d-paths-with-spaces-powershell-direct" `
    -Arguments @(
      "/InstallDirectory=$spaceProgramPath",
      '/DataRoot=D:\Development\Ligase Data\Host') `
    -ShouldSucceed $true `
    -ExpectedInstallDirectory $spaceProgramPath `
    -ExpectedDataRoot 'D:\Development\Ligase Data\Host'
  Invoke-Harness `
    -Name "d-paths-with-spaces-start-process" `
    -LaunchMode "PowerShellStartProcess" `
    -Arguments @(
      "/InstallDirectory=$spaceProgramPath",
      '/DataRoot=D:\Development\Ligase Data\Host') `
    -ShouldSucceed $true `
    -ExpectedInstallDirectory $spaceProgramPath `
    -ExpectedDataRoot 'D:\Development\Ligase Data\Host'
  Invoke-Harness `
    -Name "d-paths-with-spaces-process-start-info" `
    -LaunchMode "ProcessStartInfo" `
    -Arguments @(
      "/InstallDirectory=$spaceProgramPath",
      '/DataRoot=D:\Development\Ligase Data\Host') `
    -ShouldSucceed $true `
    -ExpectedInstallDirectory $spaceProgramPath `
    -ExpectedDataRoot 'D:\Development\Ligase Data\Host'
  Invoke-Harness `
    -Name "d-paths-with-spaces-raw-win32" `
    -LaunchMode "RawWin32" `
    -Arguments @(
      "/InstallDirectory=$spaceProgramPath",
      '/DataRoot=D:\Development\Ligase Data\Host') `
    -ShouldSucceed $true `
    -ExpectedInstallDirectory $spaceProgramPath `
    -ExpectedDataRoot 'D:\Development\Ligase Data\Host'
  Invoke-Harness `
    -Name "unsafe-start-process-split-path" `
    -LaunchMode "PowerShellStartProcessUnsafe" `
    -Arguments @(
      "/InstallDirectory=$spaceProgramPath",
      '/DataRoot=D:\Development\Ligase Data\Host') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "malformed-zero-write" `
    -Arguments @(
      "/InstallDirectory=$(Join-Path $root 'must-not-exist-install')",
      "/DataRoot=$(Join-Path $root 'must-not-exist-data')",
      "stray-token") `
    -ShouldSucceed $false `
    -ForbiddenPaths @(
      (Join-Path $root "must-not-exist-install"),
      (Join-Path $root "must-not-exist-data"))
  Invoke-Harness `
    -Name "duplicate-install" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/InstallDirectory=E:\Ligase Host') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "duplicate-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot=D:\Ligase Data',
      '/DataRoot=E:\Ligase Data') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "cross-duplicate" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/InstallDirectory=E:\Ligase Host',
      '/DataRoot=D:\Ligase Data',
      '/DataRoot=E:\Ligase Data') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "malformed-install" `
    -Arguments @('/InstallDirectory') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "malformed-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "empty-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot=') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "relative-install" `
    -Arguments @('/InstallDirectory=relative\Ligase Host') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "relative-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot=relative\Ligase Data') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "drive-root-install" `
    -Arguments @('/InstallDirectory=D:\') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "drive-root-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot=D:\') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "unc-install" `
    -Arguments @('/InstallDirectory=\\server\share\Ligase Host') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "unc-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot=\\server\share\Ligase Data') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "device-install" `
    -Arguments @('/InstallDirectory=\\?\D:\Ligase Host') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "device-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot=\\?\D:\Ligase Data') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "noncanonical-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot=D:\Development\..\Ligase Data') `
    -ShouldSucceed $false
)

$orphanOperatorLocal = Join-Path $root "orphan-operator-local"
$orphanInstances = Join-Path $orphanOperatorLocal "Ligase Host\Instances"
New-Item -ItemType Directory -Path $orphanInstances -Force | Out-Null
$results += Invoke-Harness `
  -Name "orphan-zero-candidates-remains-fresh" `
  -Arguments @(
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal") `
  -ShouldSucceed $true `
  -ExpectedInstallDirectory $spaceProgramPath `
  -ExpectedDataRootPattern ('^' +
    [regex]::Escape((Join-Path $env:ProgramData 'Ligase Host\Instances\')) +
    '[0-9a-f-]{36}$') `
  -ExpectedDataRootMode "freshDefault"

$orphanId = "00000000-0000-0000-0000-000000000101"
$orphanSource = Join-Path $orphanInstances $orphanId
New-Item -ItemType Directory -Path $orphanSource -Force | Out-Null
$orphanAcl = Get-Acl -LiteralPath $orphanSource
$orphanAcl.AddAccessRule(
  [Security.AccessControl.FileSystemAccessRule]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent().User,
    [Security.AccessControl.FileSystemRights]::Modify,
    [Security.AccessControl.InheritanceFlags](
      [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
      [Security.AccessControl.InheritanceFlags]::ObjectInherit),
    [Security.AccessControl.PropagationFlags]::None,
    [Security.AccessControl.AccessControlType]::Allow))
Set-Acl -LiteralPath $orphanSource -AclObject $orphanAcl
foreach ($name in @("ligase-authority.json", "library.json", "ligase-sync.json")) {
  [IO.File]::WriteAllText(
    (Join-Path $orphanSource $name),
    "{}",
    [Text.UTF8Encoding]::new($false))
}
$orphanEntriesBeforeSilent = @(
  Get-ChildItem -LiteralPath $orphanInstances -Force -Recurse |
    ForEach-Object { $_.FullName }
)
$results += Invoke-Harness `
  -Name "orphan-one-candidate-silent-without-action-fails-zero-write" `
  -Arguments @(
    "/S",
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal") `
  -ShouldSucceed $false `
  -LaunchMode "PowerShellStartProcess" `
  -ExpectedNativeExitCode 18
$orphanEntriesAfterSilent = @(
  Get-ChildItem -LiteralPath $orphanInstances -Force -Recurse |
    ForEach-Object { $_.FullName }
)
if (Compare-Object $orphanEntriesBeforeSilent $orphanEntriesAfterSilent) {
  throw "orphanSilentWithoutActionMutatedSource"
}
$results += Invoke-Harness `
  -Name "orphan-one-candidate-gui-proposal-has-no-confirmed-action" `
  -Arguments @(
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal") `
  -ShouldSucceed $true `
  -ExpectedInstallDirectory $spaceProgramPath `
  -ExpectedDataRootPattern ('^' +
    [regex]::Escape((Join-Path $env:ProgramData 'Ligase Host\Instances\')) +
    '[0-9a-f-]{36}$') `
  -ExpectedDataRootMode "orphanLegacyRecovery" `
  -ExpectedDataRootSource $orphanSource

$results += Invoke-Harness `
  -Name "orphan-one-candidate-recovery-next-back-next" `
  -Arguments @(
    "/S",
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal",
    "/OrphanLegacyAction=Recover") `
  -ShouldSucceed $true `
  -ExpectedInstallDirectory $spaceProgramPath `
  -ExpectedDataRootPattern ('^' +
    [regex]::Escape((Join-Path $env:ProgramData 'Ligase Host\Instances\')) +
    '[0-9a-f-]{36}$') `
  -ExpectedDataRootMode "orphanLegacyRecovery" `
  -ExpectedDataRootSource $orphanSource `
  -ExpectedNativeExitCode 0

$results += Invoke-Harness `
  -Name "orphan-one-candidate-gui-recover-consumes-confirmed-projection" `
  -Arguments @(
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal",
    "/OrphanLegacyAction=Recover") `
  -ShouldSucceed $true `
  -ExpectedInstallDirectory $spaceProgramPath `
  -ExpectedDataRootPattern ('^' +
    [regex]::Escape((Join-Path $env:ProgramData 'Ligase Host\Instances\')) +
    '[0-9a-f-]{36}$') `
  -ExpectedDataRootMode "orphanLegacyRecovery" `
  -ExpectedDataRootSource $orphanSource `
  -ExpectedNativeExitCode 0

$results += Invoke-Harness `
  -Name "orphan-one-candidate-explicit-fresh-choice" `
  -Arguments @(
    "/S",
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal",
    "/OrphanLegacyAction=CreateFresh") `
  -ShouldSucceed $true `
  -ExpectedInstallDirectory $spaceProgramPath `
  -ExpectedDataRootPattern ('^' +
    [regex]::Escape((Join-Path $env:ProgramData 'Ligase Host\Instances\')) +
    '[0-9a-f-]{36}$') `
  -ExpectedDataRootMode "freshDefault" `
  -ExpectedNativeExitCode 0

$secondOrphan = Join-Path $orphanInstances "00000000-0000-0000-0000-000000000102"
New-Item -ItemType Directory -Path $secondOrphan -Force | Out-Null
$secondAcl = Get-Acl -LiteralPath $secondOrphan
$secondAcl.AddAccessRule(
  [Security.AccessControl.FileSystemAccessRule]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent().User,
    [Security.AccessControl.FileSystemRights]::Modify,
    [Security.AccessControl.InheritanceFlags](
      [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
      [Security.AccessControl.InheritanceFlags]::ObjectInherit),
    [Security.AccessControl.PropagationFlags]::None,
    [Security.AccessControl.AccessControlType]::Allow))
Set-Acl -LiteralPath $secondOrphan -AclObject $secondAcl
foreach ($name in @("ligase-authority.json", "library.json", "ligase-sync.json")) {
  [IO.File]::WriteAllText(
    (Join-Path $secondOrphan $name),
    "{}",
    [Text.UTF8Encoding]::new($false))
}
$results += Invoke-Harness `
  -Name "orphan-multiple-candidates-fail-closed" `
  -Arguments @(
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal") `
  -ShouldSucceed $false

Remove-Item -LiteralPath $secondOrphan -Recurse -Force
[IO.File]::WriteAllText(
  (Join-Path $orphanSource "ligase-sync.json"),
  "{invalid",
  [Text.UTF8Encoding]::new($false))
$results += Invoke-Harness `
  -Name "orphan-malformed-required-json-fail-closed" `
  -Arguments @(
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal") `
  -ShouldSucceed $false

Remove-Item -LiteralPath $orphanSource -Recurse -Force
$orphanReparseTarget = Join-Path $root "orphan-reparse-target"
New-Item -ItemType Directory -Path $orphanReparseTarget -Force | Out-Null
New-Item -ItemType Junction `
  -Path (Join-Path $orphanInstances "00000000-0000-0000-0000-000000000103") `
  -Target $orphanReparseTarget | Out-Null
$results += Invoke-Harness `
  -Name "orphan-reparse-candidate-fail-closed" `
  -Arguments @(
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal") `
  -ShouldSucceed $false

$standardDriftInstall = Join-Path $root "standard-drift-install"
$standardDriftData = Join-Path $root "ProgramDataFixture\Ligase Host\Instances\00000000-0000-0000-0000-000000000001"
New-Item -ItemType Directory -Path $standardDriftInstall, $standardDriftData -Force |
  Out-Null
[IO.File]::WriteAllText(
  (Join-Path $standardDriftInstall "ligase-bootstrap.json"),
  (@{ schemaVersion = 1; dataRoot = $standardDriftData } |
    ConvertTo-Json -Compress),
  [Text.UTF8Encoding]::new($false))
$results += Invoke-Harness `
  -Name "standard-programdata-inherited-acl-drift-rejected" `
  -Arguments @("/InstallDirectory=$standardDriftInstall") `
  -ShouldSucceed $false

$missingInstall = Join-Path $root "missing-existing-install"
New-Item -ItemType Directory -Path $missingInstall -Force | Out-Null
[IO.File]::WriteAllText(
  (Join-Path $missingInstall "ligase-bootstrap.json"),
  (@{
      schemaVersion = 1
      dataRoot = (Join-Path $root "missing-existing-data")
    } | ConvertTo-Json -Compress),
  [Text.UTF8Encoding]::new($false))
$results += Invoke-Harness `
  -Name "missing-existing-data-root-rejected" `
  -Arguments @("/InstallDirectory=$missingInstall") `
  -ShouldSucceed $false

$reparseInstall = Join-Path $root "reparse-existing-install"
$reparseTarget = Join-Path $root "reparse-target"
$reparseData = Join-Path $root "reparse-existing-data"
New-Item -ItemType Directory -Path $reparseInstall, $reparseTarget -Force |
  Out-Null
New-Item -ItemType Junction -Path $reparseData -Target $reparseTarget |
  Out-Null
[IO.File]::WriteAllText(
  (Join-Path $reparseInstall "ligase-bootstrap.json"),
  (@{ schemaVersion = 1; dataRoot = $reparseData } |
    ConvertTo-Json -Compress),
  [Text.UTF8Encoding]::new($false))
$results += Invoke-Harness `
  -Name "reparse-existing-data-root-rejected" `
  -Arguments @("/InstallDirectory=$reparseInstall") `
  -ShouldSucceed $false

$shortcutRoot = Join-Path $root "shortcut-fixture"
$shortcutInstall = Join-Path $shortcutRoot "install"
$shortcutCommonPrograms = Join-Path $shortcutRoot "common-programs"
$shortcutCommonDesktop = Join-Path $shortcutRoot "common-desktop"
$shortcutLegacyPrograms = Join-Path $shortcutRoot "legacy-programs"
$shortcutLegacyDesktop = Join-Path $shortcutRoot "legacy-desktop"
New-Item -ItemType Directory -Path $shortcutInstall -Force | Out-Null
$shortcutLauncher = Join-Path $shortcutInstall "Ligase Host.exe"
[IO.File]::WriteAllBytes($shortcutLauncher, [byte[]](0))
$legacyStart = Join-Path $shortcutLegacyPrograms (
  "Ligase Host\Ligase Host.lnk")
$legacyDesktop = Join-Path $shortcutLegacyDesktop "Ligase Host.lnk"
New-TestShortcut $legacyStart $shortcutLauncher $shortcutInstall
New-TestShortcut $legacyDesktop $shortcutLauncher $shortcutInstall
$selectedResult = Invoke-ShortcutFixture `
  -InstallRoot $shortcutInstall `
  -CommonPrograms $shortcutCommonPrograms `
  -CommonDesktop $shortcutCommonDesktop `
  -LegacyPrograms $shortcutLegacyPrograms `
  -LegacyDesktop $shortcutLegacyDesktop `
  -DesktopSelected
if ($selectedResult.exitCode -ne 0 -or
    $selectedResult.output -cne (
      '{"code":"shortcutsReconciled","success":true,"desktopSelected":true}') -or
    (Test-Path -LiteralPath $legacyStart) -or
    (Test-Path -LiteralPath $legacyDesktop) -or
    -not (Test-Path -LiteralPath (
      Join-Path $shortcutCommonPrograms "Ligase Host\Ligase Host.lnk")) -or
    -not (Test-Path -LiteralPath (
      Join-Path $shortcutCommonDesktop "Ligase Host.lnk"))) {
  throw "shortcutSelectedFixtureFailed"
}
$unselectedResult = Invoke-ShortcutFixture `
  -InstallRoot $shortcutInstall `
  -CommonPrograms $shortcutCommonPrograms `
  -CommonDesktop $shortcutCommonDesktop `
  -LegacyPrograms $shortcutLegacyPrograms `
  -LegacyDesktop $shortcutLegacyDesktop
if ($unselectedResult.exitCode -ne 0 -or
    $unselectedResult.output -cne (
      '{"code":"shortcutsReconciled","success":true,"desktopSelected":false}') -or
    (Test-Path -LiteralPath (
      Join-Path $shortcutCommonDesktop "Ligase Host.lnk"))) {
  throw "shortcutUnselectedFixtureFailed"
}
$unownedTarget = Join-Path $shortcutRoot "not-ligase.exe"
[IO.File]::WriteAllBytes($unownedTarget, [byte[]](0))
New-TestShortcut $legacyDesktop $unownedTarget $shortcutRoot "--not-owned"
$preserveResult = Invoke-ShortcutFixture `
  -InstallRoot $shortcutInstall `
  -CommonPrograms $shortcutCommonPrograms `
  -CommonDesktop $shortcutCommonDesktop `
  -LegacyPrograms $shortcutLegacyPrograms `
  -LegacyDesktop $shortcutLegacyDesktop
if ($preserveResult.exitCode -ne 0 -or
    -not (Test-Path -LiteralPath $legacyDesktop)) {
  throw "shortcutNonOwnedPreservationFailed"
}
$commonDesktopShortcut = Join-Path $shortcutCommonDesktop "Ligase Host.lnk"
New-TestShortcut $commonDesktopShortcut $unownedTarget $shortcutRoot "--not-owned"
$conflictResult = Invoke-ShortcutFixture `
  -InstallRoot $shortcutInstall `
  -CommonPrograms $shortcutCommonPrograms `
  -CommonDesktop $shortcutCommonDesktop `
  -LegacyPrograms $shortcutLegacyPrograms `
  -LegacyDesktop $shortcutLegacyDesktop `
  -DesktopSelected
if ($conflictResult.exitCode -eq 0 -or
    -not (Test-Path -LiteralPath $commonDesktopShortcut)) {
  throw "shortcutConflictFailClosedFixtureFailed"
}

$combinationRoot = Join-Path $root "shortcut-combination-fixture"
$combinationInstall = Join-Path $combinationRoot "install"
$combinationCommonPrograms = Join-Path $combinationRoot "common-programs"
$combinationCommonDesktop = Join-Path $combinationRoot "common-desktop"
$combinationLegacyPrograms = Join-Path $combinationRoot "legacy-programs"
$combinationLegacyDesktop = Join-Path $combinationRoot "legacy-desktop"
New-Item -ItemType Directory -Path $combinationInstall -Force | Out-Null
$combinationLauncher = Join-Path $combinationInstall "Ligase Host.exe"
[IO.File]::WriteAllBytes($combinationLauncher, [byte[]](0))
$combinationStart = Join-Path $combinationCommonPrograms (
  "Ligase Host\Ligase Host.lnk")
$combinationDesktop = Join-Path $combinationCommonDesktop "Ligase Host.lnk"
$combinationLegacyStart = Join-Path $combinationLegacyPrograms (
  "Ligase Host\Ligase Host.lnk")
$combinationLegacyDesk = Join-Path $combinationLegacyDesktop "Ligase Host.lnk"
$combinationPaths = @(
  $combinationStart,
  $combinationDesktop,
  $combinationLegacyStart,
  $combinationLegacyDesk)
$firewallSentinel = Join-Path $combinationRoot "owned-firewall-state.txt"
[IO.File]::WriteAllText($firewallSentinel, "unchanged")
New-TestShortcut $combinationLegacyStart $combinationLauncher `
  $combinationInstall
New-TestShortcut $combinationStart $unownedTarget $shortcutRoot "--not-owned"
$beforeStartConflict = Get-ShortcutFixtureSnapshot $combinationPaths
$beforeFirewall = [IO.File]::ReadAllText($firewallSentinel)
$startConflict = Invoke-ShortcutFixture `
  -InstallRoot $combinationInstall `
  -CommonPrograms $combinationCommonPrograms `
  -CommonDesktop $combinationCommonDesktop `
  -LegacyPrograms $combinationLegacyPrograms `
  -LegacyDesktop $combinationLegacyDesktop
if ($startConflict.exitCode -eq 0 -or
    $startConflict.output -notmatch '"code":"startMenuShortcutConflict"' -or
    (Get-ShortcutFixtureSnapshot $combinationPaths) -cne $beforeStartConflict -or
    [IO.File]::ReadAllText($firewallSentinel) -cne $beforeFirewall) {
  throw "shortcutStartConflictTransactionFixtureFailed"
}

Remove-Item -LiteralPath $combinationStart -Force
Remove-Item -LiteralPath $combinationLegacyStart -Force
New-TestShortcut $combinationDesktop $unownedTarget $shortcutRoot "--not-owned"
$beforeDesktopConflict = Get-ShortcutFixtureSnapshot $combinationPaths
$desktopConflict = Invoke-ShortcutFixture `
  -InstallRoot $combinationInstall `
  -CommonPrograms $combinationCommonPrograms `
  -CommonDesktop $combinationCommonDesktop `
  -LegacyPrograms $combinationLegacyPrograms `
  -LegacyDesktop $combinationLegacyDesktop `
  -DesktopSelected
if ($desktopConflict.exitCode -eq 0 -or
    $desktopConflict.output -notmatch '"code":"desktopShortcutConflict"' -or
    (Get-ShortcutFixtureSnapshot $combinationPaths) -cne $beforeDesktopConflict -or
    [IO.File]::ReadAllText($firewallSentinel) -cne $beforeFirewall) {
  throw "shortcutDesktopConflictTransactionFixtureFailed"
}

Remove-Item -LiteralPath $combinationDesktop -Force
$beforeWriteFailure = Get-ShortcutFixtureSnapshot $combinationPaths
$writeFailure = Invoke-ShortcutFixture `
  -InstallRoot $combinationInstall `
  -CommonPrograms $combinationCommonPrograms `
  -CommonDesktop $combinationCommonDesktop `
  -LegacyPrograms $combinationLegacyPrograms `
  -LegacyDesktop $combinationLegacyDesktop `
  -DesktopSelected `
  -FailureStage "write"
if ($writeFailure.exitCode -eq 0 -or
    (Get-ShortcutFixtureSnapshot $combinationPaths) -cne $beforeWriteFailure) {
  throw "shortcutWriteFailureRollbackFixtureFailed"
}
$readbackFailure = Invoke-ShortcutFixture `
  -InstallRoot $combinationInstall `
  -CommonPrograms $combinationCommonPrograms `
  -CommonDesktop $combinationCommonDesktop `
  -LegacyPrograms $combinationLegacyPrograms `
  -LegacyDesktop $combinationLegacyDesktop `
  -DesktopSelected `
  -FailureStage "readback"
if ($readbackFailure.exitCode -eq 0 -or
    (Get-ShortcutFixtureSnapshot $combinationPaths) -cne $beforeWriteFailure) {
  throw "shortcutReadbackFailureRollbackFixtureFailed"
}

$transactionPublish = Join-Path $OutputRoot "transaction-helper"
& $DotNet publish (Join-Path $sourceRoot (
    "tools/Ligase.Installation.TransactionHelper/" +
    "Ligase.Installation.TransactionHelper.csproj")) `
  -c Release -p:Platform=x64 -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:UseSharedCompilation=false `
  -o $transactionPublish | Out-Null
if ($LASTEXITCODE -ne 0) {
  throw "installTransactionHelperPublishFailed"
}
$transactionHelper = Join-Path $transactionPublish (
  "Ligase.Installation.TransactionHelper.exe")
if (-not (Test-Path -LiteralPath $transactionHelper -PathType Leaf)) {
  throw "installTransactionHelperMissing"
}
$inventoryPublish = Join-Path $OutputRoot "virtual-display-inventory-helper"
& $DotNet publish (Join-Path $sourceRoot (
    "tools/Ligase.VirtualDisplay.InventoryHelper/" +
    "Ligase.VirtualDisplay.InventoryHelper.csproj")) `
  -c Release -p:Platform=x64 -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:UseSharedCompilation=false `
  -o $inventoryPublish | Out-Null
if ($LASTEXITCODE -ne 0) {
  throw "virtualDisplayInventoryHelperPublishFailed"
}
$inventoryHelper = Join-Path $inventoryPublish (
  "Ligase.VirtualDisplay.InventoryHelper.exe")
if (-not (Test-Path -LiteralPath $inventoryHelper -PathType Leaf)) {
  throw "virtualDisplayInventoryHelperMissing"
}
$inventoryFixtureRoot = Join-Path $OutputRoot "virtual-display-inventory-fixtures"
New-Item -ItemType Directory -Path $inventoryFixtureRoot -Force | Out-Null
$inventoryCases = @(
  [ordered]@{ name="zero"; expected=0; nodes=@() },
  [ordered]@{ name="present"; expected=1; nodes=@([ordered]@{
    instanceId="ROOT\DISPLAY\0000";hardwareIds=@("ROOT\SUDOMAKER\SUDOVDA");
    present=$true;status="OK";driverInf="oem32.inf"}) },
  [ordered]@{ name="phantomClassUnknown"; expected=1; nodes=@([ordered]@{
    instanceId="ROOT\DISPLAY\0001";hardwareIds=@("root\sudomaker\sudovda");
    present=$false;status="Unknown";driverInf=""}) },
  [ordered]@{ name="duplicate"; expected=2; nodes=@(
    [ordered]@{instanceId="ROOT\DISPLAY\0000";hardwareIds=@("ROOT\SUDOMAKER\SUDOVDA");present=$true;status="OK";driverInf="oem32.inf"},
    [ordered]@{instanceId="ROOT\DISPLAY\0001";hardwareIds=@("root\sudomaker\sudovda");present=$false;status="Unknown";driverInf=""}) },
  [ordered]@{ name="unrelated"; expected=0; nodes=@([ordered]@{
    instanceId="ROOT\DISPLAY\0002";hardwareIds=@(
      "ROOT\OTHER\DISPLAY", "ROOT\SUDOMAKER\SUDOVDA\EXTRA");
    present=$true;status="OK";driverInf="oem99.inf"}) }
)
$savedValidationHarness = [string]$env:LIGASE_INSTALL_VALIDATION_HARNESS
$savedInventoryValidation = [string]$env:LIGASE_VDISPLAY_INVENTORY_HELPER_VALIDATION
$inventoryHelperResults = @()
try {
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_VDISPLAY_INVENTORY_HELPER_VALIDATION = "1"
  foreach ($inventoryCase in $inventoryCases) {
    $fixturePath = Join-Path $inventoryFixtureRoot (
      [string]$inventoryCase.name + ".json")
    [ordered]@{schemaVersion=1;nodes=@($inventoryCase.nodes)} |
      ConvertTo-Json -Depth 8 -Compress |
      Set-Content -LiteralPath $fixturePath -Encoding UTF8 -NoNewline
    $output = @(& $inventoryHelper --validate-fixture $fixturePath)
    if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) {
      throw "virtualDisplayInventoryHelperCaseFailed"
    }
    $projection = $output[0] | ConvertFrom-Json
    if ([string]$projection.state -cne "available" -or
        @($projection.devices).Count -ne [int]$inventoryCase.expected) {
      throw "virtualDisplayInventoryHelperProjectionFailed"
    }
    $inventoryHelperResults += [ordered]@{
      name = [string]$inventoryCase.name
      result = "passed"
      matchingDeviceCount = @($projection.devices).Count
    }
    if ([string]$inventoryCase.name -ceq "present") {
      $device = @($projection.devices)[0]
      $request = [ordered]@{
        schemaVersion = 1
        inventoryNonce = [string]$projection.inventoryNonce
        inventoryEpochSha256 = [string]$projection.inventoryEpochSha256
        instanceId = [string]$device.instanceId
        instanceIdSha256 = [string]$device.instanceIdSha256
        removalAuthoritySha256 = [string]$device.removalAuthoritySha256
      }
      $requestBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        ($request | ConvertTo-Json -Compress))
      $requestToken = [Convert]::ToBase64String($requestBytes).TrimEnd('=').
        Replace('+', '-').Replace('/', '_')
      $removeOutput = @(& $inventoryHelper --validate-remove-fixture (
          $fixturePath) $requestToken)
      if ($LASTEXITCODE -ne 0 -or $removeOutput.Count -ne 1) {
        throw "virtualDisplayInventoryHelperRemovalFixtureFailed"
      }
      $removeProjection = $removeOutput[0] | ConvertFrom-Json
      if ([string]$removeProjection.state -cne "removed" -or
          [string]$removeProjection.instanceIdSha256 -cne
            [string]$device.instanceIdSha256 -or
          [string]$removeProjection.priorInventoryEpochSha256 -cne
            [string]$projection.inventoryEpochSha256 -or
          [int]$removeProjection.nativeCode -ne 0) {
        throw "virtualDisplayInventoryHelperRemovalProjectionFailed"
      }
      $inventoryHelperResults += [ordered]@{
        name = "exactRemovalAuthority"
        result = "passed"
        matchingDeviceCount = 1
      }
      foreach ($drift in @("epoch", "instanceHash", "authority", "instance")) {
        $invalid = ($request | ConvertTo-Json -Compress) | ConvertFrom-Json
        switch ($drift) {
          "epoch" { $invalid.inventoryEpochSha256 = "0" * 64 }
          "instanceHash" { $invalid.instanceIdSha256 = "0" * 64 }
          "authority" { $invalid.removalAuthoritySha256 = "0" * 64 }
          "instance" { $invalid.instanceId = "ROOT\DISPLAY\9999" }
        }
        $invalidBytes = [Text.UTF8Encoding]::new($false).GetBytes(
          ($invalid | ConvertTo-Json -Compress))
        $invalidToken = [Convert]::ToBase64String($invalidBytes).TrimEnd('=').
          Replace('+', '-').Replace('/', '_')
        $invalidOutput = @(& $inventoryHelper --validate-remove-fixture (
            $fixturePath) $invalidToken)
        if ($LASTEXITCODE -ne 20 -or $invalidOutput.Count -ne 1 -or
            [string](($invalidOutput[0] | ConvertFrom-Json).code) -cne
              "removalAuthorityInvalid") {
          throw "virtualDisplayInventoryHelperRemovalDriftAccepted:$drift"
        }
        $inventoryHelperResults += [ordered]@{
          name = "exactRemovalReject-$drift"
          result = "passed"
          matchingDeviceCount = -1
        }
      }
    }
  }
  $duplicateFixture = Join-Path $inventoryFixtureRoot "duplicate-identity.json"
  [ordered]@{schemaVersion=1;nodes=@(
    [ordered]@{instanceId="ROOT\DISPLAY\0000";hardwareIds=@("ROOT\SUDOMAKER\SUDOVDA");present=$true;status="OK";driverInf="oem32.inf"},
    [ordered]@{instanceId="root\display\0000";hardwareIds=@("ROOT\SUDOMAKER\SUDOVDA");present=$false;status="Unknown";driverInf=""})} |
    ConvertTo-Json -Depth 8 -Compress |
    Set-Content -LiteralPath $duplicateFixture -Encoding UTF8 -NoNewline
  $duplicateOutput = @(& $inventoryHelper --validate-fixture $duplicateFixture)
  if ($LASTEXITCODE -ne 15 -or $duplicateOutput.Count -ne 1 -or
      [string](($duplicateOutput[0] | ConvertFrom-Json).code) -cne
        "validationInvalid") {
    throw "virtualDisplayInventoryHelperDuplicateIdentityFailed"
  }
  $inventoryHelperResults += [ordered]@{
    name = "duplicateIdentityRejected"
    result = "passed"
    matchingDeviceCount = -1
  }
} finally {
  if ([string]::IsNullOrEmpty($savedValidationHarness)) {
    Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS -ErrorAction SilentlyContinue
  } else { $env:LIGASE_INSTALL_VALIDATION_HARNESS = $savedValidationHarness }
  if ([string]::IsNullOrEmpty($savedInventoryValidation)) {
    Remove-Item Env:\LIGASE_VDISPLAY_INVENTORY_HELPER_VALIDATION -ErrorAction SilentlyContinue
  } else { $env:LIGASE_VDISPLAY_INVENTORY_HELPER_VALIDATION = $savedInventoryValidation }
}
$nativeInventoryInvocationResults = @()
$savedNativeHarness = [string]$env:LIGASE_INSTALL_VALIDATION_HARNESS
$savedNativeValidation = [string]$env:LIGASE_VDISPLAY_INVENTORY_HELPER_VALIDATION
$savedHelperPath = [string]$env:LIGASE_VDISPLAY_INVENTORY_HELPER_PATH
$savedHelperBehavior = [string]$env:LIGASE_VDISPLAY_INVENTORY_HELPER_BEHAVIOR
$savedSchemaBehavior = [string]$env:LIGASE_VDISPLAY_INVENTORY_SCHEMA_BEHAVIOR
try {
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_VDISPLAY_INVENTORY_HELPER_VALIDATION = "1"
  $env:LIGASE_VDISPLAY_INVENTORY_HELPER_PATH = $inventoryHelper
  foreach ($behavior in @(
      "none", "hang", "overflow", "stderr", "stdoutPending",
      "stderrPending", "dualPending", "overflowPending", "pipeFault",
      "startRetain")) {
    if ($behavior -ceq "none") {
      Remove-Item Env:\LIGASE_VDISPLAY_INVENTORY_HELPER_BEHAVIOR `
        -ErrorAction SilentlyContinue
    } else { $env:LIGASE_VDISPLAY_INVENTORY_HELPER_BEHAVIOR = $behavior }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $raw = @(& (Join-Path $env:SystemRoot (
          "System32\WindowsPowerShell\v1.0\powershell.exe")) `
      -NoProfile -NonInteractive -ExecutionPolicy Bypass `
      -File $managementScript `
      -Action ValidateVirtualDisplayNativeInventoryHelper `
      -InstallDirectory $inventoryFixtureRoot)
    $nativeExit = $LASTEXITCODE
    $clock.Stop()
    if ($raw.Count -ne 1 -or $clock.ElapsedMilliseconds -gt 11000) {
      throw "virtualDisplayNativeInventoryInvocationUnavailable:$behavior"
    }
    $result = $raw[0] | ConvertFrom-Json
    $expectedExit = if ($behavior -ceq "none") { 0 } else { 18 }
    $expectedState = if ($behavior -ceq "none") { "available" } else { "failed" }
    if ($nativeExit -ne $expectedExit -or
        [string]$result.state -cne $expectedState -or
        [string]$result.cleanupState -cne "completed" -or
        -not [bool]$result.rootPidZero -or
        [int]$result.jobActiveProcesses -ne 0 -or
        -not [bool]$result.stdoutClosed -or
        -not [bool]$result.stderrClosed) {
      throw "virtualDisplayNativeInventoryInvocationFailed:$behavior"
    }
    $nativeInventoryInvocationResults += [ordered]@{
      name = $behavior
      state = [string]$result.state
      matchingDeviceCount = [int]$result.matchingDeviceCount
      elapsedMilliseconds = [int]$clock.ElapsedMilliseconds
      cleanupState = [string]$result.cleanupState
      rootPidZero = [bool]$result.rootPidZero
      jobActiveProcesses = [int]$result.jobActiveProcesses
      stdoutClosed = [bool]$result.stdoutClosed
      stderrClosed = [bool]$result.stderrClosed
    }
  }
  Remove-Item Env:\LIGASE_VDISPLAY_INVENTORY_HELPER_BEHAVIOR `
    -ErrorAction SilentlyContinue
  foreach ($schemaCase in @(
      @{ behavior = "missingProperty"; reason = "missingProperty"; count = 1 },
      @{ behavior = "unknownProperty"; reason = "unknownProperty"; count = 1 },
      @{ behavior = "duplicateProperty"; reason = "duplicateProperty"; count = 1 },
      @{ behavior = "recordCount"; reason = "recordCount"; count = 2 },
      @{ behavior = "recordCountNull"; reason = "recordCount"; count = 1 },
      @{ behavior = "recordCountEmpty"; reason = "recordCount"; count = 1 },
      @{ behavior = "recordCountLimit"; reason = "recordCount"; count = 17 },
      @{ behavior = "schemaVersion"; reason = "schemaVersion"; count = 1 },
      @{ behavior = "schemaVersionNonempty"; reason = "schemaVersion"; count = 1 },
      @{ behavior = "type"; reason = "type"; count = 1 },
      @{ behavior = "enum"; reason = "enum"; count = 1 },
      @{ behavior = "identity"; reason = "identity"; count = 1 })) {
    $schemaBehavior = [string]$schemaCase.behavior
    $env:LIGASE_VDISPLAY_INVENTORY_SCHEMA_BEHAVIOR = $schemaBehavior
    $raw = @(& (Join-Path $env:SystemRoot (
          "System32\WindowsPowerShell\v1.0\powershell.exe")) `
      -NoProfile -NonInteractive -ExecutionPolicy Bypass `
      -File $managementScript `
      -Action ValidateVirtualDisplayNativeInventoryHelper `
      -InstallDirectory $inventoryFixtureRoot)
    $nativeExit = $LASTEXITCODE
    if ($raw.Count -ne 1 -or $nativeExit -ne 18) {
      throw "virtualDisplayNativeSchemaCaseUnavailable:$schemaBehavior"
    }
    $result = $raw[0] | ConvertFrom-Json
    if ([string]$result.state -cne "failed" -or
        [string]$result.validationStage -cne "schema" -or
        [string]$result.schemaReason -cne [string]$schemaCase.reason -or
        [int]$result.schemaCount -ne [int]$schemaCase.count -or
        [string]$result.cleanupState -cne "completed" -or
        -not [bool]$result.rootPidZero -or
        [int]$result.jobActiveProcesses -ne 0 -or
        -not [bool]$result.stdoutClosed -or
        -not [bool]$result.stderrClosed) {
      throw "virtualDisplayNativeSchemaCaseFailed:$schemaBehavior"
    }
    $nativeInventoryInvocationResults += [ordered]@{
      name = "schema-$schemaBehavior"
      state = [string]$result.state
      schemaReason = [string]$result.schemaReason
      schemaCount = [int]$result.schemaCount
      cleanupState = [string]$result.cleanupState
      rootPidZero = [bool]$result.rootPidZero
      jobActiveProcesses = [int]$result.jobActiveProcesses
      stdoutClosed = [bool]$result.stdoutClosed
      stderrClosed = [bool]$result.stderrClosed
    }
  }
  $env:LIGASE_VDISPLAY_INVENTORY_SCHEMA_BEHAVIOR = "zeroDevices"
  $zeroRaw = @(& (Join-Path $env:SystemRoot (
        "System32\WindowsPowerShell\v1.0\powershell.exe")) `
    -NoProfile -NonInteractive -ExecutionPolicy Bypass `
    -File $managementScript `
    -Action ValidateVirtualDisplayNativeInventoryHelper `
    -InstallDirectory $inventoryFixtureRoot)
  if ($LASTEXITCODE -ne 0 -or $zeroRaw.Count -ne 1) {
    throw "virtualDisplayNativeZeroSchemaCaseUnavailable"
  }
  $zeroResult = $zeroRaw[0] | ConvertFrom-Json
  if ([string]$zeroResult.state -cne "available" -or
      [int]$zeroResult.matchingDeviceCount -ne 0 -or
      [string]$zeroResult.schemaReason -cne "none" -or
      [int]$zeroResult.schemaCount -ne 0 -or
      [string]$zeroResult.cleanupState -cne "completed" -or
      -not [bool]$zeroResult.rootPidZero -or
      [int]$zeroResult.jobActiveProcesses -ne 0 -or
      -not [bool]$zeroResult.stdoutClosed -or
      -not [bool]$zeroResult.stderrClosed) {
    throw "virtualDisplayNativeZeroSchemaCaseFailed"
  }
  $nativeInventoryInvocationResults += [ordered]@{
    name = "schema-zeroDevices"
    state = [string]$zeroResult.state
    schemaReason = [string]$zeroResult.schemaReason
    schemaCount = [int]$zeroResult.schemaCount
    cleanupState = [string]$zeroResult.cleanupState
    rootPidZero = [bool]$zeroResult.rootPidZero
    jobActiveProcesses = [int]$zeroResult.jobActiveProcesses
    stdoutClosed = [bool]$zeroResult.stdoutClosed
    stderrClosed = [bool]$zeroResult.stderrClosed
  }
} finally {
  if ([string]::IsNullOrEmpty($savedNativeHarness)) {
    Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
      -ErrorAction SilentlyContinue
  } else { $env:LIGASE_INSTALL_VALIDATION_HARNESS = $savedNativeHarness }
  if ([string]::IsNullOrEmpty($savedNativeValidation)) {
    Remove-Item Env:\LIGASE_VDISPLAY_INVENTORY_HELPER_VALIDATION `
      -ErrorAction SilentlyContinue
  } else {
    $env:LIGASE_VDISPLAY_INVENTORY_HELPER_VALIDATION = $savedNativeValidation
  }
  if ([string]::IsNullOrEmpty($savedHelperPath)) {
    Remove-Item Env:\LIGASE_VDISPLAY_INVENTORY_HELPER_PATH `
      -ErrorAction SilentlyContinue
  } else { $env:LIGASE_VDISPLAY_INVENTORY_HELPER_PATH = $savedHelperPath }
  if ([string]::IsNullOrEmpty($savedHelperBehavior)) {
    Remove-Item Env:\LIGASE_VDISPLAY_INVENTORY_HELPER_BEHAVIOR `
      -ErrorAction SilentlyContinue
  } else { $env:LIGASE_VDISPLAY_INVENTORY_HELPER_BEHAVIOR = $savedHelperBehavior }
  if ([string]::IsNullOrEmpty($savedSchemaBehavior)) {
    Remove-Item Env:\LIGASE_VDISPLAY_INVENTORY_SCHEMA_BEHAVIOR `
      -ErrorAction SilentlyContinue
  } else {
    $env:LIGASE_VDISPLAY_INVENTORY_SCHEMA_BEHAVIOR = $savedSchemaBehavior
  }
}
$boundedInstallRoot = Join-Path $combinationRoot "bounded-helper-install"
$boundedDeployment = Join-Path $boundedInstallRoot "Deployment"
New-Item -ItemType Directory -Path $boundedDeployment -Force | Out-Null
$boundedHelper = Join-Path $boundedDeployment (
  "Ligase.Installation.TransactionHelper.exe")
Copy-Item -LiteralPath $transactionHelper -Destination $boundedHelper
$boundedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $boundedHelper).Hash
$boundedManifest = [ordered]@{
  privilegedHelpers = @([ordered]@{
    relativePath = "Deployment/Ligase.Installation.TransactionHelper.exe"
    signedArtifactSha256 = $boundedHash.ToLowerInvariant()
  })
} | ConvertTo-Json -Depth 4 -Compress
Set-Content -LiteralPath (
  Join-Path $boundedInstallRoot "ligase-install-manifest.json") `
  -Value $boundedManifest -Encoding UTF8 -NoNewline
$boundedBefore = @(Get-ChildItem -LiteralPath $boundedInstallRoot -Recurse -File |
  ForEach-Object {
    [ordered]@{
      relative = $_.FullName.Substring(
        $boundedInstallRoot.TrimEnd('\').Length + 1)
      hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
    }
  } | ConvertTo-Json -Depth 3 -Compress) -join ""
$boundedResults = @()
$boundedSavedErrorAction = $ErrorActionPreference
foreach ($behavior in @(
    "hang", "delayedPipe", "oversizeStdout", "oversizeStderr", "killTree")) {
  $behaviorRoot = Join-Path $combinationRoot ("bounded-" + $behavior)
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_TEST_BEHAVIOR = $behavior
  $clock = [Diagnostics.Stopwatch]::StartNew()
  $ErrorActionPreference = "Continue"
  $behaviorOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File $managementScript `
    -Action PreflightInstallTransaction `
    -InstallDirectory $boundedInstallRoot `
    -InstallTransactionRoot $behaviorRoot 2>&1)
  $behaviorExit = $LASTEXITCODE
  $ErrorActionPreference = $boundedSavedErrorAction
  $clock.Stop()
  Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS -ErrorAction SilentlyContinue
  if ($behaviorExit -ne 10 -or $clock.Elapsed.TotalSeconds -gt 20) {
    throw "installTransactionBoundedInvocationFailed"
  }
  $behaviorResult = ($behaviorOutput[-1] | Out-String).Trim() |
    ConvertFrom-Json
  if ([string]$behaviorResult.code -notin @(
      "installTransactionUnavailable", "installTransactionInvalid") -or
      [bool]$behaviorResult.success -or
      [string]$behaviorResult.failedField -cne "installTransaction" -or
      [int]$behaviorResult.transactionHelperNativeExit -ne 18 -or
      [string]$behaviorResult.transactionHelperStage -cne "processTimeout") {
    throw "installTransactionBoundedEvidenceInvalid"
  }
  if (Test-Path -LiteralPath $behaviorRoot) {
    throw "installTransactionBoundedInvocationMutatedJournal"
  }
  $boundedResults += [ordered]@{
    name = "transaction-bounded-$behavior"
    passed = $true
  }
}
foreach ($writeCase in @(
    [ordered]@{
      behavior = "hangBeforeStdinRead"
      expectedExit = 10
      expectedStage = "processTimeout"
      rootMayExist = $false
    },
    [ordered]@{
      behavior = "delayedStdinRead"
      expectedExit = 0
      expectedStage = "delete"
      rootMayExist = $true
    },
    [ordered]@{
      behavior = "oversizeInput"
      expectedExit = 10
      expectedStage = "inputValidation"
      rootMayExist = $false
    })) {
  $writeRoot = Join-Path $combinationRoot (
    "bounded-" + [string]$writeCase.behavior)
  $savedWriteHarness = [Environment]::GetEnvironmentVariable(
    "LIGASE_INSTALL_VALIDATION_HARNESS",
    [EnvironmentVariableTarget]::Process)
  $savedWriteBehavior = [Environment]::GetEnvironmentVariable(
    "LIGASE_TRANSACTION_TEST_BEHAVIOR",
    [EnvironmentVariableTarget]::Process)
  try {
    $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
    $env:LIGASE_TRANSACTION_TEST_BEHAVIOR =
      [string]$writeCase.behavior
    $ErrorActionPreference = "Continue"
    $writeInvocation = @(
      & $DotNet $argumentListRunner --bounded-capture `
        20000 5000 8192 none powershell.exe `
        -NoProfile -ExecutionPolicy Bypass `
        -File $managementScript `
        -Action PreflightInstallTransaction `
        -InstallDirectory $boundedInstallRoot `
        -InstallTransactionRoot $writeRoot)
    $writeRunnerExit = $LASTEXITCODE
  } finally {
    $ErrorActionPreference = $boundedSavedErrorAction
    [Environment]::SetEnvironmentVariable(
      "LIGASE_INSTALL_VALIDATION_HARNESS", $savedWriteHarness,
      [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
      "LIGASE_TRANSACTION_TEST_BEHAVIOR", $savedWriteBehavior,
      [EnvironmentVariableTarget]::Process)
  }
  if ($writeInvocation.Count -ne 1) {
    throw "installTransactionBoundedWriteEnvelopeInvalid"
  }
  try {
    $writeEnvelope = [string]$writeInvocation[0] | ConvertFrom-Json
  } catch {
    throw "installTransactionBoundedWriteEnvelopeInvalid"
  }
  $writeExit = [int]$writeEnvelope.exitCode
  $writeElapsedMilliseconds = [int64]$writeEnvelope.elapsedMilliseconds
  if ($writeRunnerExit -ne 0 -or
      [string]$writeEnvelope.schema -cne "boundedProcessV1" -or
      [string]$writeEnvelope.startStage -cne "none" -or
      [int]$writeEnvelope.startCode -ne 0 -or
      [bool]$writeEnvelope.timedOut -or
      [bool]$writeEnvelope.overflow -or
      [bool]$writeEnvelope.pipeFault -or
      -not [bool]$writeEnvelope.cleanupCompleted -or
      -not [bool]$writeEnvelope.jobEmpty -or
      [uint64]$writeEnvelope.jobActiveProcesses -ne 0 -or
      [int]$writeEnvelope.pid -ne 0 -or
      [int64]$writeEnvelope.hardCapMilliseconds -ne 25000 -or
      $writeExit -ne [int]$writeCase.expectedExit -or
      $writeElapsedMilliseconds -gt 25000) {
    throw ("installTransactionBoundedWriteInvocationFailed:" +
      [string]$writeCase.behavior + ":exit=" + $writeExit +
      ":milliseconds=" + $writeElapsedMilliseconds)
  }
  $writeRaw = ([string]$writeEnvelope.stdoutText).TrimEnd("`r","`n")
  if ([string]::IsNullOrWhiteSpace($writeRaw) -or
      $writeRaw.IndexOf("`n", [StringComparison]::Ordinal) -ge 0 -or
      $writeRaw.IndexOf("`r", [StringComparison]::Ordinal) -ge 0) {
    throw "installTransactionBoundedWriteOutputInvalid"
  }
  try { $writeResult = $writeRaw | ConvertFrom-Json } catch {
    throw "installTransactionBoundedWriteOutputInvalid"
  }
  if ($writeExit -eq 0) {
    if (-not [bool]$writeResult.success -or
        [string]$writeResult.code -cne
          "installTransactionPreflightReady") {
      throw "installTransactionBoundedWriteSuccessInvalid"
    }
  } elseif ([bool]$writeResult.success -or
      [string]$writeResult.failedField -cne "installTransaction" -or
      [int]$writeResult.transactionHelperNativeExit -ne 18 -or
      [string]$writeResult.transactionHelperStage -cne
        [string]$writeCase.expectedStage) {
    throw "installTransactionBoundedWriteEvidenceInvalid"
  }
  $pending = Join-Path $writeRoot "pending-install-transaction.json"
  if (Test-Path -LiteralPath $pending) {
    throw "installTransactionBoundedWriteLeftJournal"
  }
  if (-not [bool]$writeCase.rootMayExist -and
      (Test-Path -LiteralPath $writeRoot)) {
    throw "installTransactionBoundedWriteMutatedRoot"
  }
  $boundedResults += [ordered]@{
    name = "transaction-bounded-" + [string]$writeCase.behavior
    passed = $true
  }
}
$boundedAfter = @(Get-ChildItem -LiteralPath $boundedInstallRoot -Recurse -File |
  ForEach-Object {
    [ordered]@{
      relative = $_.FullName.Substring(
        $boundedInstallRoot.TrimEnd('\').Length + 1)
      hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
    }
  } | ConvertTo-Json -Depth 3 -Compress) -join ""
if ($boundedAfter -cne $boundedBefore) {
  throw "installTransactionBoundedInvocationMutatedInstallRoot"
}
$transactionFixtureRoot = Resolve-TransactionFixturePhysicalPath (
  Join-Path $combinationRoot "transaction-fixtures")
New-Item -ItemType Directory -Path $transactionFixtureRoot -Force | Out-Null
$transactionRoot = Join-Path $transactionFixtureRoot "admin-transaction"
$env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
$savedErrorAction = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$preflightOutput = @(& $transactionHelper preflight --test-root $transactionRoot 2>&1)
$ErrorActionPreference = $savedErrorAction
$transactionProbeExit = $LASTEXITCODE
Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS -ErrorAction SilentlyContinue
$transactionElevatedAvailable = $transactionProbeExit -eq 0 -and
  ($preflightOutput -join "") -ceq (
    '{"code":"installTransactionPreflightReady","stage":"finalReadback",' +
    '"recoveryAction":"none"}') -and
  -not (Test-Path -LiteralPath (
    Join-Path $transactionRoot "pending-install-transaction.json"))
$emptyRecoveryRoot = Join-Path $transactionFixtureRoot "empty-admin-residue"
$emptyRecoveryTransactionRoot = Join-Path $emptyRecoveryRoot "Transactions"
New-Item -ItemType Directory -Path $emptyRecoveryRoot | Out-Null
$emptyRecoveryAcl = Get-Acl -LiteralPath $emptyRecoveryRoot
$emptyRecoveryAcl.SetOwner(
  [Security.Principal.SecurityIdentifier]::new(
    [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null))
$emptyRecoveryAvailable = $true
try {
  Set-Acl -LiteralPath $emptyRecoveryRoot -AclObject $emptyRecoveryAcl
} catch {
  $emptyRecoveryAvailable = $false
}
if ($emptyRecoveryAvailable) {
  $emptyRecoveryBeforeAcl = Get-Acl -LiteralPath $emptyRecoveryRoot
  if ($emptyRecoveryBeforeAcl.Owner -notmatch "Administrators$" -or
      $emptyRecoveryBeforeAcl.AreAccessRulesProtected -or
      (Get-Item -LiteralPath $emptyRecoveryRoot).Attributes.ToString().
        Contains("ReparsePoint") -or
      @(Get-ChildItem -LiteralPath $emptyRecoveryRoot -Force).Count -ne 0 -or
      @(Get-Item -LiteralPath $emptyRecoveryRoot -Stream * `
        -ErrorAction SilentlyContinue).Count -ne 0) {
    throw "installTransactionEmptyAdminRootFixtureInvalid"
  }
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $ErrorActionPreference = "Continue"
  $emptyRecoveryOutput = @(
    & $transactionHelper preflight `
      --test-root $emptyRecoveryTransactionRoot 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $emptyRecoveryExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  if ($emptyRecoveryExit -ne 0 -or
      ($emptyRecoveryOutput -join "") -cne (
        '{"code":"installTransactionPreflightReady","stage":"finalReadback",' +
        '"recoveryAction":"recoverEmptyAdminRoot"}')) {
    throw "installTransactionEmptyAdminRootRecoveryFailed"
  }
  $emptyRecoveryAfterAcl = Get-Acl -LiteralPath $emptyRecoveryRoot
  $emptyRecoveryAfterRules = @($emptyRecoveryAfterAcl.Access)
  if (-not $emptyRecoveryAfterAcl.AreAccessRulesProtected -or
      $emptyRecoveryAfterAcl.Owner -notmatch "Administrators$" -or
      @($emptyRecoveryAfterRules | Where-Object {
        $_.IsInherited -or $_.AccessControlType -ne "Allow" -or
        $_.IdentityReference.Value -notmatch
          "(^|\\\\)(SYSTEM|Administrators)$"
      }).Count -ne 0 -or
      $emptyRecoveryAfterRules.Count -ne 2 -or
      -not (Test-Path -LiteralPath $emptyRecoveryTransactionRoot) -or
      (Test-Path -LiteralPath (Join-Path $emptyRecoveryTransactionRoot `
        "pending-install-transaction.json"))) {
    throw "installTransactionEmptyAdminRootRecoveryReadbackFailed"
  }
}
$concurrentRecoveryResults = @()
if ($emptyRecoveryAvailable) {
  $busyRoot = Join-Path $transactionFixtureRoot "race-open-handle"
  New-Item -ItemType Directory -Path $busyRoot | Out-Null
  $busyAcl = Get-Acl -LiteralPath $busyRoot
  $busyAcl.SetOwner(
    [Security.Principal.SecurityIdentifier]::new(
      [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null))
  Set-Acl -LiteralPath $busyRoot -AclObject $busyAcl
  $busySddlBefore = (Get-Acl -LiteralPath $busyRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_TEST_BEHAVIOR = "holdRecoveryHandle"
  $ErrorActionPreference = "Continue"
  $busyOutput = @(
    & $transactionHelper preflight --test-root $busyRoot 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $busyExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR `
    -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  $busyFailure = (($busyOutput -join "") | ConvertFrom-Json)
  $busySddlAfter = (Get-Acl -LiteralPath $busyRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  if ($busyExit -ne 18 -or
      [string]$busyFailure.code -cne "installTransactionUnavailable" -or
      [string]$busyFailure.stage -cne "openHandle" -or
      [string]$busyFailure.nativeCategory -cne "busy" -or
      [int]$busyFailure.nativeCode -ne 32 -or
      [bool]$busyFailure.aclMutationOccurred -or
      [string]$busyFailure.aclRollback -cne "notRequired" -or
      $busySddlAfter -cne $busySddlBefore -or
      @(Get-ChildItem -LiteralPath $busyRoot -Force).Count -ne 0) {
    throw "installTransactionExclusiveOpenBusyGateInvalid"
  }
  $concurrentRecoveryResults += [ordered]@{
    name = "transaction-existing-open-handle-busy-zero-acl-mutation"
    passed = $true
  }
  foreach ($behavior in @("injectResidueChild", "injectResidueAds")) {
    $raceRoot = Join-Path $transactionFixtureRoot ("race-" + $behavior)
    New-Item -ItemType Directory -Path $raceRoot | Out-Null
    $raceAcl = Get-Acl -LiteralPath $raceRoot
    $raceAcl.SetOwner(
      [Security.Principal.SecurityIdentifier]::new(
        [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid,
        $null))
    Set-Acl -LiteralPath $raceRoot -AclObject $raceAcl
    $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
    $env:LIGASE_TRANSACTION_TEST_BEHAVIOR = $behavior
    $ErrorActionPreference = "Continue"
    $raceOutput = @(
      & $transactionHelper preflight --test-root $raceRoot 2>&1)
    $ErrorActionPreference = $savedErrorAction
    $raceExit = $LASTEXITCODE
    Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR `
      -ErrorAction SilentlyContinue
    Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
      -ErrorAction SilentlyContinue
    $inserted = if ($behavior -eq "injectResidueChild") {
      Test-Path -LiteralPath (Join-Path $raceRoot "injected.bin")
    } else {
      $streams = @(Get-Item -LiteralPath $raceRoot -Stream * `
        -ErrorAction SilentlyContinue)
      @($streams | Where-Object { $_.Stream -ne ':$DATA' }).Count -ne 0
    }
    if ($raceExit -eq 0) {
      if ($inserted -or ($raceOutput -join "") -cne (
          '{"code":"installTransactionPreflightReady",' +
          '"stage":"finalReadback",' +
          '"recoveryAction":"recoverEmptyAdminRoot"}')) {
        throw "installTransactionConcurrentResidueAccepted"
      }
    } elseif ($raceExit -ne 18 -or -not $inserted) {
      throw "installTransactionConcurrentResidueGateInvalid"
    }
    $concurrentRecoveryResults += [ordered]@{
      name = "transaction-$behavior-exclusive-gate"
      passed = $true
    }
  }
}
$nonEmptyRecoveryRoot = Join-Path $transactionFixtureRoot "nonempty-admin-residue"
New-Item -ItemType Directory -Path $nonEmptyRecoveryRoot | Out-Null
$nonEmptyAcl = Get-Acl -LiteralPath $nonEmptyRecoveryRoot
$nonEmptyAcl.SetOwner(
  [Security.Principal.SecurityIdentifier]::new(
    [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null))
Set-Content -LiteralPath (Join-Path $nonEmptyRecoveryRoot "unknown.bin") `
  -Value "unchanged" -NoNewline
$nonEmptyBefore = (Get-FileHash -LiteralPath (
  Join-Path $nonEmptyRecoveryRoot "unknown.bin") -Algorithm SHA256).Hash
if ($emptyRecoveryAvailable) {
  Set-Acl -LiteralPath $nonEmptyRecoveryRoot -AclObject $nonEmptyAcl
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $ErrorActionPreference = "Continue"
  $null = @(
    & $transactionHelper preflight --test-root $nonEmptyRecoveryRoot 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $nonEmptyExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  if ($nonEmptyExit -ne 18 -or
      (Get-FileHash -LiteralPath (
        Join-Path $nonEmptyRecoveryRoot "unknown.bin") -Algorithm SHA256).Hash `
        -cne $nonEmptyBefore) {
    throw "installTransactionNonemptyAdminRootAccepted"
  }
}
$wrongOwnerRoot = Join-Path $transactionFixtureRoot "wrong-owner-admin-residue"
New-Item -ItemType Directory -Path $wrongOwnerRoot | Out-Null
$env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
$ErrorActionPreference = "Continue"
$null = @(& $transactionHelper preflight --test-root $wrongOwnerRoot 2>&1)
$ErrorActionPreference = $savedErrorAction
$wrongOwnerExit = $LASTEXITCODE
Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS -ErrorAction SilentlyContinue
if ($wrongOwnerExit -ne 18 -or
    @(Get-ChildItem -LiteralPath $wrongOwnerRoot -Force).Count -ne 0) {
  throw "installTransactionWrongOwnerAdminRootAccepted"
}
$adsRecoveryRoot = Join-Path $transactionFixtureRoot "ads-admin-residue"
New-Item -ItemType Directory -Path $adsRecoveryRoot | Out-Null
$adsAcl = Get-Acl -LiteralPath $adsRecoveryRoot
$adsAcl.SetOwner(
  [Security.Principal.SecurityIdentifier]::new(
    [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null))
Set-Content -LiteralPath "${adsRecoveryRoot}:unknown" `
  -Value "unchanged" -NoNewline
if ($emptyRecoveryAvailable) {
  Set-Acl -LiteralPath $adsRecoveryRoot -AclObject $adsAcl
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $ErrorActionPreference = "Continue"
  $null = @(& $transactionHelper preflight --test-root $adsRecoveryRoot 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $adsExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  if ($adsExit -ne 18 -or
      (Get-Content -LiteralPath "${adsRecoveryRoot}:unknown" -Raw) -cne
        "unchanged") {
    throw "installTransactionAdsAdminRootAccepted"
  }
}
$stageDiagnostics = @()
$neutralBindingDiagnostic = (
  '"bindingReason":"none","bindingRootKind":"none",' +
  '"bindingSegmentCount":0,"bindingPrefixMatched":false,' +
  '"bindingVolumeMatched":false,"bindingFileIdentityMatched":false,' +
  '"aclInspectionReason":"none","emptyRootInspectionReason":"none",')
foreach ($stage in @(
    "resolveProgramData", "rejectReparse", "createSegment", "openHandle",
    "verifyIdentity", "resolveFinalPath", "canonicalRoot", "inspectAcl",
    "readSecurityDescriptor", "descriptorLength", "descriptorCopy",
    "descriptorParse", "buildSecurityDescriptor",
    "compareSecurityDescriptor", "applyAcl", "assertAcl",
    "inspectEmptyRootOwner", "inspectEmptyRootChildren",
    "inspectEmptyRootStreams", "inspectEmptyRootStreamMetadata",
    "createTemp", "atomicReplace",
    "finalReadback", "read", "delete")) {
  $stageRoot = Join-Path $transactionFixtureRoot ("stage-" + $stage)
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_FAILURE_STAGE = $stage
  $ErrorActionPreference = "Continue"
  $stageOutput = @(
    & $transactionHelper preflight --test-root $stageRoot 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $stageExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_TRANSACTION_FAILURE_STAGE -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS -ErrorAction SilentlyContinue
  $stageRaw = $stageOutput -join ""
  $expectedStage = (
    '{"code":"installTransactionUnavailable","stage":"' + $stage +
    '","nativeCategory":"none","nativeCode":0,' +
    $neutralBindingDiagnostic +
    '"aclMutationOccurred":false,"aclRollback":"notRequired"}')
  $exactStage = $stageExit -eq 18 -and $stageRaw -ceq $expectedStage
  $nonElevatedOpen = (
    '{"code":"installTransactionUnavailable","stage":"openHandle",' +
    '"nativeCategory":"none","nativeCode":0,' +
    $neutralBindingDiagnostic +
    '"aclMutationOccurred":false,"aclRollback":"notRequired"}')
  $nonElevatedOpenAccessDenied = (
    '{"code":"installTransactionUnavailable","stage":"openHandle",' +
    '"nativeCategory":"accessDenied","nativeCode":5,' +
    $neutralBindingDiagnostic +
    '"aclMutationOccurred":false,"aclRollback":"notRequired"}')
  $nonElevatedOwner = (
    '{"code":"installTransactionAclInvalid","stage":"createSegment",' +
    '"nativeCategory":"invalidOwner","nativeCode":1307,' +
    $neutralBindingDiagnostic +
    '"aclMutationOccurred":false,"aclRollback":"notRequired"}')
  $blockedByNonElevatedAcl = $stageExit -eq 18 -and
    $stageRaw -in @(
      $nonElevatedOpen, $nonElevatedOpenAccessDenied, $nonElevatedOwner)
  if (-not $exactStage -and -not $blockedByNonElevatedAcl) {
    throw "installTransactionStageDiagnosticFixtureFailed"
  }
  if ($stage -in @("resolveProgramData", "rejectReparse", "createSegment") -and
      (Test-Path -LiteralPath $stageRoot)) {
    throw "installTransactionStageDiagnosticMutatedEarly"
  }
  $stageDiagnostics += [ordered]@{
    name = "transaction-stage-$stage"
    passed = $exactStage
    inconclusive = -not $exactStage
  }
}
$nativeSubstageResults = @()
foreach ($nativeCase in @(
    [ordered]@{
      behavior = "failOpenHandleAccessDenied"
      stage = "openHandle"
      category = "accessDenied"
      code = 5
    },
    [ordered]@{
      behavior = "failVerifyIdentityInvalidHandle"
      stage = "verifyIdentity"
      category = "invalidHandle"
      code = 6
    },
    [ordered]@{
      behavior = "failResolveFinalPathInvalidParameter"
      stage = "resolveFinalPath"
      category = "invalidParameter"
      code = 87
    })) {
  $nativeRoot = Join-Path $transactionFixtureRoot (
    "native-" + [string]$nativeCase.stage)
  New-Item -ItemType Directory -Path $nativeRoot | Out-Null
  $nativeAclBefore = (Get-Acl -LiteralPath $nativeRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_TEST_BEHAVIOR = [string]$nativeCase.behavior
  $ErrorActionPreference = "Continue"
  $nativeOutput = @(
    & $transactionHelper preflight --test-root $nativeRoot 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $nativeExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR `
    -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  $nativeRaw = $nativeOutput -join ""
  $expectedNative = (
    '{"code":"installTransactionUnavailable","stage":"' +
    [string]$nativeCase.stage + '","nativeCategory":"' +
    [string]$nativeCase.category + '","nativeCode":' +
    [string]$nativeCase.code + ',' + $neutralBindingDiagnostic +
    '"aclMutationOccurred":false,' +
    '"aclRollback":"notRequired"}')
  $nativeAclAfter = (Get-Acl -LiteralPath $nativeRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  if ($nativeExit -ne 18 -or $nativeRaw -cne $expectedNative -or
      $nativeAclAfter -cne $nativeAclBefore -or
      @(Get-ChildItem -LiteralPath $nativeRoot -Force).Count -ne 0) {
    throw "installTransactionNativeSubstageDiagnosticInvalid"
  }
  $nativeSubstageResults += [ordered]@{
    name = "transaction-native-" + [string]$nativeCase.stage
    passed = $true
  }
}
$aclInspectionResults = @()
foreach ($aclCase in @(
    [ordered]@{
      behavior = "failCanonicalRootInspection"
      stage = "canonicalRoot"
      reason = "canonicalRootInspectionFailed"
      code = 20001
    },
    [ordered]@{
      behavior = "failReadSecurityDescriptor"
      stage = "readSecurityDescriptor"
      reason = "securityDescriptorReadFailed"
      code = 20002
    },
    [ordered]@{
      behavior = "failDescriptorLength"
      stage = "descriptorLength"
      reason = "descriptorLengthInvalid"
      code = 20003
    },
    [ordered]@{
      behavior = "failDescriptorCopy"
      stage = "descriptorCopy"
      reason = "descriptorCopyFailed"
      code = 20004
    },
    [ordered]@{
      behavior = "failDescriptorParse"
      stage = "descriptorParse"
      reason = "descriptorParseFailed"
      code = 20005
    },
    [ordered]@{
      behavior = "failBuildSecurityDescriptor"
      stage = "buildSecurityDescriptor"
      reason = "expectedDescriptorBuildFailed"
      code = 20006
    },
    [ordered]@{
      behavior = "failCompareSecurityDescriptor"
      stage = "compareSecurityDescriptor"
      reason = "descriptorCompareFailed"
      code = 20007
    })) {
  $aclRoot = Join-Path $transactionFixtureRoot (
    "acl-inspection-" + [string]$aclCase.stage)
  New-Item -ItemType Directory -Path $aclRoot | Out-Null
  $aclBefore = (Get-Acl -LiteralPath $aclRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_TEST_BEHAVIOR = [string]$aclCase.behavior
  $ErrorActionPreference = "Continue"
  $aclOutput = @(
    & $transactionHelper preflight --test-root $aclRoot 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $aclExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR `
    -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  $aclFailure = (($aclOutput -join "") | ConvertFrom-Json)
  $aclAfter = (Get-Acl -LiteralPath $aclRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  if ($aclExit -ne 18 -or
      [string]$aclFailure.code -cne "installTransactionAclInvalid" -or
      [string]$aclFailure.stage -cne [string]$aclCase.stage -or
      [string]$aclFailure.nativeCategory -cne "managedFailure" -or
      [int]$aclFailure.nativeCode -ne [int]$aclCase.code -or
      [string]$aclFailure.aclInspectionReason -cne
        [string]$aclCase.reason -or
      -not [bool]$aclFailure.bindingPrefixMatched -or
      -not [bool]$aclFailure.bindingVolumeMatched -or
      -not [bool]$aclFailure.bindingFileIdentityMatched -or
      [bool]$aclFailure.aclMutationOccurred -or
      [string]$aclFailure.aclRollback -cne "notRequired" -or
      $aclAfter -cne $aclBefore -or
      @(Get-ChildItem -LiteralPath $aclRoot -Force).Count -ne 0) {
    throw "installTransactionAclInspectionDiagnosticInvalid"
  }
  $aclInspectionResults += [ordered]@{
    name = "transaction-acl-inspection-" + [string]$aclCase.stage
    passed = $true
  }
}
$emptyRootInspectionResults = @()
foreach ($emptyRootCase in @(
    [ordered]@{
      behavior = "failEmptyRootOwnerMismatch"
      stage = "inspectEmptyRootOwner"
      reason = "ownerNotAdministrators"
      code = 20008
    },
    [ordered]@{
      behavior = "failEmptyRootChildPresent"
      stage = "inspectEmptyRootChildren"
      reason = "childEntryPresent"
      code = 20009
    },
    [ordered]@{
      behavior = "failEmptyRootNamedAds"
      stage = "inspectEmptyRootStreams"
      reason = "namedDataStreamPresent"
      code = 20010
    },
    [ordered]@{
      behavior = "failEmptyRootStreamMalformed"
      stage = "inspectEmptyRootStreamMetadata"
      reason = "streamMetadataInvalid"
      code = 20011
    },
    [ordered]@{
      behavior = "failEmptyRootStreamOffsetOverflow"
      stage = "inspectEmptyRootStreamMetadata"
      reason = "streamMetadataInvalid"
      code = 20011
    },
    [ordered]@{
      behavior = "failEmptyRootStreamNearMax"
      stage = "inspectEmptyRootStreamMetadata"
      reason = "streamMetadataInvalid"
      code = 20011
    },
    [ordered]@{
      behavior = "failEmptyRootStreamRemainingShort"
      stage = "inspectEmptyRootStreamMetadata"
      reason = "streamMetadataInvalid"
      code = 20011
    },
    [ordered]@{
      behavior = "failEmptyRootStreamZeroProgress"
      stage = "inspectEmptyRootStreamMetadata"
      reason = "streamMetadataInvalid"
      code = 20011
    })) {
  $inspectionRoot = Join-Path $transactionFixtureRoot (
    "empty-root-" + [string]$emptyRootCase.behavior)
  New-Item -ItemType Directory -Path $inspectionRoot | Out-Null
  $aclBefore = (Get-Acl -LiteralPath $inspectionRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_TEST_BEHAVIOR =
    [string]$emptyRootCase.behavior
  $ErrorActionPreference = "Continue"
  $inspectionOutput = @(
    & $transactionHelper preflight --test-root $inspectionRoot 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $inspectionExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR `
    -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  $failure = (($inspectionOutput -join "") | ConvertFrom-Json)
  $aclAfter = (Get-Acl -LiteralPath $inspectionRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  if ($inspectionExit -ne 18 -or
      [string]$failure.code -cne "installTransactionInvalid" -or
      [string]$failure.stage -cne [string]$emptyRootCase.stage -or
      [string]$failure.nativeCategory -cne "managedFailure" -or
      [int]$failure.nativeCode -ne [int]$emptyRootCase.code -or
      [string]$failure.emptyRootInspectionReason -cne
        [string]$emptyRootCase.reason -or
      [bool]$failure.aclMutationOccurred -or
      [string]$failure.aclRollback -cne "notRequired" -or
      $aclAfter -cne $aclBefore -or
      @(Get-ChildItem -LiteralPath $inspectionRoot -Force).Count -ne 0) {
    throw "installTransactionEmptyRootDiagnosticInvalid"
  }
  $emptyRootInspectionResults += [ordered]@{
    name = "transaction-empty-root-" + [string]$emptyRootCase.behavior
    passed = $true
  }
}
$realStreamRoot = Join-Path $transactionFixtureRoot "real-directory-streams"
New-Item -ItemType Directory -Path $realStreamRoot | Out-Null
$streamAclBefore = (Get-Acl -LiteralPath $realStreamRoot).
  GetSecurityDescriptorSddlForm(
    [Security.AccessControl.AccessControlSections]::All)
$env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
$env:LIGASE_TRANSACTION_TEST_BEHAVIOR = "inspectFixtureStreams"
$emptyStreamOutput = @(
  & $transactionHelper inspectEmptyRoot --test-root $realStreamRoot 2>&1)
$emptyStreamExit = $LASTEXITCODE
if ($emptyStreamExit -ne 0 -or
    [string](($emptyStreamOutput -join "") | ConvertFrom-Json).reason -cne
      "empty") {
  throw "installTransactionCanonicalDirectoryStreamRejected"
}
Set-Content -LiteralPath "${realStreamRoot}:named" `
  -Value "preserve" -NoNewline
$ErrorActionPreference = "Continue"
$namedStreamOutput = @(
  & $transactionHelper inspectEmptyRoot --test-root $realStreamRoot 2>&1)
$namedStreamExit = $LASTEXITCODE
$ErrorActionPreference = $savedErrorAction
Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR `
  -ErrorAction SilentlyContinue
Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
  -ErrorAction SilentlyContinue
$namedStreamFailure = (($namedStreamOutput -join "") | ConvertFrom-Json)
$streamAclAfter = (Get-Acl -LiteralPath $realStreamRoot).
  GetSecurityDescriptorSddlForm(
    [Security.AccessControl.AccessControlSections]::All)
if ($namedStreamExit -ne 18 -or
    [string]$namedStreamFailure.stage -cne "inspectEmptyRootStreams" -or
    [int]$namedStreamFailure.nativeCode -ne 20010 -or
    [string]$namedStreamFailure.emptyRootInspectionReason -cne
      "namedDataStreamPresent" -or
    (Get-Content -LiteralPath "${realStreamRoot}:named" -Raw) -cne
      "preserve" -or
    $streamAclAfter -cne $streamAclBefore -or
    @(Get-ChildItem -LiteralPath $realStreamRoot -Force).Count -ne 0) {
  throw "installTransactionNamedDirectoryStreamNotRejected"
}
$emptyRootInspectionResults += [ordered]@{
  name = "transaction-empty-root-real-canonical-stream"
  passed = $true
}
$emptyRootInspectionResults += [ordered]@{
  name = "transaction-empty-root-real-named-ads"
  passed = $true
}
$bindingResults = @()
foreach ($bindingCase in @(
    [ordered]@{
      behavior = "failBindingWrongVolume"
      reason = "volumeMismatch"
      volume = $false
      prefix = $false
      file = $false
    },
    [ordered]@{
      behavior = "failBindingSegmentMismatch"
      reason = "segmentMismatch"
      volume = $true
      prefix = $false
      file = $false
    },
    [ordered]@{
      behavior = "failBindingIdentitySwap"
      reason = "fileIdentityMismatch"
      volume = $true
      prefix = $true
      file = $false
    })) {
  $bindingRoot = Join-Path $transactionFixtureRoot (
    "binding-" + [string]$bindingCase.reason)
  New-Item -ItemType Directory -Path $bindingRoot | Out-Null
  $bindingAclBefore = (Get-Acl -LiteralPath $bindingRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_TEST_BEHAVIOR = [string]$bindingCase.behavior
  $ErrorActionPreference = "Continue"
  $bindingOutput = @(
    & $transactionHelper preflight --test-root $bindingRoot 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $bindingExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR `
    -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  $bindingFailure = (($bindingOutput -join "") | ConvertFrom-Json)
  $bindingAclAfter = (Get-Acl -LiteralPath $bindingRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  if ($bindingExit -ne 18 -or
      [string]$bindingFailure.code -cne "installTransactionInvalid" -or
      [string]$bindingFailure.stage -cne "resolveFinalPath" -or
      [string]$bindingFailure.nativeCategory -cne "bindingMismatch" -or
      [int]$bindingFailure.nativeCode -ne 0 -or
      [string]$bindingFailure.bindingReason -cne
        [string]$bindingCase.reason -or
      [bool]$bindingFailure.bindingVolumeMatched -ne
        [bool]$bindingCase.volume -or
      [bool]$bindingFailure.bindingPrefixMatched -ne
        [bool]$bindingCase.prefix -or
      [bool]$bindingFailure.bindingFileIdentityMatched -ne
        [bool]$bindingCase.file -or
      [int]$bindingFailure.bindingSegmentCount -ne 1 -or
      [string]$bindingFailure.bindingRootKind -notin @(
        "dosDrive", "volumeGuid", "device") -or
      [bool]$bindingFailure.aclMutationOccurred -or
      [string]$bindingFailure.aclRollback -cne "notRequired" -or
      $bindingAclAfter -cne $bindingAclBefore -or
      @(Get-ChildItem -LiteralPath $bindingRoot -Force).Count -ne 0) {
    throw "installTransactionBindingDiagnosticInvalid"
  }
  $bindingResults += [ordered]@{
    name = "transaction-binding-" + [string]$bindingCase.reason
    passed = $true
  }
  $bindingManageRoot = Join-Path $transactionFixtureRoot (
    "binding-manage-" + [string]$bindingCase.reason)
  New-Item -ItemType Directory -Path $bindingManageRoot | Out-Null
  $bindingManageAclBefore = (Get-Acl -LiteralPath $bindingManageRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_TEST_BEHAVIOR = [string]$bindingCase.behavior
  $ErrorActionPreference = "Continue"
  $bindingManageOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File $managementScript `
    -Action PreflightInstallTransaction `
    -InstallDirectory $boundedInstallRoot `
    -InstallTransactionRoot $bindingManageRoot 2>&1)
  $bindingManageExit = $LASTEXITCODE
  $ErrorActionPreference = $savedErrorAction
  Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR `
    -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  $bindingProjection = (
    ($bindingManageOutput[-1] | Out-String).Trim() | ConvertFrom-Json)
  $bindingManageAclAfter = (Get-Acl -LiteralPath $bindingManageRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  if ($bindingManageExit -ne 10 -or
      [string]$bindingProjection.code -cne "installTransactionInvalid" -or
      [string]$bindingProjection.failedField -cne "installTransaction" -or
      [int]$bindingProjection.transactionHelperNativeExit -ne 18 -or
      [string]$bindingProjection.transactionHelperStage -cne
        "resolveFinalPath" -or
      [string]$bindingProjection.transactionHelperNativeCategory -cne
        "bindingMismatch" -or
      [int]$bindingProjection.transactionHelperNativeCode -ne 0 -or
      [string]$bindingProjection.transactionBindingReason -cne
        [string]$bindingCase.reason -or
      [bool]$bindingProjection.transactionBindingVolumeMatched -ne
        [bool]$bindingCase.volume -or
      [bool]$bindingProjection.transactionBindingPrefixMatched -ne
        [bool]$bindingCase.prefix -or
      [bool]$bindingProjection.transactionBindingFileIdentityMatched -ne
        [bool]$bindingCase.file -or
      $bindingManageAclAfter -cne $bindingManageAclBefore -or
      @(Get-ChildItem -LiteralPath $bindingManageRoot -Force).Count -ne 0) {
    throw "installTransactionBindingProjectionInvalid"
  }
  $bindingResults += [ordered]@{
    name = "transaction-binding-projection-" +
      [string]$bindingCase.reason
    passed = $true
  }
}
$systemAdminRoot = Join-Path (
  [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::CommonApplicationData)) "Ligase Host Admin"
$systemBindingAvailable = Test-Path -LiteralPath $systemAdminRoot -PathType Container
$systemBindingReadable = $false
if ($systemBindingAvailable) {
  try {
    $systemBindingAclBefore = (Get-Acl -LiteralPath $systemAdminRoot).
      GetSecurityDescriptorSddlForm(
        [Security.AccessControl.AccessControlSections]::All)
    $systemBindingChildrenBefore = @(
      Get-ChildItem -LiteralPath $systemAdminRoot -Force).Count
    $systemBindingReadable = $true
  } catch [UnauthorizedAccessException] {
    # An exact admin-only root intentionally denies this non-elevated build
    # harness. Keep the real-system observation explicitly inconclusive.
    $systemBindingReadable = $false
  }
}
if ($systemBindingReadable) {
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $systemBindingOutput = @(& $transactionHelper inspectSystemBinding)
  $systemBindingExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  $systemBinding = (($systemBindingOutput -join "") | ConvertFrom-Json)
  $systemBindingAclAfter = (Get-Acl -LiteralPath $systemAdminRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  if ($systemBindingExit -ne 0 -or
      [string]$systemBinding.code -cne
        "installTransactionBindingValid" -or
      [string]$systemBinding.rootKind -notin @(
        "dosDrive", "volumeGuid", "device") -or
      [int]$systemBinding.segmentCount -ne 1 -or
      -not [bool]$systemBinding.prefixMatched -or
      -not [bool]$systemBinding.volumeMatched -or
      -not [bool]$systemBinding.fileIdentityMatched -or
      $systemBindingAclAfter -cne $systemBindingAclBefore -or
      @(Get-ChildItem -LiteralPath $systemAdminRoot -Force).Count -ne
        $systemBindingChildrenBefore) {
    throw "installTransactionSystemBindingReadOnlyInvalid"
  }
}
$bindingResults += [ordered]@{
  name = "transaction-system-programdata-alias-readonly-binding"
  passed = $systemBindingReadable
  inconclusive = -not $systemBindingReadable
}
$systemAclInspectionAvailable = Test-Path -LiteralPath $systemAdminRoot
$systemAclInspectionReadable = $false
if ($systemAclInspectionAvailable) {
  try {
    $systemAclBefore = (Get-Acl -LiteralPath $systemAdminRoot).
      GetSecurityDescriptorSddlForm(
        [Security.AccessControl.AccessControlSections]::All)
    $systemAclChildrenBefore = @(
      Get-ChildItem -LiteralPath $systemAdminRoot -Force).Count
    $systemAclStreamsBefore = @(
      Get-Item -LiteralPath $systemAdminRoot -Stream * `
        -ErrorAction SilentlyContinue).Count
    $systemAclInspectionReadable = $true
  } catch [UnauthorizedAccessException] {
    $systemAclInspectionReadable = $false
  }
}
if ($systemAclInspectionReadable) {
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $ErrorActionPreference = "Continue"
  $systemAclOutput = @(
    & $transactionHelper inspectSystemAcl 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $systemAclExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  $systemAclResult = (($systemAclOutput -join "") | ConvertFrom-Json)
  $systemAclAfter = (Get-Acl -LiteralPath $systemAdminRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  if ($systemAclExit -ne 0 -or
      [string]$systemAclResult.code -cne
        "installTransactionAclInspectionValid" -or
      [bool]$systemAclResult.exact -or
      [string]$systemAclResult.reason -cne "notExact" -or
      $systemAclAfter -cne $systemAclBefore -or
      @(Get-ChildItem -LiteralPath $systemAdminRoot -Force).Count -ne
        $systemAclChildrenBefore -or
      @(Get-Item -LiteralPath $systemAdminRoot -Stream * `
        -ErrorAction SilentlyContinue).Count -ne $systemAclStreamsBefore) {
    throw "installTransactionSystemAclReadOnlyInvalid"
  }
}
$bindingResults += [ordered]@{
  name = "transaction-system-inherited-acl-readonly-not-exact"
  passed = $systemAclInspectionReadable
  inconclusive = -not $systemAclInspectionReadable
}
foreach ($sequenceCase in @(
    [ordered]@{
      behavior = "failSecondBindingWrongVolume"
      reason = "volumeMismatch"
      volume = $false
      prefix = $false
      file = $false
    },
    [ordered]@{
      behavior = "failSecondBindingSegmentMismatch"
      reason = "segmentMismatch"
      volume = $true
      prefix = $false
      file = $false
    },
    [ordered]@{
      behavior = "failSecondBindingIdentitySwap"
      reason = "fileIdentityMismatch"
      volume = $true
      prefix = $true
      file = $false
    })) {
  $sequenceParent = Join-Path $transactionFixtureRoot (
    "binding-sequence-" + [string]$sequenceCase.reason)
  $sequenceFirst = Join-Path $sequenceParent "binding-first"
  $sequenceSecond = Join-Path $sequenceParent "binding-second"
  New-Item -ItemType Directory -Path $sequenceFirst -Force | Out-Null
  New-Item -ItemType Directory -Path $sequenceSecond -Force | Out-Null
  $sequenceAclBefore = (Get-Acl -LiteralPath $sequenceSecond).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_TEST_BEHAVIOR = [string]$sequenceCase.behavior
  $ErrorActionPreference = "Continue"
  $sequenceOutput = @(& $transactionHelper inspectSequentialBinding `
    --test-root $sequenceSecond 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $sequenceExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR `
    -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  $sequenceFailure = (($sequenceOutput -join "") | ConvertFrom-Json)
  if ($sequenceExit -ne 18 -or
      [string]$sequenceFailure.nativeCategory -cne "bindingMismatch" -or
      [string]$sequenceFailure.bindingReason -cne
        [string]$sequenceCase.reason -or
      [bool]$sequenceFailure.bindingVolumeMatched -ne
        [bool]$sequenceCase.volume -or
      [bool]$sequenceFailure.bindingPrefixMatched -ne
        [bool]$sequenceCase.prefix -or
      [bool]$sequenceFailure.bindingFileIdentityMatched -ne
        [bool]$sequenceCase.file -or
      (Get-Acl -LiteralPath $sequenceSecond).
        GetSecurityDescriptorSddlForm(
          [Security.AccessControl.AccessControlSections]::All) -cne
        $sequenceAclBefore -or
      @(Get-ChildItem -LiteralPath $sequenceFirst -Force).Count -ne 0 -or
      @(Get-ChildItem -LiteralPath $sequenceSecond -Force).Count -ne 0) {
    throw "installTransactionSequentialBindingIsolationInvalid"
  }
  $bindingResults += [ordered]@{
    name = "transaction-binding-sequence-" +
      [string]$sequenceCase.reason
    passed = $true
  }
}
$duplicateResults = @()
foreach ($duplicateBehavior in @(
    "emitDuplicateCode",
    "emitDuplicateCodeLastConflicting",
    "emitDuplicateNativeCode",
    "emitDuplicateBindingReason",
    "emitDuplicateBindingRootKind",
    "emitDuplicateBindingSegmentCount",
    "emitDuplicateBindingPrefixMatched",
    "emitDuplicateBindingVolumeMatched",
    "emitDuplicateBindingFileIdentityMatched",
    "emitDuplicateAclInspectionReason",
    "emitDuplicateEmptyRootInspectionReason")) {
  $duplicateRoot = Join-Path $transactionFixtureRoot (
    "duplicate-" + $duplicateBehavior)
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_TEST_BEHAVIOR = $duplicateBehavior
  $ErrorActionPreference = "Continue"
  $duplicateOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File $managementScript `
    -Action PreflightInstallTransaction `
    -InstallDirectory $boundedInstallRoot `
    -InstallTransactionRoot $duplicateRoot 2>&1)
  $duplicateExit = $LASTEXITCODE
  $ErrorActionPreference = $savedErrorAction
  Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR `
    -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  $duplicateProjection = (
    ($duplicateOutput[-1] | Out-String).Trim() | ConvertFrom-Json)
  if ($duplicateExit -ne 10 -or
      [string]$duplicateProjection.code -cne
        "installTransactionInvalid" -or
      [string]$duplicateProjection.failedField -cne
        "installTransaction" -or
      [int]$duplicateProjection.transactionHelperNativeExit -ne 18 -or
      [string]$duplicateProjection.transactionHelperStage -cne "none" -or
      (Test-Path -LiteralPath $duplicateRoot)) {
    throw "installTransactionDuplicatePropertyAccepted"
  }
  $duplicateResults += [ordered]@{
    name = "transaction-duplicate-rejected-" + $duplicateBehavior
    passed = $true
  }
}
$aclTupleResults = @()
foreach ($tupleBehavior in @(
    "emitAclTupleWrongStage",
    "emitAclTupleWrongCode",
    "emitAclTupleWrongReason",
    "emitAclTupleCrossSplice",
    "emitEmptyRootTupleWrongStage",
    "emitEmptyRootTupleWrongCode",
    "emitEmptyRootTupleWrongReason",
    "emitEmptyRootTupleCrossSplice")) {
  $tupleRoot = Join-Path $transactionFixtureRoot (
    "acl-tuple-" + $tupleBehavior)
  $installBefore = @(
    Get-ChildItem -LiteralPath $boundedInstallRoot -Recurse -File |
      ForEach-Object {
        $_.FullName.Substring($boundedInstallRoot.Length + 1) + "|" +
          $_.Length + "|" +
          (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
      } | Sort-Object) -join "`n"
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_TEST_BEHAVIOR = $tupleBehavior
  $ErrorActionPreference = "Continue"
  $tupleOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File $managementScript `
    -Action PreflightInstallTransaction `
    -InstallDirectory $boundedInstallRoot `
    -InstallTransactionRoot $tupleRoot 2>&1)
  $tupleExit = $LASTEXITCODE
  $ErrorActionPreference = $savedErrorAction
  Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR `
    -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  $tupleProjection = (
    ($tupleOutput[-1] | Out-String).Trim() | ConvertFrom-Json)
  $installAfter = @(
    Get-ChildItem -LiteralPath $boundedInstallRoot -Recurse -File |
      ForEach-Object {
        $_.FullName.Substring($boundedInstallRoot.Length + 1) + "|" +
          $_.Length + "|" +
          (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
      } | Sort-Object) -join "`n"
  if ($tupleExit -ne 10 -or
      [string]$tupleProjection.code -cne
        "installTransactionInvalid" -or
      [string]$tupleProjection.failedField -cne
        "installTransaction" -or
      [int]$tupleProjection.transactionHelperNativeExit -ne 18 -or
      [string]$tupleProjection.transactionHelperStage -cne "none" -or
      (Test-Path -LiteralPath $tupleRoot) -or
      $installAfter -cne $installBefore) {
    throw "installTransactionManagedAclTupleAccepted"
  }
  $aclTupleResults += [ordered]@{
    name = "transaction-managed-acl-tuple-rejected-" + $tupleBehavior
    passed = $true
  }
}
$junctionRoot = Join-Path $transactionFixtureRoot "transaction-junction"
$junctionTarget = Join-Path $transactionFixtureRoot "outside-target"
New-Item -ItemType Directory -Path $junctionTarget -Force | Out-Null
$sentinel = Join-Path $junctionTarget "sentinel.txt"
[IO.File]::WriteAllText(
  $sentinel, "unchanged", [Text.UTF8Encoding]::new($false))
$junctionCreated = $false
try {
  New-Item -ItemType Junction -Path $junctionRoot -Target $junctionTarget `
    -ErrorAction Stop | Out-Null
  $junctionCreated = $true
} catch {}
$junctionRejected = $false
$junctionExit = -1
$junctionElapsedMilliseconds = 0
$junctionStage = "none"
$junctionNativeExit = -1
$junctionErrorActionRestored = $false
$junctionValidationEnvironmentRestored = $false
$junctionEvidenceSha = ""
if ($junctionCreated) {
  $junctionPriorValidation = [Environment]::GetEnvironmentVariable(
    "LIGASE_INSTALL_VALIDATION_HARNESS")
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $junctionClock = [Diagnostics.Stopwatch]::StartNew()
  $junctionSavedErrorAction = $ErrorActionPreference
  try {
    $ErrorActionPreference = "Continue"
    $junctionEnvelopeOutput = @(
      & $DotNet $argumentListRunner --bounded-capture 15000 5000 4096 `
        none powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
        -File $managementScript `
        -Action PreflightInstallTransaction `
        -InstallDirectory $boundedInstallRoot `
        -InstallTransactionRoot $junctionRoot)
    $junctionRunnerExit = $LASTEXITCODE
  } finally {
    $ErrorActionPreference = $junctionSavedErrorAction
    if ($null -eq $junctionPriorValidation) {
      Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
        -ErrorAction SilentlyContinue
    } else {
      $env:LIGASE_INSTALL_VALIDATION_HARNESS =
        $junctionPriorValidation
    }
    $junctionClock.Stop()
    $junctionElapsedMilliseconds = [int]$junctionClock.ElapsedMilliseconds
  }
  $junctionErrorActionRestored =
    $ErrorActionPreference -ceq $junctionSavedErrorAction
  $junctionValidationEnvironmentRestored =
    [Environment]::GetEnvironmentVariable(
      "LIGASE_INSTALL_VALIDATION_HARNESS") -ceq
        $junctionPriorValidation
  if (-not $junctionErrorActionRestored -or
      -not $junctionValidationEnvironmentRestored) {
    throw "installTransactionJunctionStateRestoreFailed"
  }
  $junctionEvidenceRoot = Join-Path $OutputRoot "junction-evidence"
  New-Item -ItemType Directory -Path $junctionEvidenceRoot | Out-Null
  $junctionEvidencePath = Join-Path $junctionEvidenceRoot (
    "transaction-junction.first.json")
  if (Test-Path -LiteralPath $junctionEvidencePath) {
    throw "gateEvidenceUnavailable"
  }
  $junctionObserved = $null
  $junctionObservedRaw = if ($junctionEnvelopeOutput.Count -eq 1) {
    [string]$junctionEnvelopeOutput[0]
  } else { "" }
  try {
    if (-not [string]::IsNullOrEmpty($junctionObservedRaw)) {
      $junctionObserved = $junctionObservedRaw | ConvertFrom-Json
    }
  } catch { $junctionObserved = $null }
  function Get-JunctionObservedProperty(
    $Value, [string]$Name, $DefaultValue) {
    if ($null -eq $Value) { return $DefaultValue }
    $properties = @($Value.PSObject.Properties | Where-Object {
      $_.Name -ceq $Name
    })
    if ($properties.Count -ne 1) { return $DefaultValue }
    return $properties[0].Value
  }
  $emptySha = Get-CompatibleSha256 ([byte[]]::new(0))
  $junctionObservation = [ordered]@{
    schema = "boundedJunctionEvidenceV1"
    caseId = "transactionJunction"
    nativeExit = [int]$junctionRunnerExit
    result = "observed"
    stage = "boundedHostProcess"
    startStage = [string](Get-JunctionObservedProperty `
      $junctionObserved "startStage" "none")
    startCode = [int](Get-JunctionObservedProperty `
      $junctionObserved "startCode" 0)
    outputRecordCount = [int]$junctionEnvelopeOutput.Count
    stdoutRawLength = [int64](Get-JunctionObservedProperty `
      $junctionObserved "stdoutRawLength" 0)
    stdoutRawSha = [string](Get-JunctionObservedProperty `
      $junctionObserved "stdoutRawSha" $emptySha)
    stdoutOverflow = [bool](Get-JunctionObservedProperty `
      $junctionObserved "stdoutOverflow" $false)
    stdoutDecoderState = [string](Get-JunctionObservedProperty `
      $junctionObserved "stdoutDecoderState" "invalid")
    stdoutPendingTailLength = [int](Get-JunctionObservedProperty `
      $junctionObserved "stdoutPendingTailLength" 0)
    stderrRawLength = [int64](Get-JunctionObservedProperty `
      $junctionObserved "stderrRawLength" 0)
    stderrRawSha = [string](Get-JunctionObservedProperty `
      $junctionObserved "stderrRawSha" $emptySha)
    stderrOverflow = [bool](Get-JunctionObservedProperty `
      $junctionObserved "stderrOverflow" $false)
    stderrDecoderState = [string](Get-JunctionObservedProperty `
      $junctionObserved "stderrDecoderState" "invalid")
    stderrPendingTailLength = [int](Get-JunctionObservedProperty `
      $junctionObserved "stderrPendingTailLength" 0)
    timedOut = [bool](Get-JunctionObservedProperty `
      $junctionObserved "timedOut" $false)
    cleanupState = if ([bool](Get-JunctionObservedProperty `
      $junctionObserved "cleanupCompleted" $false)) {
      "completed"
    } else { "failed" }
    rootPidZero = [bool]([int](Get-JunctionObservedProperty `
      $junctionObserved "pid" -1) -eq 0)
    descendantPidZero = [bool](
      [int](Get-JunctionObservedProperty $junctionObserved "pid" -1) -eq 0 -and
      [uint64](Get-JunctionObservedProperty `
        $junctionObserved "jobActiveProcesses" ([uint32]::MaxValue)) -eq 0)
    jobActiveProcesses = [uint64](Get-JunctionObservedProperty `
      $junctionObserved "jobActiveProcesses" ([uint32]::MaxValue))
    elapsedMilliseconds = [int]$junctionElapsedMilliseconds
    runBudgetMilliseconds = 15000
    cleanupReserveMilliseconds = 5000
    hardCapMilliseconds = 20000
    environmentRestored = [bool](
      $junctionErrorActionRestored -and
      $junctionValidationEnvironmentRestored)
  }
  $junctionEvidenceRaw = $junctionObservation | ConvertTo-Json -Compress
  $junctionEvidenceExpectedNames = @(
    "schema","caseId","nativeExit","result","stage","startStage","startCode",
    "outputRecordCount",
    "stdoutRawLength","stdoutRawSha","stdoutOverflow","stdoutDecoderState",
    "stdoutPendingTailLength","stderrRawLength","stderrRawSha",
    "stderrOverflow","stderrDecoderState","stderrPendingTailLength",
    "timedOut","cleanupState","rootPidZero","descendantPidZero",
    "jobActiveProcesses","elapsedMilliseconds","runBudgetMilliseconds",
    "cleanupReserveMilliseconds","hardCapMilliseconds",
    "environmentRestored")
  $junctionEvidenceNames = @([regex]::Matches(
    $junctionEvidenceRaw, '"(?<name>[A-Za-z][A-Za-z0-9]*)"\s*:') |
    ForEach-Object { $_.Groups["name"].Value })
  if ($junctionEvidenceNames.Count -ne $junctionEvidenceExpectedNames.Count -or
      @($junctionEvidenceNames | Sort-Object -Unique).Count -ne
        $junctionEvidenceExpectedNames.Count -or
      @($junctionEvidenceNames | Where-Object {
        $junctionEvidenceExpectedNames -cnotcontains $_
      }).Count -ne 0 -or
      [string]$junctionObservation.startStage -cnotin @(
        "none","executableResolve","pipe","job","attribute","create","assign","resume",
        "managedHandoff") -or
      [int]$junctionObservation.startCode -lt 0) {
    throw "gateEvidenceUnavailable"
  }
  $junctionEvidenceTemp = Join-Path $junctionEvidenceRoot (
    "." + [guid]::NewGuid().ToString("N") + ".tmp")
  try {
    $junctionEvidenceBytes = [Text.UTF8Encoding]::new(
      $false, $true).GetBytes($junctionEvidenceRaw)
    $junctionEvidenceStream = [IO.FileStream]::new(
      $junctionEvidenceTemp, [IO.FileMode]::CreateNew,
      [IO.FileAccess]::Write, [IO.FileShare]::None, 4096,
      [IO.FileOptions]::WriteThrough)
    try {
      $junctionEvidenceStream.Write(
        $junctionEvidenceBytes, 0, $junctionEvidenceBytes.Length)
      $junctionEvidenceStream.Flush($true)
    } finally { $junctionEvidenceStream.Dispose() }
    [IO.File]::Move($junctionEvidenceTemp, $junctionEvidencePath)
    $junctionEvidenceReadback = [IO.File]::ReadAllBytes(
      $junctionEvidencePath)
    if ([Convert]::ToBase64String($junctionEvidenceReadback) -cne
        [Convert]::ToBase64String($junctionEvidenceBytes)) {
      throw "gateEvidenceUnavailable"
    }
    $junctionEvidenceSha = Get-CompatibleSha256 $junctionEvidenceReadback
  } catch {
    if (Test-Path -LiteralPath $junctionEvidenceTemp) {
      Remove-Item -LiteralPath $junctionEvidenceTemp -Force `
        -ErrorAction SilentlyContinue
    }
    throw "gateEvidenceUnavailable"
  }
  if ($junctionRunnerExit -ne 0 -or
      $junctionEnvelopeOutput.Count -ne 1 -or
      $junctionClock.Elapsed.TotalSeconds -gt 26) {
    throw "installTransactionJunctionInvocationUnbounded"
  }
  $junctionEnvelopeRaw = [string]$junctionEnvelopeOutput[0]
  $junctionEnvelopeNames = @(
    [regex]::Matches(
      $junctionEnvelopeRaw,
      '"(?<name>[A-Za-z][A-Za-z0-9]*)"\s*:') |
      ForEach-Object { $_.Groups["name"].Value })
  $junctionExpectedEnvelopeNames = @(
    "schema", "startStage", "startCode", "exitCode",
    "timedOut", "overflow", "pipeFault",
    "killAttempted", "cleanupCompleted", "jobEmpty",
    "jobActiveProcesses", "pid",
    "elapsedMilliseconds", "stdoutRawLength", "stderrRawLength",
    "hardCapMilliseconds", "stdoutOverflow", "stderrOverflow",
    "stdoutRawSha", "stderrRawSha", "stdoutDecoderState",
    "stderrDecoderState", "stdoutPendingTailLength",
    "stderrPendingTailLength", "stdoutText", "stderrText")
  if ($junctionEnvelopeNames.Count -ne
      $junctionExpectedEnvelopeNames.Count -or
      @($junctionEnvelopeNames | Sort-Object -Unique).Count -ne
        $junctionExpectedEnvelopeNames.Count -or
      @($junctionEnvelopeNames | Where-Object {
        $junctionExpectedEnvelopeNames -cnotcontains $_
      }).Count -ne 0 -or
      $junctionEnvelopeRaw -cnotmatch
        '"jobActiveProcesses":0(?:,|})' -or
      $junctionEnvelopeRaw -cnotmatch
        '"hardCapMilliseconds":20000(?:,|})' -or
      $junctionEnvelopeRaw -cnotmatch
        '"stdoutOverflow":false(?:,|})' -or
      $junctionEnvelopeRaw -cnotmatch
        '"stderrOverflow":false(?:,|})') {
    throw "installTransactionJunctionInvocationUnbounded"
  }
  try {
    $junctionEnvelope = $junctionEnvelopeRaw | ConvertFrom-Json
  } catch {
    throw "installTransactionJunctionInvocationUnbounded"
  }
  if ([string]$junctionEnvelope.schema -cne "boundedProcessV1" -or
      [string]$junctionEnvelope.startStage -cne "none" -or
      [int]$junctionEnvelope.startCode -ne 0 -or
      [bool]$junctionEnvelope.timedOut -or
      [bool]$junctionEnvelope.overflow -or
      [bool]$junctionEnvelope.pipeFault -or
      [bool]$junctionEnvelope.stdoutOverflow -or
      [bool]$junctionEnvelope.stderrOverflow -or
      -not [bool]$junctionEnvelope.cleanupCompleted -or
      -not [bool]$junctionEnvelope.jobEmpty -or
      [uint64]$junctionEnvelope.jobActiveProcesses -ne 0 -or
      [int64]$junctionEnvelope.hardCapMilliseconds -ne 20000 -or
      [int]$junctionEnvelope.pid -ne 0) {
    throw "installTransactionJunctionInvocationUnbounded"
  }
  $junctionExit = [int]$junctionEnvelope.exitCode
  if ([string]$junctionEnvelope.stdoutDecoderState -cne "closed" -or
      [string]$junctionEnvelope.stderrDecoderState -cne "closed" -or
      [int]$junctionEnvelope.stdoutPendingTailLength -ne 0 -or
      [int]$junctionEnvelope.stderrPendingTailLength -ne 0) {
    throw "installTransactionJunctionProjectionInvalid"
  }
  $junctionStdout = [string]$junctionEnvelope.stdoutText
  $junctionStderr = [string]$junctionEnvelope.stderrText
  if (-not [string]::IsNullOrEmpty($junctionStderr)) {
    throw "installTransactionJunctionProjectionInvalid"
  }
  $junctionRecords = @($junctionStdout -split "\r?\n" | Where-Object {
    -not [string]::IsNullOrWhiteSpace($_)
  })
  if ($junctionRecords.Count -ne 1) {
    throw "installTransactionJunctionProjectionInvalid"
  }
  $junctionRaw = [string]$junctionRecords[0]
  if ([Text.Encoding]::UTF8.GetByteCount($junctionRaw) -gt 4096) {
    throw "installTransactionJunctionOutputOverflow"
  }
  $junctionNames = @(
    [regex]::Matches(
      $junctionRaw,
      '"(?<name>[A-Za-z][A-Za-z0-9]*)"\s*:') |
      ForEach-Object { $_.Groups["name"].Value })
  $junctionExpectedNames = @(
    "code", "success", "failedField", "transactionHelperNativeExit",
    "transactionHelperStage", "transactionHelperNativeCategory",
    "transactionHelperNativeCode", "transactionBindingReason",
    "transactionBindingRootKind", "transactionBindingSegmentCount",
    "transactionBindingPrefixMatched", "transactionBindingVolumeMatched",
    "transactionBindingFileIdentityMatched")
  if ($junctionNames.Count -ne $junctionExpectedNames.Count -or
      @($junctionNames | Sort-Object -Unique).Count -ne
        $junctionExpectedNames.Count -or
      @($junctionNames | Where-Object {
        $junctionExpectedNames -cnotcontains $_
      }).Count -ne 0) {
    throw "installTransactionJunctionProjectionInvalid"
  }
  try {
    $junctionProjection = $junctionRaw | ConvertFrom-Json
  } catch {
    throw "installTransactionJunctionProjectionInvalid"
  }
  $junctionStage = [string]$junctionProjection.transactionHelperStage
  $junctionNativeExit =
    [int]$junctionProjection.transactionHelperNativeExit
  $junctionRejected = $junctionExit -eq 10 -and
    [string]$junctionProjection.code -ceq "installTransactionInvalid" -and
    -not [bool]$junctionProjection.success -and
    [string]$junctionProjection.failedField -ceq "installTransaction" -and
    [int]$junctionProjection.transactionHelperNativeExit -eq 18 -and
    [string]$junctionProjection.transactionHelperStage -ceq "rejectReparse" -and
    [string]$junctionProjection.transactionHelperNativeCategory -ceq "none" -and
    [int]$junctionProjection.transactionHelperNativeCode -eq 0 -and
    [IO.File]::ReadAllText($sentinel) -ceq "unchanged" -and
    -not (Test-Path -LiteralPath (
      Join-Path $junctionTarget "pending-install-transaction.json"))
}
if ($junctionCreated -and -not $junctionRejected) {
  throw "installTransactionJunctionFollowed"
}
$shortcutResults = @(
  $boundedResults
  $stageDiagnostics
  $nativeSubstageResults
  $aclInspectionResults
  $emptyRootInspectionResults
  $bindingResults
  $duplicateResults
  $aclTupleResults
  [ordered]@{ name = "current-to-all-owned-selected"; passed = $true },
  [ordered]@{ name = "all-users-desktop-unselected"; passed = $true },
  [ordered]@{ name = "nonowned-current-preserved"; passed = $true },
  [ordered]@{ name = "nonowned-common-conflict-fail-closed"; passed = $true },
  [ordered]@{ name = "legacy-owned-start-conflict-zero-mutation"; passed = $true },
  [ordered]@{ name = "desktop-conflict-zero-mutation"; passed = $true },
  [ordered]@{ name = "shortcut-write-failure-exact-rollback"; passed = $true },
  [ordered]@{ name = "shortcut-readback-failure-exact-rollback"; passed = $true },
  [ordered]@{
    name = "admin-only-handle-transaction-boundary"
    passed = $transactionElevatedAvailable
    inconclusive = -not $transactionElevatedAvailable
  },
  [ordered]@{
    name = "transaction-junction-rejected-zero-external-mutation"
    passed = $junctionRejected
    inconclusive = -not $junctionCreated
    exitCode = $junctionExit
    helperNativeExit = $junctionNativeExit
    helperStage = $junctionStage
    elapsedMilliseconds = $junctionElapsedMilliseconds
    evidenceSha = $junctionEvidenceSha
    errorActionRestored = $junctionErrorActionRestored
    validationEnvironmentRestored =
      $junctionValidationEnvironmentRestored
  },
  [ordered]@{
    name = "transaction-empty-admin-root-recovered-by-file-identity"
    passed = $emptyRecoveryAvailable
    inconclusive = -not $emptyRecoveryAvailable
  },
  [ordered]@{
    name = "transaction-nonempty-admin-root-rejected-without-mutation"
    passed = $emptyRecoveryAvailable
    inconclusive = -not $emptyRecoveryAvailable
  },
  [ordered]@{
    name = "transaction-wrong-owner-admin-root-rejected"
    passed = $true
  },
  [ordered]@{
    name = "transaction-ads-admin-root-rejected"
    passed = $emptyRecoveryAvailable
    inconclusive = -not $emptyRecoveryAvailable
  }
  $concurrentRecoveryResults)

$failureFlowResults = @(
  Invoke-FailureFlowHarness `
    -FailureMode "helperFailure" `
    -ExpectedCode "installationIntegrationFailed" `
    -ExpectedRollback "completed" `
    -ExpectedFailedField "artifacts"
  Invoke-FailureFlowHarness `
    -FailureMode "migrationFailure" `
    -ExpectedCode "dataRootMigrationReadbackFailed" `
    -ExpectedRollback "completed" `
    -ExpectedFailedField "dataRoot"
  Invoke-FailureFlowHarness `
    -FailureMode "integrationFailure" `
    -ExpectedCode "installationFinalReadbackFailed" `
    -ExpectedRollback "completed" `
    -ExpectedFailedField "startMenu"
  Invoke-FailureFlowHarness `
    -FailureMode "rollbackFailure" `
    -ExpectedCode "installationActionFailed" `
    -ExpectedRollback "failed" `
    -ExpectedFailedField "dataRoot"
  Invoke-FailureFlowHarness `
    -FailureMode "silentProvisional" `
    -ExpectedCode "installationFinalReadbackRequired" `
    -ExpectedRollback "notRequired" `
    -ExpectedFailedField "none"
)

$installerProcessRoot = Join-Path $root "virtual-display-installer-process"
$installerProcessEvidence = Join-Path $root "virtual-display-installer-evidence"
New-Item -ItemType Directory -Path $installerProcessRoot | Out-Null
New-Item -ItemType Directory -Path $installerProcessEvidence | Out-Null
$installerProcessMock = Join-Path $installerProcessRoot (
  "virtual-display-installer-mock.cmd")
@'
@echo off
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="success" echo LIGASE_VDISPLAY_V1^|stage=completed^|nativeExit=0^|removeExit=0^|removeCount=0&exit /b 0
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="exit20" echo LIGASE_VDISPLAY_V1^|stage=toolValidation^|nativeExit=2&exit /b 20
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="exit21" echo LIGASE_VDISPLAY_V1^|stage=certificateRoot^|nativeExit=5&exit /b 21
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="exit22" echo LIGASE_VDISPLAY_V1^|stage=certificatePublisher^|nativeExit=5&exit /b 22
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="exit23" echo LIGASE_VDISPLAY_V1^|stage=deviceCreate^|nativeExit=87^|removeExit=0^|removeCount=0&exit /b 23
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="exit24" echo LIGASE_VDISPLAY_V1^|stage=driverPackageInstall^|nativeExit=5^|removeExit=0^|removeCount=0&exit /b 24
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="malformed" echo not-json&exit /b 0
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="extra" echo LIGASE_VDISPLAY_V1^|stage=completed^|nativeExit=0^|removeExit=0^|removeCount=0&echo extra&exit /b 0
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="cross" echo LIGASE_VDISPLAY_V1^|stage=deviceCreate^|nativeExit=5^|removeExit=0^|removeCount=0&exit /b 24
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="invalidUtf8" powershell.exe -NoProfile -Command "[Console]::OpenStandardOutput().WriteByte(255)"&exit /b 0
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="stdoutOverflow" for /L %%i in (1,1,80) do @echo 0123456789
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="stderrOverflow" for /L %%i in (1,1,80) do @echo 0123456789 1>&2
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="stdoutOverflowTree" start "" /b powershell.exe -NoProfile -Command "Start-Sleep -Seconds 3; [IO.File]::WriteAllText($env:LIGASE_VDISPLAY_SENTINEL,'late')" & for /L %%i in (1,1,80) do @echo 0123456789
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="stderrOverflowTree" start "" /b powershell.exe -NoProfile -Command "Start-Sleep -Seconds 3; [IO.File]::WriteAllText($env:LIGASE_VDISPLAY_SENTINEL,'late')" & for /L %%i in (1,1,80) do @echo 0123456789 1>&2
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="hang" powershell.exe -NoProfile -Command "Start-Sleep -Seconds 30"
if "%LIGASE_VDISPLAY_PROCESS_BEHAVIOR%"=="tree" start "" /b powershell.exe -NoProfile -Command "Start-Sleep -Seconds 3; [IO.File]::WriteAllText($env:LIGASE_VDISPLAY_SENTINEL,'late')" & powershell.exe -NoProfile -Command "Start-Sleep -Seconds 30"
exit /b 0
'@ | Set-Content -LiteralPath $installerProcessMock -Encoding ASCII
function Assert-InstallerProcessCase(
  [Collections.IDictionary]$Case,
  [string[]]$ObservedKeys
) {
  $schema = if ($Case.Contains("schema") -and $Case["schema"] -is [string]) {
    [string]$Case["schema"]
  } else { "none" }
  $expected = @(switch -CaseSensitive ($schema) {
    "directV1" { @("schema", "name", "code", "success") }
    "faultV1" { @("schema", "name", "behavior", "fault", "code", "success") }
    "retainedV1" {
      @("schema", "name", "behavior", "fault", "code", "success",
        "externalCleanup")
    }
    default { @() }
  })
  $actual = if ($PSBoundParameters.ContainsKey("ObservedKeys")) {
    @($ObservedKeys)
  } else {
    @($Case.Keys | ForEach-Object { [string]$_ })
  }
  $actualSet = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
  $keysUnique = $true
  foreach ($key in $actual) {
    if (-not $actualSet.Add($key)) { $keysUnique = $false }
  }
  $expectedSet = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
  foreach ($key in $expected) {
    if (-not $expectedSet.Add([string]$key)) {
      throw "virtualDisplayInstallerCaseSchemaDefinitionInvalid"
    }
  }
  $valid = $expected.Count -gt 0 -and
    $keysUnique -and $actualSet.Count -eq $expectedSet.Count
  if ($valid) {
    foreach ($key in $actualSet) {
      if (-not $expectedSet.Contains($key)) { $valid = $false; break }
    }
  }
  $valid = $valid -and
    $Case["name"] -is [string] -and $Case["name"].Length -gt 0 -and
    $Case["code"] -is [string] -and $Case["code"].Length -gt 0 -and
    $Case["success"] -is [bool]
  if ($schema -ceq "faultV1" -or $schema -ceq "retainedV1") {
    $valid = $valid -and $Case["behavior"] -is [string] -and
      $Case["behavior"].Length -gt 0 -and $Case["fault"] -is [string] -and
      $Case["fault"].Length -gt 0
  }
  if ($schema -ceq "retainedV1") {
    $valid = $valid -and $Case["externalCleanup"] -is [bool] -and
      [bool]$Case["externalCleanup"]
  }
  if (-not $valid) {
    $safeName = if ($Case.Contains("name") -and $Case["name"] -is [string] -and
        [string]$Case["name"] -match '^[A-Za-z0-9]{1,64}$') {
      [string]$Case["name"]
    } else { "unknown" }
    $safeEvidence = [ordered]@{
      schema = 1
      result = "failed"
      code = "virtualDisplayInstallerCaseSchemaInvalid"
      caseName = $safeName
      declaredSchema = $(if ($schema -ceq "directV1" -or
          $schema -ceq "faultV1" -or $schema -ceq "retainedV1") {
        $schema
      } else { "unknown" })
      propertyCount = $actual.Count
    }
    [IO.File]::WriteAllText(
      (Join-Path $installerProcessEvidence "case-schema-$safeName.json"),
      ($safeEvidence | ConvertTo-Json -Compress),
      [Text.UTF8Encoding]::new($false))
    throw "virtualDisplayInstallerCaseSchemaInvalid:$safeName"
  }
  return $Case
}

$installerProcessCaseSchemaResults = @()
foreach ($invalidCase in @(
    [ordered]@{
      name = "missing"
      value = @{ schema = "directV1"; name = "missing";
        success = $false }
    },
    [ordered]@{
      name = "unknown"
      value = @{ schema = "directV1"; name = "unknown";
        code = "virtualDisplayInstallerOutputInvalid"; success = $false;
        unexpected = "rejected" }
    },
    [ordered]@{
      name = "type"
      value = @{ schema = "directV1"; name = "type";
        code = 18; success = $false }
    },
    [ordered]@{
      name = "duplicateSame"
      value = @{ schema = "directV1"; name = "duplicateSame";
        code = "virtualDisplayInstallerOutputInvalid"; success = $false }
      observedKeys = @("schema", "name", "code", "code", "success")
    },
    [ordered]@{
      name = "duplicateConflict"
      value = @{ schema = "directV1"; name = "duplicateConflict";
        code = "virtualDisplayInstallerOutputInvalid"; success = $false }
      observedKeys = @("schema", "name", "code", "success", "code")
    },
    [ordered]@{
      name = "schemaUpper"
      value = @{ schema = "DIRECTV1"; name = "schemaUpper";
        code = "virtualDisplayInstallerOutputInvalid"; success = $false }
    },
    [ordered]@{
      name = "schemaMixed"
      value = @{ schema = "DirectV1"; name = "schemaMixed";
        code = "virtualDisplayInstallerOutputInvalid"; success = $false }
    },
    [ordered]@{
      name = "nameCase"
      value = @{ schema = "directV1"; Name = "nameCase";
        code = "virtualDisplayInstallerOutputInvalid"; success = $false }
    },
    [ordered]@{
      name = "codeCase"
      value = @{ schema = "directV1"; name = "codeCase";
        Code = "virtualDisplayInstallerOutputInvalid"; success = $false }
    },
    [ordered]@{
      name = "externalCleanupCase"
      value = @{ schema = "retainedV1"; name = "externalCleanupCase";
        behavior = "hang"; fault = "secondaryWait";
        code = "virtualDisplayInstallerCleanupFailed"; success = $false;
        ExternalCleanup = $true }
    },
    [ordered]@{
      name = "unsafeSchema"
      value = @{ schema = "C:\Users\private\secret-token";
        name = "unsafeSchema"; code = "virtualDisplayInstallerOutputInvalid";
        success = $false }
    })) {
  $rejected = $false
  try {
    if ($invalidCase.Contains("observedKeys")) {
      Assert-InstallerProcessCase $invalidCase.value $invalidCase.observedKeys |
        Out-Null
    } else {
      Assert-InstallerProcessCase $invalidCase.value | Out-Null
    }
  } catch {
    $rejected =
      $_.Exception.Message.StartsWith(
        "virtualDisplayInstallerCaseSchemaInvalid:",
        [StringComparison]::Ordinal)
  }
  $invalidEvidence = Join-Path $installerProcessEvidence (
    "case-schema-$($invalidCase.name).json")
  if (-not $rejected -or
      -not (Test-Path -LiteralPath $invalidEvidence -PathType Leaf)) {
    throw "virtualDisplayInstallerCaseSchemaNegativeFailed:$($invalidCase.name)"
  }
  $invalidReadback = [IO.File]::ReadAllText($invalidEvidence) |
    ConvertFrom-Json
  if ([string]$invalidReadback.result -cne "failed" -or
      [string]$invalidReadback.code -cne
        "virtualDisplayInstallerCaseSchemaInvalid" -or
      [string]$invalidReadback.declaredSchema -notin @(
        "directV1", "faultV1", "retainedV1", "unknown") -or
      ([string]$invalidReadback.declaredSchema -cne "directV1" -and
       [string]$invalidReadback.declaredSchema -cne "faultV1" -and
       [string]$invalidReadback.declaredSchema -cne "retainedV1" -and
       [string]$invalidReadback.declaredSchema -cne "unknown")) {
    throw "virtualDisplayInstallerCaseSchemaEvidenceInvalid:$($invalidCase.name)"
  }
  $invalidEvidenceBytes = [IO.File]::ReadAllText($invalidEvidence)
  if ($invalidEvidenceBytes.IndexOf(
      "C:\Users\", [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
      $invalidEvidenceBytes.IndexOf(
      "secret-token", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    throw "virtualDisplayInstallerCaseSchemaEvidenceLeak:$($invalidCase.name)"
  }
  $installerProcessCaseSchemaResults += [ordered]@{
    name = [string]$invalidCase.name
    rejected = $true
    evidenceCode = [string]$invalidReadback.code
  }
}

$installerProcessCases = @(
  @{ schema = "directV1"; name = "success";
     code = "virtualDisplayInstalled"; success = $true },
  @{ schema = "directV1"; name = "exit20";
     code = "virtualDisplayInstallerToolUnavailable"; success = $false },
  @{ schema = "directV1"; name = "exit21";
     code = "virtualDisplayCertificateRootFailed"; success = $false },
  @{ schema = "directV1"; name = "exit22";
     code = "virtualDisplayCertificatePublisherFailed"; success = $false },
  @{ schema = "directV1"; name = "exit23";
     code = "virtualDisplayDeviceCreateFailed"; success = $false },
  @{ schema = "directV1"; name = "exit24";
     code = "virtualDisplayDriverPackageInstallFailed"; success = $false },
  @{ schema = "directV1"; name = "malformed";
     code = "virtualDisplayInstallerOutputInvalid"; success = $false },
  @{ schema = "directV1"; name = "extra";
     code = "virtualDisplayInstallerOutputInvalid"; success = $false },
  @{ schema = "directV1"; name = "cross";
     code = "virtualDisplayInstallerOutputInvalid"; success = $false },
  @{ schema = "directV1"; name = "invalidUtf8";
     code = "virtualDisplayInstallerOutputInvalid"; success = $false },
  @{ schema = "directV1"; name = "stdoutOverflow";
     code = "virtualDisplayInstallerOutputOverflow"; success = $false },
  @{ schema = "directV1"; name = "stderrOverflow";
     code = "virtualDisplayInstallerOutputOverflow"; success = $false },
  @{ schema = "directV1"; name = "stdoutOverflowTree";
     code = "virtualDisplayInstallerOutputOverflow"; success = $false },
  @{ schema = "directV1"; name = "stderrOverflowTree";
     code = "virtualDisplayInstallerOutputOverflow"; success = $false },
  @{ schema = "directV1"; name = "hang";
     code = "virtualDisplayInstallerTimeout"; success = $false },
  @{ schema = "directV1"; name = "tree";
     code = "virtualDisplayInstallerTimeout"; success = $false },
  @{ schema = "faultV1"; name = "assignFault"; behavior = "hang";
     fault = "assign"; code = "virtualDisplayInstallerCleanupFailed";
     success = $false },
  @{ schema = "faultV1"; name = "resumeFault"; behavior = "hang";
     fault = "resume"; code = "virtualDisplayInstallerCleanupFailed";
     success = $false },
  @{ schema = "faultV1"; name = "unassignedTerminateFault"; behavior = "hang";
     fault = "startTerminate"; code = "virtualDisplayInstallerCleanupFailed";
     success = $false },
  @{ schema = "faultV1"; name = "unassignedWaitFault"; behavior = "hang";
     fault = "startWait"; code = "virtualDisplayInstallerCleanupFailed";
     success = $false },
  @{ schema = "faultV1"; name = "assignedJobTerminateFault"; behavior = "hang";
     fault = "jobTerminate"; code = "virtualDisplayInstallerCleanupFailed";
     success = $false },
  @{ schema = "faultV1"; name = "assignedJobAccountingFault"; behavior = "hang";
     fault = "jobAccounting"; code = "virtualDisplayInstallerCleanupFailed";
     success = $false },
  @{ schema = "faultV1"; name = "secondaryContainment"; behavior = "hang";
     fault = "retain"; code = "virtualDisplayInstallerCleanupFailed";
     success = $false },
  @{ schema = "retainedV1"; name = "secondaryTerminateFailure";
     behavior = "hang"; fault = "secondaryTerminate";
     code = "virtualDisplayInstallerCleanupFailed"; success = $false;
     externalCleanup = $true },
  @{ schema = "retainedV1"; name = "secondaryWaitFailure";
     behavior = "hang"; fault = "secondaryWait";
     code = "virtualDisplayInstallerCleanupFailed"; success = $false;
     externalCleanup = $true },
  @{ schema = "retainedV1"; name = "secondaryAccountingFailure";
     behavior = "hang"; fault = "secondaryAccounting";
     code = "virtualDisplayInstallerCleanupFailed"; success = $false;
     externalCleanup = $true },
  @{ schema = "faultV1"; name = "readFault"; behavior = "hang";
     fault = "read"; code = "virtualDisplayInstallerOutputUnavailable";
     success = $false },
  @{ schema = "faultV1"; name = "terminateFault"; behavior = "hang";
     fault = "terminate"; code = "virtualDisplayInstallerCleanupFailed";
     success = $false },
  @{ schema = "faultV1"; name = "waitFault"; behavior = "hang";
     fault = "wait"; code = "virtualDisplayInstallerCleanupFailed";
     success = $false },
  @{ schema = "faultV1"; name = "pipeFault"; behavior = "hang";
     fault = "pipe"; code = "virtualDisplayInstallerCleanupFailed";
     success = $false })
if ($InstallerProcessCaseFilter -cne "all") {
  $installerProcessCases = @($installerProcessCases | Where-Object {
    [string]$_["name"] -ceq $InstallerProcessCaseFilter
  })
  if ($installerProcessCases.Count -ne 1) {
    throw "virtualDisplayInstallerProcessCaseFilterInvalid"
  }
}
function Get-SecondaryRunnerRawParseState(
  [Parameter(Mandatory)][string]$Raw,
  [Parameter(Mandatory)][string[]]$ExpectedNames
) {
  $trimmed = $Raw.Trim()
  $rawNames = @([regex]::Matches(
    $trimmed, '(?<!\\)"(?<name>[A-Za-z][A-Za-z0-9]*)"\s*:') |
    ForEach-Object { $_.Groups["name"].Value })
  if (-not $trimmed.StartsWith("{", [StringComparison]::Ordinal) -or
      -not $trimmed.EndsWith("}", [StringComparison]::Ordinal)) {
    return "trailing"
  }
  if (@($rawNames | Sort-Object -Unique).Count -ne $rawNames.Count) {
    return "duplicate"
  }
  if (@($rawNames | Where-Object {
        $ExpectedNames -cnotcontains $_
      }).Count -ne 0) {
    return "unknownProperty"
  }
  if ($rawNames.Count -ne $ExpectedNames.Count -or
      @($ExpectedNames | Where-Object {
        $rawNames -cnotcontains $_
      }).Count -ne 0) {
    return "missing"
  }
  return "convert"
}
$secondaryRunnerParserExpectedNames = @(
  "schema","startStage","startCode","exitCode","timedOut","overflow",
  "pipeFault","killAttempted","cleanupCompleted","jobEmpty",
  "jobActiveProcesses","pid","elapsedMilliseconds",
  "hardCapMilliseconds","stdoutOverflow","stderrOverflow",
  "stdoutRawLength","stderrRawLength","stdoutRawSha","stderrRawSha",
  "stdoutDecoderState","stderrDecoderState",
  "stdoutPendingTailLength","stderrPendingTailLength",
  "stdoutText","stderrText")
$secondaryRunnerParserProbe = [ordered]@{}
foreach ($parserName in $secondaryRunnerParserExpectedNames) {
  $secondaryRunnerParserProbe[$parserName] = if ($parserName -ceq "schema") {
    "boundedProcessV1"
  } else { 0 }
}
$secondaryRunnerParserProbeRaw =
  $secondaryRunnerParserProbe | ConvertTo-Json -Compress
$secondaryRunnerParserPrefix = '"schema":"boundedProcessV1"'
$secondaryRunnerParserDuplicateSame =
  $secondaryRunnerParserProbeRaw.Replace(
    $secondaryRunnerParserPrefix,
    $secondaryRunnerParserPrefix + "," + $secondaryRunnerParserPrefix)
$secondaryRunnerParserDuplicateConflict =
  $secondaryRunnerParserProbeRaw.Replace(
    $secondaryRunnerParserPrefix,
    '"schema":"boundedProcessV1","schema":"conflict"')
$secondaryRunnerParserSameState = Get-SecondaryRunnerRawParseState `
  $secondaryRunnerParserDuplicateSame $secondaryRunnerParserExpectedNames
$secondaryRunnerParserConflictState = Get-SecondaryRunnerRawParseState `
  $secondaryRunnerParserDuplicateConflict $secondaryRunnerParserExpectedNames
$secondaryRunnerParserTrailingState = Get-SecondaryRunnerRawParseState `
  ($secondaryRunnerParserProbeRaw + " trailing") `
  $secondaryRunnerParserExpectedNames
if ($secondaryRunnerParserSameState -cne "duplicate" -or
    $secondaryRunnerParserConflictState -cne "duplicate" -or
    $secondaryRunnerParserTrailingState -cne "trailing") {
  throw "secondaryRunnerRawParserSelfTestFailed"
}
function Test-SecondaryContainmentCaseEvidence([string]$Raw) {
  try { $value = $Raw | ConvertFrom-Json } catch { return $false }
  $expected = @(
    "schema","caseId","declaredSchema","behavior","fault",
    "runnerExit","startStage","startCode","childExitCode",
    "parseState","outputRecordCount",
    "stdoutRawLength","stdoutRawSha","stdoutOverflow",
    "stdoutDecoderState","stdoutPendingTailLength",
    "stderrRawLength","stderrRawSha","stderrOverflow",
    "stderrDecoderState","stderrPendingTailLength",
    "timedOut","runnerCleanupState","rootPidZero","descendantPidZero",
    "jobActiveProcesses","runnerElapsedMilliseconds",
    "runBudgetMilliseconds","cleanupReserveMilliseconds",
    "externalCleanupReserveMilliseconds",
    "outerElapsedMilliseconds","outerHardCapMilliseconds","code","success",
    "firstCleanupProven","authorityRetained","retainedPid",
    "secondaryContainmentAttempted","secondaryContainmentCompleted",
    "cleanupState",
    "externalCleanupAttempted","externalCleanupCompleted",
    "finalPidZero","sentinelExists","residueCount","environmentRestored")
  $actual = @($value.PSObject.Properties.Name)
  return (
    $actual.Count -eq $expected.Count -and
    @($actual | Sort-Object -Unique).Count -eq $expected.Count -and
    @($actual | Where-Object { $expected -cnotcontains $_ }).Count -eq 0 -and
    [string]$value.schema -ceq "secondaryContainmentCaseEvidenceV1" -and
    [string]$value.caseId -cin @(
      "secondaryContainment","secondaryTerminateFailure",
      "secondaryWaitFailure","secondaryAccountingFailure") -and
    [string]$value.declaredSchema -cin @("faultV1","retainedV1") -and
    [string]$value.behavior -ceq "hang" -and
    [string]$value.fault -cin @(
      "retain","secondaryTerminate","secondaryWait","secondaryAccounting") -and
    $value.runnerExit -is [int] -and
    [string]$value.startStage -cin @(
      "none","executableResolve","pipe","job","attribute","create","assign",
      "resume","managedHandoff") -and
    $value.startCode -is [int] -and
    $value.childExitCode -is [int] -and
    [string]$value.parseState -cin @(
      "valid","absent","multiple","malformed","typeInvalid",
      "unknownProperty","missing","duplicate","trailing") -and
    $value.outputRecordCount -is [int] -and
    [int64]$value.stdoutRawLength -ge 0 -and
    [string]$value.stdoutRawSha -cmatch '^[0-9A-F]{64}$' -and
    $value.stdoutOverflow -is [bool] -and
    [string]$value.stdoutDecoderState -cin @(
      "closed","pendingTail","invalid") -and
    [int]$value.stdoutPendingTailLength -ge 0 -and
    [int64]$value.stderrRawLength -ge 0 -and
    [string]$value.stderrRawSha -cmatch '^[0-9A-F]{64}$' -and
    $value.stderrOverflow -is [bool] -and
    [string]$value.stderrDecoderState -cin @(
      "closed","pendingTail","invalid") -and
    [int]$value.stderrPendingTailLength -ge 0 -and
    $value.timedOut -is [bool] -and
    [string]$value.runnerCleanupState -cin @("completed","failed") -and
    $value.rootPidZero -is [bool] -and
    $value.descendantPidZero -is [bool] -and
    [int64]$value.jobActiveProcesses -ge 0 -and
    [int64]$value.runnerElapsedMilliseconds -ge 0 -and
    [int64]$value.runBudgetMilliseconds -eq 7000 -and
    [int64]$value.cleanupReserveMilliseconds -eq 3000 -and
    [int64]$value.externalCleanupReserveMilliseconds -eq 500 -and
    [int64]$value.outerElapsedMilliseconds -ge 0 -and
    [int64]$value.outerHardCapMilliseconds -eq 10500 -and
    $value.success -is [bool] -and
    $value.firstCleanupProven -is [bool] -and
    $value.authorityRetained -is [bool] -and
    $value.retainedPid -is [int] -and
    $value.secondaryContainmentAttempted -is [bool] -and
    $value.secondaryContainmentCompleted -is [bool] -and
    [string]$value.cleanupState -cin @("completed","failed","unavailable") -and
    $value.externalCleanupAttempted -is [bool] -and
    $value.externalCleanupCompleted -is [bool] -and
    $value.finalPidZero -is [bool] -and
    $value.sentinelExists -is [bool] -and
    [int]$value.residueCount -ge 0 -and
    $value.environmentRestored -is [bool])
}
function Write-SecondaryContainmentCaseEvidence(
  [Parameter(Mandatory)][Collections.IDictionary]$Observation
) {
  $path = Join-Path $installerProcessEvidence (
    "secondary-$([string]$Observation.caseId).first.json")
  if (Test-Path -LiteralPath $path) { throw "gateEvidenceUnavailable" }
  $raw = $Observation | ConvertTo-Json -Compress
  if (-not (Test-SecondaryContainmentCaseEvidence $raw)) {
    throw "gateEvidenceUnavailable"
  }
  $temp = Join-Path $installerProcessEvidence (
    "." + [guid]::NewGuid().ToString("N") + ".tmp")
  try {
    $bytes = [Text.UTF8Encoding]::new($false, $true).GetBytes($raw)
    $stream = [IO.FileStream]::new(
      $temp, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
      [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
    try {
      $stream.Write($bytes, 0, $bytes.Length)
      $stream.Flush($true)
    } finally { $stream.Dispose() }
    [IO.File]::Move($temp, $path)
    $readback = [IO.File]::ReadAllBytes($path)
    $readbackRaw = [Text.UTF8Encoding]::new(
      $false, $true).GetString($readback)
    if (-not (Test-SecondaryContainmentCaseEvidence $readbackRaw) -or
        -not [Linq.Enumerable]::SequenceEqual(
          [byte[]]$bytes, [byte[]]$readback)) {
      throw "gateEvidenceUnavailable"
    }
    return Get-CompatibleSha256 $readback
  } catch {
    if (Test-Path -LiteralPath $temp) {
      Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
    }
    throw "gateEvidenceUnavailable"
  }
}
$installerProcessResults = @()
foreach ($untrustedCase in $installerProcessCases) {
  $case = Assert-InstallerProcessCase $untrustedCase
  $caseBehavior = if ([string]$case["schema"] -ceq "directV1") {
    [string]$case["name"]
  } else { [string]$case["behavior"] }
  $caseFault = if ([string]$case["schema"] -ceq "directV1") {
    "none"
  } else { [string]$case["fault"] }
  $caseUsesExternalCleanup =
    [string]$case["schema"] -ceq "retainedV1"
  $caseIsSecondary =
    [string]$case["name"] -cin @(
      "secondaryContainment","secondaryTerminateFailure",
      "secondaryWaitFailure","secondaryAccountingFailure")
  $sentinel = Join-Path $installerProcessRoot "$($case.name).sentinel"
  $clock = [Diagnostics.Stopwatch]::StartNew()
  $outerClock = [Diagnostics.Stopwatch]::StartNew()
  $validationEnvironmentNames = @(
    "LIGASE_INSTALL_VALIDATION_HARNESS",
    "LIGASE_VIRTUAL_DISPLAY_PROCESS_VALIDATION_ROOT",
    "LIGASE_VIRTUAL_DISPLAY_PROCESS_TIMEOUT_MS",
    "LIGASE_VDISPLAY_PROCESS_BEHAVIOR",
    "LIGASE_VIRTUAL_DISPLAY_PROCESS_CLEANUP_FAULT",
    "LIGASE_VDISPLAY_SENTINEL")
  $savedValidationEnvironment = @{}
  foreach ($environmentName in $validationEnvironmentNames) {
    $savedValidationEnvironment[$environmentName] =
      [Environment]::GetEnvironmentVariable(
        $environmentName, [EnvironmentVariableTarget]::Process)
  }
  try {
    $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
    $env:LIGASE_VIRTUAL_DISPLAY_PROCESS_VALIDATION_ROOT =
      $installerProcessRoot
    $env:LIGASE_VIRTUAL_DISPLAY_PROCESS_TIMEOUT_MS = "1000"
    $env:LIGASE_VDISPLAY_PROCESS_BEHAVIOR = $caseBehavior
    $env:LIGASE_VIRTUAL_DISPLAY_PROCESS_CLEANUP_FAULT = $caseFault
    $env:LIGASE_VDISPLAY_SENTINEL = $sentinel
    if ($caseIsSecondary) {
      $raw = @(& $DotNet $argumentListRunner --bounded-capture `
        7000 3000 8192 none powershell.exe `
        -NoLogo -NoProfile -ExecutionPolicy Bypass `
        -File $managementScript `
        -Action ValidateVirtualDisplayInstallerProcess `
        -InstallDirectory $installerProcessRoot `
        -ValidationRoot $installerProcessRoot)
      $nativeExit = $LASTEXITCODE
    } else {
      $raw = & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
        -File $managementScript `
        -Action ValidateVirtualDisplayInstallerProcess `
        -InstallDirectory $installerProcessRoot `
        -ValidationRoot $installerProcessRoot
      $nativeExit = $LASTEXITCODE
    }
  } finally {
    foreach ($environmentName in $validationEnvironmentNames) {
      [Environment]::SetEnvironmentVariable(
        $environmentName, $savedValidationEnvironment[$environmentName],
        [EnvironmentVariableTarget]::Process)
    }
  }
  $clock.Stop()
  $environmentRestored = @($validationEnvironmentNames | Where-Object {
    [Environment]::GetEnvironmentVariable(
      $_, [EnvironmentVariableTarget]::Process) -cne
      $savedValidationEnvironment[$_]
  }).Count -eq 0
  if (-not $caseIsSecondary) { Start-Sleep -Milliseconds 3500 }
  $projection = [ordered]@{
    code = "virtualDisplayInstallerOutputInvalid"
    success = $false
    installStage = "notStarted"
    childExitCode = -1
    stdoutSha256 =
      "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"
    stderrSha256 =
      "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"
    cleanupState = "unavailable"
    cleanupPid = 0
    firstCleanupProven = $false
    authorityRetained = $false
    secondaryContainmentAttempted = $false
    secondaryContainmentCompleted = $false
  }
  $parseState = "absent"
  $outputRecordCount = 0
  $runnerEnvelope = $null
  if ($caseIsSecondary) {
    if (@($raw).Count -eq 0) {
      $parseState = "absent"
    } elseif (@($raw).Count -ne 1) {
      $parseState = "multiple"
      $outputRecordCount = @($raw).Count
    } else {
      $runnerRaw = ([string]$raw[0]).Trim()
      $runnerExpectedNames = $secondaryRunnerParserExpectedNames
      # The runner envelope is a generated flat object. Scan its raw authority
      # before ConvertFrom-Json so PowerShell's last-wins duplicate folding can
      # never become schema authority. Escaped property-like text belongs only
      # to stdoutText/stderrText and is excluded by the negative lookbehind.
      $runnerRawParseState =
        Get-SecondaryRunnerRawParseState $runnerRaw $runnerExpectedNames
      if ($runnerRawParseState -cne "convert") {
        $parseState = $runnerRawParseState
      } else {
        try { $runnerEnvelope = $runnerRaw | ConvertFrom-Json } catch {
          $parseState = "malformed"
        }
      }
      if ($null -ne $runnerEnvelope) {
        $runnerNames = @($runnerEnvelope.PSObject.Properties.Name)
        if ($runnerNames.Count -ne $runnerExpectedNames.Count -or
            @($runnerNames | Where-Object {
              $runnerExpectedNames -cnotcontains $_
            }).Count -ne 0) {
          $parseState = "typeInvalid"
        } elseif ([string]$runnerEnvelope.schema -cne "boundedProcessV1" -or
            $runnerEnvelope.startCode -isnot [int] -or
            $runnerEnvelope.exitCode -isnot [int] -or
            $runnerEnvelope.timedOut -isnot [bool] -or
            $runnerEnvelope.cleanupCompleted -isnot [bool] -or
            $runnerEnvelope.pid -isnot [int]) {
          $parseState = "typeInvalid"
        } else {
          $outputRaw = ([string]$runnerEnvelope.stdoutText).TrimEnd("`r","`n")
          if ([string]::IsNullOrEmpty($outputRaw)) {
            $parseState = "absent"
          } elseif ($outputRaw.IndexOf(
              "`n", [StringComparison]::Ordinal) -ge 0 -or
              $outputRaw.IndexOf(
              "`r", [StringComparison]::Ordinal) -ge 0) {
            $parseState = "multiple"
            $outputRecordCount = @($outputRaw -split '\r?\n').Count
          } else {
            $outputNames = @([regex]::Matches(
              $outputRaw, '"(?<name>[A-Za-z][A-Za-z0-9]*)"\s*:') |
              ForEach-Object { $_.Groups["name"].Value })
            $outputExpectedNames = @(
              "code","success","installStage","childExitCode","removeExitCode",
              "removeCount",
              "stdoutSha256","stderrSha256","cleanupState","cleanupPid",
              "firstCleanupProven","authorityRetained",
              "secondaryContainmentAttempted",
              "secondaryContainmentCompleted")
            if (@($outputNames | Sort-Object -Unique).Count -ne
                $outputNames.Count) {
              $parseState = "duplicate"
            } elseif ($outputNames.Count -ne $outputExpectedNames.Count -or
                @($outputNames | Where-Object {
                  $outputExpectedNames -cnotcontains $_
                }).Count -ne 0) {
              $parseState = "unknownProperty"
            } else {
              try { $candidateProjection = $outputRaw | ConvertFrom-Json }
              catch { $parseState = "malformed" }
              if ($null -ne $candidateProjection) {
                if ($candidateProjection.code -isnot [string] -or
                    $candidateProjection.success -isnot [bool] -or
                    $candidateProjection.childExitCode -isnot [int] -or
                    $candidateProjection.removeExitCode -isnot [int] -or
                    $candidateProjection.removeCount -isnot [int] -or
                    $candidateProjection.cleanupPid -isnot [int] -or
                    $candidateProjection.firstCleanupProven -isnot [bool] -or
                    $candidateProjection.authorityRetained -isnot [bool] -or
                    $candidateProjection.secondaryContainmentAttempted -isnot [bool] -or
                    $candidateProjection.secondaryContainmentCompleted -isnot [bool]) {
                  $parseState = "typeInvalid"
                } else {
                  $projection = $candidateProjection
                  $parseState = "valid"
                  $outputRecordCount = 1
                }
              }
            }
          }
        }
      }
    }
  } else {
    if ($nativeExit -ne 0 -or @($raw).Count -ne 1) {
      throw "virtualDisplayInstallerProcessFixtureFailed:$($case.name)"
    }
    $projection = [string]$raw | ConvertFrom-Json
    $parseState = "valid"
    $outputRecordCount = 1
  }
  $expectedSuccess = [bool]$case.success
  $externalCleanupAttempted = $false
  $externalCleanupCompleted = $false
  $externalCleanupFailed = $false
  $observedCleanupPid = [int]$projection.cleanupPid
  $runnerElapsedMilliseconds = if ($null -ne $runnerEnvelope) {
    [int64]$runnerEnvelope.elapsedMilliseconds
  } else { [int64]$clock.ElapsedMilliseconds }
  $remainingCleanupMilliseconds = [Math]::Max(
    0, 10500 - [int]$outerClock.ElapsedMilliseconds)
  if ($caseUsesExternalCleanup) {
    $retainedProjectionValid =
      [string]$projection.cleanupState -ceq "failed" -and
      $observedCleanupPid -gt 0 -and
      -not [bool]$projection.firstCleanupProven -and
      [bool]$projection.authorityRetained -and
      [bool]$projection.secondaryContainmentAttempted -and
      -not [bool]$projection.secondaryContainmentCompleted
    if ($retainedProjectionValid) {
      $externalCleanupAttempted = $true
      $retainedProcess =
        Get-Process -Id $observedCleanupPid -ErrorAction SilentlyContinue
      if ($null -ne $retainedProcess) {
        $externalCleanupClock = [Diagnostics.Stopwatch]::StartNew()
        try {
          $killer = [Diagnostics.Process]::Start(
            (Join-Path $env:SystemRoot "System32\taskkill.exe"),
            "/PID $observedCleanupPid /T /F")
          if ($remainingCleanupMilliseconds -le 0 -or
              $null -eq $killer -or
              -not $killer.WaitForExit($remainingCleanupMilliseconds) -or
              $killer.ExitCode -ne 0 -or
              -not $retainedProcess.WaitForExit(
                [Math]::Max(0,
                  10500 - [int]$outerClock.ElapsedMilliseconds))) {
            $externalCleanupFailed = $true
          }
          if ($null -ne $killer) { $killer.Dispose() }
        } catch {
          $externalCleanupFailed = $true
        } finally {
          $retainedProcess.Dispose()
        }
      }
      $externalCleanupCompleted =
        $null -eq (Get-Process -Id $observedCleanupPid -ErrorAction SilentlyContinue)
      if (-not $externalCleanupCompleted) { $externalCleanupFailed = $true }
    }
  }
  $sentinelExists = Test-Path -LiteralPath $sentinel
  $finalPidZero = if ($observedCleanupPid -eq 0) {
    $true
  } else { $externalCleanupCompleted }
  $outerClock.Stop()
  if ($caseIsSecondary) {
    $runnerCleanupCompleted =
      $null -ne $runnerEnvelope -and
      [bool]$runnerEnvelope.cleanupCompleted
    $runnerRootPidZero =
      $null -ne $runnerEnvelope -and [int]$runnerEnvelope.pid -eq 0
    $runnerJobActiveProcesses = if ($null -ne $runnerEnvelope) {
      [uint64]$runnerEnvelope.jobActiveProcesses
    } else { [uint64][uint32]::MaxValue }
    $secondaryEvidence = [ordered]@{
      schema = "secondaryContainmentCaseEvidenceV1"
      caseId = [string]$case.name
      declaredSchema = [string]$case.schema
      behavior = $caseBehavior
      fault = $caseFault
      runnerExit = [int]$nativeExit
      startStage = if ($null -ne $runnerEnvelope) {
        [string]$runnerEnvelope.startStage
      } else { "none" }
      startCode = if ($null -ne $runnerEnvelope) {
        [int]$runnerEnvelope.startCode
      } else { 0 }
      childExitCode = if ($null -ne $runnerEnvelope) {
        [int]$runnerEnvelope.exitCode
      } else { -1 }
      parseState = $parseState
      outputRecordCount = [int]$outputRecordCount
      stdoutRawLength = if ($null -ne $runnerEnvelope) {
        [int64]$runnerEnvelope.stdoutRawLength
      } else { 0 }
      stdoutRawSha = if ($null -ne $runnerEnvelope) {
        [string]$runnerEnvelope.stdoutRawSha
      } else {
        "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"
      }
      stdoutOverflow = if ($null -ne $runnerEnvelope) {
        [bool]$runnerEnvelope.stdoutOverflow
      } else { $false }
      stdoutDecoderState = if ($null -ne $runnerEnvelope) {
        [string]$runnerEnvelope.stdoutDecoderState
      } else { "closed" }
      stdoutPendingTailLength = if ($null -ne $runnerEnvelope) {
        [int]$runnerEnvelope.stdoutPendingTailLength
      } else { 0 }
      stderrRawLength = if ($null -ne $runnerEnvelope) {
        [int64]$runnerEnvelope.stderrRawLength
      } else { 0 }
      stderrRawSha = if ($null -ne $runnerEnvelope) {
        [string]$runnerEnvelope.stderrRawSha
      } else {
        "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"
      }
      stderrOverflow = if ($null -ne $runnerEnvelope) {
        [bool]$runnerEnvelope.stderrOverflow
      } else { $false }
      stderrDecoderState = if ($null -ne $runnerEnvelope) {
        [string]$runnerEnvelope.stderrDecoderState
      } else { "closed" }
      stderrPendingTailLength = if ($null -ne $runnerEnvelope) {
        [int]$runnerEnvelope.stderrPendingTailLength
      } else { 0 }
      timedOut = if ($null -ne $runnerEnvelope) {
        [bool]$runnerEnvelope.timedOut
      } else { $false }
      runnerCleanupState = if ($runnerCleanupCompleted) {
        "completed"
      } else { "failed" }
      rootPidZero = $runnerRootPidZero
      descendantPidZero =
        $runnerRootPidZero -and $runnerJobActiveProcesses -eq 0
      jobActiveProcesses = $runnerJobActiveProcesses
      runnerElapsedMilliseconds = $runnerElapsedMilliseconds
      runBudgetMilliseconds = [int64]7000
      cleanupReserveMilliseconds = [int64]3000
      externalCleanupReserveMilliseconds = [int64]500
      outerElapsedMilliseconds = [int64]$outerClock.ElapsedMilliseconds
      outerHardCapMilliseconds = [int64]10500
      code = [string]$projection.code
      success = [bool]$projection.success
      firstCleanupProven = [bool]$projection.firstCleanupProven
      authorityRetained = [bool]$projection.authorityRetained
      retainedPid = $observedCleanupPid
      secondaryContainmentAttempted =
        [bool]$projection.secondaryContainmentAttempted
      secondaryContainmentCompleted =
        [bool]$projection.secondaryContainmentCompleted
      cleanupState = [string]$projection.cleanupState
      externalCleanupAttempted = $externalCleanupAttempted
      externalCleanupCompleted = $externalCleanupCompleted
      finalPidZero = $finalPidZero
      sentinelExists = $sentinelExists
      residueCount = [int]$(if ($sentinelExists) { 1 } else { 0 })
      environmentRestored = $environmentRestored
    }
    $secondaryEvidenceSha =
      Write-SecondaryContainmentCaseEvidence $secondaryEvidence
    # SECONDARY_ASSERTIONS_BEGIN
    $secondaryFailure = if (
        (-not $caseUsesExternalCleanup -and $nativeExit -ne 0) -or
        ($caseUsesExternalCleanup -and $nativeExit -notin @(0,93))) {
        "runnerExit"
      }
      elseif ($parseState -cne "valid") { "parseState" }
      elseif ($outputRecordCount -ne 1) { "recordCount" }
      elseif ($null -eq $runnerEnvelope -or
          [string]$runnerEnvelope.startStage -cne "none" -or
          [int]$runnerEnvelope.startCode -ne 0) { "runnerStart" }
      elseif ([bool]$runnerEnvelope.timedOut -or
          [bool]$runnerEnvelope.overflow -or
          [bool]$runnerEnvelope.pipeFault) { "runnerOutput" }
      elseif ([int64]$runnerEnvelope.hardCapMilliseconds -ne 10000 -or
          $runnerElapsedMilliseconds -gt 10500) { "runBudget" }
      elseif (-not $caseUsesExternalCleanup -and (
          -not $runnerCleanupCompleted -or
          -not $runnerRootPidZero -or
          $runnerJobActiveProcesses -ne 0)) { "runnerCleanup" }
      elseif ($outerClock.ElapsedMilliseconds -gt 10500) { "hardCap" }
      elseif (-not $environmentRestored) { "environment" }
      elseif ([string]$projection.code -cne [string]$case.code -or
          [bool]$projection.success -ne $expectedSuccess) { "semanticTuple" }
      elseif (-not $caseUsesExternalCleanup -and (
          [string]$projection.cleanupState -cne "completed" -or
          [int]$projection.cleanupPid -ne 0 -or
          [bool]$projection.firstCleanupProven -or
          -not [bool]$projection.authorityRetained -or
          -not [bool]$projection.secondaryContainmentAttempted -or
          -not [bool]$projection.secondaryContainmentCompleted)) {
        "primaryContainmentAuthority"
      }
      elseif ($caseUsesExternalCleanup -and
          (-not $retainedProjectionValid -or
           -not $externalCleanupAttempted -or
           -not $externalCleanupCompleted -or
           $externalCleanupFailed)) { "externalCleanup" }
      elseif ($sentinelExists -or -not $finalPidZero) { "residue" }
      else { "" }
    if (-not [string]::IsNullOrEmpty($secondaryFailure)) {
      throw ("virtualDisplayInstallerSecondaryCaseFailed caseId=" +
        [string]$case.name + " evidenceSha=" + $secondaryEvidenceSha)
    }
    # SECONDARY_ASSERTIONS_END
  }
  if (-not $caseIsSecondary -and
      $caseUsesExternalCleanup -and -not $retainedProjectionValid) {
    throw "virtualDisplayInstallerRetainedProjectionFailed:$($case.name)"
  }
  if (-not $caseIsSecondary -and
      $caseUsesExternalCleanup -and $externalCleanupFailed) {
    throw "virtualDisplayInstallerExternalCleanupFailed:$($case.name)"
  }
  if (-not $caseIsSecondary -and
      $case.name -ceq "secondaryContainment" -and (
      [bool]$projection.firstCleanupProven -or
      -not [bool]$projection.authorityRetained -or
      -not [bool]$projection.secondaryContainmentAttempted -or
      -not [bool]$projection.secondaryContainmentCompleted -or
      [int]$projection.cleanupPid -ne 0)) {
    throw "virtualDisplayInstallerSecondaryContainmentNotReached"
  }
  if (-not $caseIsSecondary -and (
      [string]$projection.code -cne [string]$case.code -or
      [bool]$projection.success -ne $expectedSuccess -or
      (-not $caseUsesExternalCleanup -and
        [int]$projection.cleanupPid -ne 0) -or
      $clock.ElapsedMilliseconds -gt 7000 -or
      $sentinelExists -or
      -not $environmentRestored)) {
    throw "virtualDisplayInstallerProcessAssertionFailed:$($case.name)"
  }
  $evidence = [ordered]@{
    name = [string]$case.name
    code = [string]$projection.code
    installStage = [string]$projection.installStage
    childExitCode = [int]$projection.childExitCode
    stdoutSha256 = [string]$projection.stdoutSha256
    stderrSha256 = [string]$projection.stderrSha256
    cleanupState = [string]$projection.cleanupState
    retainedPid = $observedCleanupPid
    firstCleanupProven = [bool]$projection.firstCleanupProven
    authorityRetained = [bool]$projection.authorityRetained
    secondaryContainmentAttempted =
      [bool]$projection.secondaryContainmentAttempted
    secondaryContainmentCompleted =
      [bool]$projection.secondaryContainmentCompleted
    externalCleanupAttempted = $externalCleanupAttempted
    externalCleanupCompleted = $externalCleanupCompleted
    finalPidZero = $finalPidZero
    elapsedMilliseconds = [int64]$clock.ElapsedMilliseconds
    sentinelExists = $sentinelExists
  }
  $evidencePath = Join-Path $installerProcessEvidence "$($case.name).json"
  [IO.File]::WriteAllText(
    $evidencePath, ($evidence | ConvertTo-Json -Compress),
    [Text.UTF8Encoding]::new($false))
  $installerProcessResults += $evidence
}
if ($StopAfterInstallerProcessCases) {
  [ordered]@{
    code = "virtualDisplayInstallerProcessFocusedPassed"
    caseCount = $installerProcessResults.Count
    cases = $installerProcessResults
  } | ConvertTo-Json -Depth 5 -Compress
  exit 0
}

$markerBehaviorRoot = Join-Path $root "virtual-display-marker-behavior"
$markerEvidenceRoot = Join-Path $root "virtual-display-marker-evidence"
New-Item -ItemType Directory -Path $markerBehaviorRoot | Out-Null
New-Item -ItemType Directory -Path $markerEvidenceRoot | Out-Null
function Invoke-MarkerBehaviorFixture(
    [string]$Name,
    [string]$FailureStage,
    [bool]$PriorMarker,
    [bool]$DependentDevice = $false,
    [bool]$ForceMarkerChanged = $false,
    [string]$CompensationFailure = "",
    [string]$ExpectedCode = "virtualDisplayMarkerCommitFailed") {
  $caseRoot = Join-Path $markerBehaviorRoot $Name
  New-Item -ItemType Directory -Path $caseRoot | Out-Null
  $marker = Join-Path $caseRoot ".ligase-driver-ownership.json"
  $sentinel = Join-Path $caseRoot "owned-cert.sentinel"
  $oldBytes = [Text.UTF8Encoding]::new($false).GetBytes(
    '{"schemaVersion":1,"certificateThumbprint":"OLD","certificateStores":[]}')
  if ($PriorMarker) { [IO.File]::WriteAllBytes($marker, $oldBytes) }
  [IO.File]::WriteAllText($sentinel, "owned", [Text.UTF8Encoding]::new($false))
  try {
    $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
    $env:LIGASE_VIRTUAL_DISPLAY_VALIDATION_ROOT = $caseRoot
    $env:LIGASE_VIRTUAL_DISPLAY_MARKER_FAILURE_STAGE = $FailureStage
    $env:LIGASE_VIRTUAL_DISPLAY_DEPENDENT_DEVICE =
      $(if ($DependentDevice) { "1" } else { "0" })
    $env:LIGASE_VIRTUAL_DISPLAY_FORCE_MARKER_CHANGED =
      $(if ($ForceMarkerChanged) { "1" } else { "0" })
    $env:LIGASE_VIRTUAL_DISPLAY_COMPENSATION_FAILURE = $CompensationFailure
    $raw = & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
      -File $managementScript `
      -Action ValidateVirtualDisplayMarkerTransaction `
      -InstallDirectory $caseRoot `
      -ValidationRoot $caseRoot
    $nativeExit = $LASTEXITCODE
  } finally {
    Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS -ErrorAction SilentlyContinue
    Remove-Item Env:\LIGASE_VIRTUAL_DISPLAY_VALIDATION_ROOT -ErrorAction SilentlyContinue
    Remove-Item Env:\LIGASE_VIRTUAL_DISPLAY_MARKER_FAILURE_STAGE -ErrorAction SilentlyContinue
    Remove-Item Env:\LIGASE_VIRTUAL_DISPLAY_DEPENDENT_DEVICE -ErrorAction SilentlyContinue
    Remove-Item Env:\LIGASE_VIRTUAL_DISPLAY_FORCE_MARKER_CHANGED -ErrorAction SilentlyContinue
    Remove-Item Env:\LIGASE_VIRTUAL_DISPLAY_COMPENSATION_FAILURE -ErrorAction SilentlyContinue
  }
  if ($nativeExit -ne 0 -or @($raw).Count -ne 1) {
    throw "virtualDisplayMarkerFixtureProcessFailed:$Name"
  }
  $projection = [string]$raw | ConvertFrom-Json
  $markerPresent = Test-Path -LiteralPath $marker -PathType Leaf
  $markerExact = if ($PriorMarker -and $markerPresent) {
    [Convert]::ToBase64String($oldBytes) -ceq [Convert]::ToBase64String(
      [IO.File]::ReadAllBytes($marker))
  } else {
    -not $markerPresent
  }
  $tempCount = @(Get-ChildItem -LiteralPath $caseRoot -Force -Filter (
      ".ligase-driver-ownership-*.tmp")).Count
  $sentinelPresent = Test-Path -LiteralPath $sentinel
  $expectedSentinel = $DependentDevice -or $CompensationFailure -ceq "removeCert"
  if ([string]$projection.code -cne $ExpectedCode -or
      [int]$projection.tempResidueCount -ne 0 -or $tempCount -ne 0 -or
      (-not $markerExact -and $CompensationFailure -cne "restoreMarker") -or
      $sentinelPresent -ne $expectedSentinel) {
    throw (
      "virtualDisplayMarkerFixtureAssertionFailed:" +
      "${Name}:$([string]$projection.code):${markerExact}:" +
      "${sentinelPresent}:$tempCount")
  }
  $evidence = [ordered]@{
    name = $Name
    failureStage = $FailureStage
    priorMarker = $PriorMarker
    dependentDevice = $DependentDevice
    compensationFailure = $(if ($CompensationFailure) {
      $CompensationFailure
    } else { "none" })
    code = [string]$projection.code
    markerExact = $markerExact
    tempResidueCount = $tempCount
    certSentinelPresent = $sentinelPresent
  }
  $evidencePath = Join-Path $markerEvidenceRoot "$Name.json"
  [IO.File]::WriteAllText(
    $evidencePath, ($evidence | ConvertTo-Json -Compress),
    [Text.UTF8Encoding]::new($false))
  $readback = [IO.File]::ReadAllText($evidencePath) | ConvertFrom-Json
  if ([string]$readback.name -cne $Name -or
      [string]$readback.code -cne $ExpectedCode) {
    throw "virtualDisplayMarkerEvidenceReadbackFailed:$Name"
  }
  return $evidence
}

$markerBehaviorResults = @()
foreach ($stage in @(
    "createTemp", "writeTemp", "tempReadback", "atomicReplace",
    "finalReadback")) {
  $markerBehaviorResults += Invoke-MarkerBehaviorFixture `
    -Name "marker-$stage-existing" -FailureStage $stage -PriorMarker $true
  $markerBehaviorResults += Invoke-MarkerBehaviorFixture `
    -Name "marker-$stage-absent" -FailureStage $stage -PriorMarker $false
}
$markerBehaviorResults += Invoke-MarkerBehaviorFixture `
  -Name "marker-dependent-device" -FailureStage "writeTemp" `
  -PriorMarker $true -DependentDevice $true `
  -ExpectedCode "virtualDisplayRollbackFailed"
$markerBehaviorResults += Invoke-MarkerBehaviorFixture `
  -Name "marker-cert-compensation-failure" -FailureStage "writeTemp" `
  -PriorMarker $true -CompensationFailure "removeCert" `
  -ExpectedCode "virtualDisplayRollbackFailed"
$markerBehaviorResults += Invoke-MarkerBehaviorFixture `
  -Name "marker-restore-failure" -FailureStage "" `
  -PriorMarker $true -ForceMarkerChanged $true `
  -CompensationFailure "restoreMarker" `
  -ExpectedCode "virtualDisplayRollbackFailed"
$markerBehaviorResults += Invoke-MarkerBehaviorFixture `
  -Name "marker-restore-exact-noop" -FailureStage "writeTemp" `
  -PriorMarker $true -CompensationFailure "restoreMarker" `
  -ExpectedCode "virtualDisplayMarkerCommitFailed"

$virtualDisplayReadbackResults = @()
$readbackPowerShell = Join-Path ([Environment]::SystemDirectory) (
  "WindowsPowerShell\v1.0\powershell.exe")
if (-not (Test-Path -LiteralPath $readbackPowerShell -PathType Leaf)) {
  throw "virtualDisplayReadbackFixtureHostUnavailable"
}
$readbackCases = @(
  @{ name = "absent"; inventoryState = "available"; devices = @();
    state = "notInstalled"; count = 0; present = 0 },
  @{ name = "inventoryUnavailable"; inventoryState = "unavailable";
    devices = @(); state = "failed"; count = 0; present = 0 },
  @{ name = "exactOne"; devices = @([ordered]@{
      instanceId = "ROOT\DISPLAY\1000"
      hardwareIds = @("root\sudomaker\sudovda")
      status = "OK"
      present = $true
      driverInf = "oem32.inf"
    }); inventoryState = "available"; state = "available"; count = 1;
      present = 1 },
  @{ name = "exactOneUppercase"; devices = @([ordered]@{
      instanceId = "ROOT\DISPLAY\1002"
      hardwareIds = @("ROOT\SUDOMAKER\SUDOVDA")
      status = "OK"
      present = $true
      driverInf = "oem32.inf"
    }); inventoryState = "available"; state = "available"; count = 1;
      present = 1 },
  @{ name = "exactOneMixedCase"; devices = @([ordered]@{
      instanceId = "ROOT\DISPLAY\1003"
      hardwareIds = @("Root\SudoMaker\SudoVDA")
      status = "OK"
      present = $true
      driverInf = "oem32.inf"
    }); inventoryState = "available"; state = "available"; count = 1;
      present = 1 },
  @{ name = "duplicate"; devices = @(
      [ordered]@{
        instanceId = "ROOT\DISPLAY\1000"
        hardwareIds = @("root\sudomaker\sudovda")
        status = "OK"
        present = $true
        driverInf = "oem32.inf"
      },
      [ordered]@{
        instanceId = "ROOT\DISPLAY\1001"
        hardwareIds = @("root\sudomaker\sudovda")
        status = "OK"
        present = $true
        driverInf = "oem32.inf"
      }); inventoryState = "available"; state = "failed"; count = 2;
        present = 2 },
  @{ name = "bindingMissing"; devices = @([ordered]@{
      instanceId = "ROOT\DISPLAY\1000"
      hardwareIds = @("root\sudomaker\sudovda")
      status = "OK"
      present = $true
      driverInf = ""
    }); inventoryState = "available"; state = "failed"; count = 1;
      present = 1 },
  @{ name = "nonPresentExact"; devices = @([ordered]@{
      instanceId = "ROOT\DISPLAY\2000"
      hardwareIds = @("root\sudomaker\sudovda")
      status = "Unknown"
      present = $false
      driverInf = ""
    }); inventoryState = "available"; state = "failed"; count = 1;
      present = 0 },
  @{ name = "unboundExactUnexpectedNames"; devices = @([ordered]@{
      instanceId = "ROOT\OTHER\2001"
      hardwareIds = @("root\sudomaker\sudovda")
      status = "OK"
      present = $true
      driverInf = ""
    }); inventoryState = "available"; state = "failed"; count = 1;
      present = 1 },
  @{ name = "mixedPresentAndPhantom"; devices = @(
      [ordered]@{
        instanceId = "ROOT\DISPLAY\2002"
        hardwareIds = @("root\sudomaker\sudovda")
        status = "OK"
        present = $true
        driverInf = "oem32.inf"
      },
      [ordered]@{
        instanceId = "ROOT\OTHER\2003"
        hardwareIds = @("root\sudomaker\sudovda")
        status = "Unknown"
        present = $false
        driverInf = ""
      }); inventoryState = "available"; state = "failed"; count = 2;
        present = 1 }
)
foreach ($case in $readbackCases) {
  $caseRoot = Join-Path $root ("virtual-display-readback-" + $case.name)
  New-Item -ItemType Directory -Path $caseRoot | Out-Null
  [IO.File]::WriteAllText(
    (Join-Path $caseRoot "virtual-display-snapshot.json"),
    ([ordered]@{
        schemaVersion = 1
        inventoryState = [string]$case.inventoryState
        devices = @($case.devices)
      } |
      ConvertTo-Json -Depth 5 -Compress),
    [Text.UTF8Encoding]::new($false))
  $previousHarness = $env:LIGASE_INSTALL_VALIDATION_HARNESS
  $previousRoot = $env:LIGASE_VIRTUAL_DISPLAY_READBACK_VALIDATION_ROOT
  try {
    $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
    $env:LIGASE_VIRTUAL_DISPLAY_READBACK_VALIDATION_ROOT = $caseRoot
    $raw = @(& $readbackPowerShell -NoProfile -NonInteractive `
      -ExecutionPolicy Bypass -File $managementScript `
      -Action ValidateVirtualDisplayReadback `
      -InstallDirectory $caseRoot -ValidationRoot $caseRoot)
  } finally {
    if ($null -eq $previousHarness) {
      Remove-Item Env:LIGASE_INSTALL_VALIDATION_HARNESS -ErrorAction SilentlyContinue
    } else { $env:LIGASE_INSTALL_VALIDATION_HARNESS = $previousHarness }
    if ($null -eq $previousRoot) {
      Remove-Item Env:LIGASE_VIRTUAL_DISPLAY_READBACK_VALIDATION_ROOT `
        -ErrorAction SilentlyContinue
    } else {
      $env:LIGASE_VIRTUAL_DISPLAY_READBACK_VALIDATION_ROOT = $previousRoot
    }
  }
  if ($LASTEXITCODE -ne 0 -or @($raw).Count -ne 1) {
    throw "virtualDisplayReadbackFixtureFailed:$($case.name)"
  }
  $projection = [string]$raw | ConvertFrom-Json
  if ([string]$projection.state -cne [string]$case.state -or
      [int]$projection.deviceCount -ne [int]$case.count -or
      [int]$projection.presentDeviceCount -ne [int]$case.present -or
      [string]$projection.uniqueDeviceIdsSha256 -cnotmatch '^[0-9a-f]{64}$') {
    throw "virtualDisplayReadbackFixtureAssertionFailed:$($case.name)"
  }
  $virtualDisplayReadbackResults += [ordered]@{
    name = [string]$case.name
    state = [string]$projection.state
    deviceCount = [int]$projection.deviceCount
    presentDeviceCount = [int]$projection.presentDeviceCount
    identitySha = [string]$projection.uniqueDeviceIdsSha256
  }
}

$virtualDisplayRemovalResults = @()
$removalCases = @(
  @{ name = "stableZero"; counts = @(0); native = @(); fault = "none"; readbackFaultAt = -1; success = $true
    code = "virtualDisplayRemoved"; removed = 0; final = 0 },
  @{ name = "transientZeroToTwo"; counts = @(0, 2); native = @(); fault = "none"; readbackFaultAt = -1; success = $false
    code = "virtualDisplayDeviceZeroProofFailed"; removed = 0; final = 2 },
  @{ name = "zeroIdentityEpochDrift"; counts = @(0, 0); epochs = @(
      ("1" * 64), ("2" * 64)); native = @(); fault = "none"; readbackFaultAt = -1; success = $false
    code = "virtualDisplayDeviceZeroProofFailed"; removed = 0; final = 0 },
  @{ name = "lastZeroSampleCrossesDeadline"; counts = @(0, 0, 0)
    delays = @(0, 0, 180); total = 250; native = @()
    fault = "none"; readbackFaultAt = -1; success = $false
    code = "virtualDisplayDeviceZeroProofFailed"; removed = 0; final = 0 },
  @{ name = "nativeOne"; counts = @(1, 0); native = @(0); fault = "none"; readbackFaultAt = -1; success = $true
    code = "virtualDisplayRemoved"; removed = 1; final = 0 },
  @{ name = "nativeRebootRequired"; counts = @(1, 0); native = @(0)
    reboots = @($true); fault = "none"; readbackFaultAt = -1; success = $true
    code = "virtualDisplayRemoved"; removed = 1; final = 0 },
  @{ name = "nativeTwo"; counts = @(2, 1, 1, 0); native = @(0, 0); fault = "none"; readbackFaultAt = -1; success = $true
    code = "virtualDisplayRemoved"; removed = 2; final = 0 },
  @{ name = "nativeAuthorityFailed"; counts = @(1); native = @()
    fault = "authority"; readbackFaultAt = -1; success = $false
    code = "virtualDisplayDeviceRemoveFallbackFailed"; removed = 0; final = 1
    fallbackStage = "tupleValidation"; fallbackReason = "tupleInvalid" },
  @{ name = "nativeApiFailed"; counts = @(1); native = @(21)
    fault = "none"; readbackFaultAt = -1; success = $false
    code = "virtualDisplayDeviceRemoveFallbackFailed"; removed = 0; final = 1
    fallbackStage = "tupleValidation"; fallbackReason = "nativeFailure" },
  @{ name = "postRemoveReadbackFailure"; counts = @(1); native = @(0)
    fault = "none"; readbackFaultAt = 1; success = $false
    code = "virtualDisplayDeviceRemoveReadbackFailed"; removed = 0; final = 1 },
  @{ name = "settleProgress"; counts = @(1, 1, 0); native = @(0); fault = "none"; readbackFaultAt = -1
    success = $true; code = "virtualDisplayRemoved"; removed = 1; final = 0 },
  @{ name = "settleTimeout"; counts = @(1); native = @(0); fault = "none"; readbackFaultAt = -1; success = $false
    code = "virtualDisplayDeviceRemoveSettleFailed"; removed = 0; final = 1 }
)
foreach ($case in $removalCases) {
  $caseRoot = Join-Path $root ("virtual-display-removal-" + $case.name)
  New-Item -ItemType Directory -Path $caseRoot | Out-Null
  [IO.File]::WriteAllText(
    (Join-Path $caseRoot "virtual-display-removal-case.json"),
    ([ordered]@{
      schemaVersion = 1
      counts = @($case.counts)
      nativeExits = @($case.native)
      nativeReboots = @(
        if ($case.ContainsKey("reboots")) { @($case.reboots) }
        else { @($case.native | ForEach-Object { $false }) })
      nativeFault = [string]$case.fault
      readbackFaultAt = [int]$case.readbackFaultAt
      settleMilliseconds = 200
      totalMilliseconds = if ($case.ContainsKey("total")) {
        [int]$case.total
      } else { 1000 }
      identityEpochs = @(
        if ($case.ContainsKey("epochs")) {
          @($case.epochs)
        } else {
          @($case.counts | ForEach-Object {
            "{0:x64}" -f ([long]$_ + 1)
          })
        })
      snapshotDelayMilliseconds = @(
        if ($case.ContainsKey("delays")) {
          @($case.delays)
        } else {
          @($case.counts | ForEach-Object { 0 })
        })
    } | ConvertTo-Json -Compress),
    [Text.UTF8Encoding]::new($false))
  $previousHarness = $env:LIGASE_INSTALL_VALIDATION_HARNESS
  $previousRemovalRoot =
    $env:LIGASE_VIRTUAL_DISPLAY_REMOVAL_VALIDATION_ROOT
  try {
    $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
    $env:LIGASE_VIRTUAL_DISPLAY_REMOVAL_VALIDATION_ROOT = $caseRoot
    $raw = @(& $readbackPowerShell -NoProfile -NonInteractive `
      -ExecutionPolicy Bypass -File $managementScript `
      -Action ValidateVirtualDisplayRemovalReconciliation `
      -InstallDirectory $caseRoot -ValidationRoot $caseRoot)
  } finally {
    if ($null -eq $previousHarness) {
      Remove-Item Env:LIGASE_INSTALL_VALIDATION_HARNESS `
        -ErrorAction SilentlyContinue
    } else { $env:LIGASE_INSTALL_VALIDATION_HARNESS = $previousHarness }
    if ($null -eq $previousRemovalRoot) {
      Remove-Item Env:LIGASE_VIRTUAL_DISPLAY_REMOVAL_VALIDATION_ROOT `
        -ErrorAction SilentlyContinue
    } else {
      $env:LIGASE_VIRTUAL_DISPLAY_REMOVAL_VALIDATION_ROOT =
        $previousRemovalRoot
    }
  }
  if ($LASTEXITCODE -ne 0 -or @($raw).Count -ne 1) {
    throw "virtualDisplayRemovalFixtureFailed:$($case.name)"
  }
  $projection = [string]$raw | ConvertFrom-Json
  if ([string]$projection.code -cne [string]$case.code -or
      [bool]$projection.success -ne [bool]$case.success -or
      [int]$projection.removeCount -ne [int]$case.removed -or
      [int]$projection.observedDeviceCount -ne [int]$case.final) {
    throw "virtualDisplayRemovalFixtureAssertionFailed:$($case.name)"
  }
  if ([string]$case.name -ceq "nativeAuthorityFailed" -and (
      [int]$projection.fallbackExitCode -ne -1 -or
      [string]$projection.deviceRecovery -cne "failed" -or
      [string]$projection.residualDeviceState -cne "exactOneBound" -or
      [string]$projection.fallbackStage -cne
        [string]$case.fallbackStage -or
      [string]$projection.fallbackReason -cne
        [string]$case.fallbackReason)) {
    throw "virtualDisplayRemovalFixtureAssertionFailed:$($case.name)"
  }
  if ([string]$case.name -ceq "nativeApiFailed" -and (
      [int]$projection.fallbackExitCode -ne 21 -or
      [string]$projection.fallbackStage -cne "tupleValidation" -or
      [string]$projection.fallbackReason -cne "nativeFailure")) {
    throw "virtualDisplayRemovalFixtureAssertionFailed:$($case.name)"
  }
  if ([string]$case.name -ceq "postRemoveReadbackFailure" -and (
      [string]$projection.deviceRecovery -cne "failed" -or
      [string]$projection.residualDeviceState -cne "unknown")) {
    throw "virtualDisplayRemovalFixtureAssertionFailed:$($case.name)"
  }
  if ([string]$case.name -in @(
      "transientZeroToTwo", "zeroIdentityEpochDrift",
      "lastZeroSampleCrossesDeadline") -and (
      [int]$projection.nativeRemoveCalls -ne 0 -or
      [string]$projection.deviceRecovery -cne "failed")) {
    throw "virtualDisplayRemovalFixtureAssertionFailed:$($case.name)"
  }
  $virtualDisplayRemovalResults += [ordered]@{
    name = [string]$case.name
    code = [string]$projection.code
    success = [bool]$projection.success
    nativeRemoveCalls = [int]$projection.nativeRemoveCalls
    removeCount = [int]$projection.removeCount
    fallbackExitCode = [int]$projection.fallbackExitCode
    fallbackStage = [string]$projection.fallbackStage
    fallbackReason = [string]$projection.fallbackReason
    observedDeviceCount = [int]$projection.observedDeviceCount
    snapshotReads = [int]$projection.snapshotReads
    deviceRecovery = [string]$projection.deviceRecovery
    residualDeviceState = [string]$projection.residualDeviceState
  }
}

$virtualDisplayTerminalReadbackResults = @()
$terminalCases = @(
  @{ mode = "zero"; count = 0; state = "zero"; readback = "completed"
    reason = "none"; binding = $false },
  @{ mode = "oneBound"; count = 1; state = "exactOneBound"
    readback = "completed"; reason = "none"; binding = $true },
  @{ mode = "oneUnbound"; count = 1; state = "exactOneUnbound"
    readback = "completed"; reason = "none"; binding = $false },
  @{ mode = "unknown"; count = -1; state = "unknown"
    readback = "failed"; reason = "invalid"; binding = $false },
  @{ mode = "unavailable"; count = -1; state = "unknown"
    readback = "failed"; reason = "unavailable"; binding = $false }
)
foreach ($case in $terminalCases) {
  $caseRoot = Join-Path $root (
    "virtual-display-terminal-" + [string]$case.mode)
  New-Item -ItemType Directory -Path $caseRoot | Out-Null
  [IO.File]::WriteAllText(
    (Join-Path $caseRoot "terminal-case.json"),
    ([ordered]@{
      schemaVersion = 1
      mode = [string]$case.mode
    } | ConvertTo-Json -Compress),
    [Text.UTF8Encoding]::new($false))
  $previousHarness = $env:LIGASE_INSTALL_VALIDATION_HARNESS
  $previousTerminalRoot =
    $env:LIGASE_VIRTUAL_DISPLAY_TERMINAL_VALIDATION_ROOT
  try {
    $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
    $env:LIGASE_VIRTUAL_DISPLAY_TERMINAL_VALIDATION_ROOT = $caseRoot
    $terminalRaw = @(& $readbackPowerShell -NoProfile -NonInteractive `
      -ExecutionPolicy Bypass -File $managementScript `
      -Action ValidateVirtualDisplayTerminalReadback `
      -InstallDirectory $caseRoot -ValidationRoot $caseRoot)
  } finally {
    if ($null -eq $previousHarness) {
      Remove-Item Env:LIGASE_INSTALL_VALIDATION_HARNESS `
        -ErrorAction SilentlyContinue
    } else { $env:LIGASE_INSTALL_VALIDATION_HARNESS = $previousHarness }
    if ($null -eq $previousTerminalRoot) {
      Remove-Item Env:LIGASE_VIRTUAL_DISPLAY_TERMINAL_VALIDATION_ROOT `
        -ErrorAction SilentlyContinue
    } else {
      $env:LIGASE_VIRTUAL_DISPLAY_TERMINAL_VALIDATION_ROOT =
        $previousTerminalRoot
    }
  }
  if ($LASTEXITCODE -ne 0 -or @($terminalRaw).Count -ne 1) {
    throw "virtualDisplayTerminalFixtureFailed:$($case.mode)"
  }
  $projection = [string]$terminalRaw | ConvertFrom-Json
  if ([string]$projection.code -cne
        "virtualDisplayTerminalReadbackValidated" -or
      -not [bool]$projection.success -or
      [int]$projection.observedDeviceCount -ne [int]$case.count -or
      [string]$projection.residualDeviceState -cne [string]$case.state -or
      [string]$projection.terminalReadbackState -cne
        [string]$case.readback -or
      [string]$projection.terminalReadbackReason -cne
        [string]$case.reason -or
      [bool]$projection.driverBindingVerified -ne [bool]$case.binding -or
      [bool]$projection.pnpAccess -or
      [bool]$projection.programDataAccess) {
    throw "virtualDisplayTerminalFixtureAssertionFailed:$($case.mode)"
  }
  $virtualDisplayTerminalReadbackResults += [ordered]@{
    mode = [string]$case.mode
    observedDeviceCount = [int]$projection.observedDeviceCount
    residualDeviceState = [string]$projection.residualDeviceState
    terminalReadbackState = [string]$projection.terminalReadbackState
    terminalReadbackReason = [string]$projection.terminalReadbackReason
  }
}

$diagnosticProjectionRoot = Join-Path $root (
  "virtual-display-diagnostic-projection")
New-Item -ItemType Directory -Path $diagnosticProjectionRoot | Out-Null
[IO.File]::WriteAllText(
  (Join-Path $diagnosticProjectionRoot "ligase-install-manifest.json"),
  ([ordered]@{
    schemaVersion = 1
    sourceHead = "0000000000000000000000000000000000000000"
  } | ConvertTo-Json -Compress),
  [Text.UTF8Encoding]::new($false))
$previousHarness = $env:LIGASE_INSTALL_VALIDATION_HARNESS
$previousDiagnosticRoot =
  $env:LIGASE_VIRTUAL_DISPLAY_DIAGNOSTIC_VALIDATION_ROOT
$previousDiagnosticFault =
  $env:LIGASE_VIRTUAL_DISPLAY_DIAGNOSTIC_WRITE_FAULT
try {
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_VIRTUAL_DISPLAY_DIAGNOSTIC_VALIDATION_ROOT =
    $diagnosticProjectionRoot
  $env:LIGASE_VIRTUAL_DISPLAY_DIAGNOSTIC_WRITE_FAULT = "1"
  $diagnosticRaw = @(& $readbackPowerShell -NoProfile -NonInteractive `
    -ExecutionPolicy Bypass -File $managementScript `
    -Action ValidateVirtualDisplayDiagnosticProjection `
    -InstallDirectory $diagnosticProjectionRoot `
    -ValidationRoot $diagnosticProjectionRoot)
} finally {
  if ($null -eq $previousHarness) {
    Remove-Item Env:LIGASE_INSTALL_VALIDATION_HARNESS -ErrorAction SilentlyContinue
  } else { $env:LIGASE_INSTALL_VALIDATION_HARNESS = $previousHarness }
  if ($null -eq $previousDiagnosticRoot) {
    Remove-Item Env:LIGASE_VIRTUAL_DISPLAY_DIAGNOSTIC_VALIDATION_ROOT `
      -ErrorAction SilentlyContinue
  } else {
    $env:LIGASE_VIRTUAL_DISPLAY_DIAGNOSTIC_VALIDATION_ROOT =
      $previousDiagnosticRoot
  }
  if ($null -eq $previousDiagnosticFault) {
    Remove-Item Env:LIGASE_VIRTUAL_DISPLAY_DIAGNOSTIC_WRITE_FAULT `
      -ErrorAction SilentlyContinue
  } else {
    $env:LIGASE_VIRTUAL_DISPLAY_DIAGNOSTIC_WRITE_FAULT =
      $previousDiagnosticFault
  }
}
if ($LASTEXITCODE -ne 0 -or @($diagnosticRaw).Count -ne 1) {
  throw "virtualDisplayDiagnosticProjectionFixtureFailed"
}
$diagnosticProjection = [string]$diagnosticRaw | ConvertFrom-Json
if ([string]$diagnosticProjection.code -cne
      "virtualDisplayDiagnosticProjectionValidated" -or
    -not [bool]$diagnosticProjection.success -or
    [int]$diagnosticProjection.crossSpliceRejected -ne 39 -or
    -not [bool]$diagnosticProjection.primaryWriteFailed -or
    [string]$diagnosticProjection.resultCode -cne
      "virtualDisplayReadbackFailed" -or
    [string]$diagnosticProjection.readbackCode -cne
      "virtualDisplayDeviceCountInvalid" -or
    [int]$diagnosticProjection.observedDeviceCount -ne 2 -or
    [bool]$diagnosticProjection.driverBindingVerified -or
    [string]$diagnosticProjection.deviceRecovery -cne "failed" -or
    [string]$diagnosticProjection.residualDeviceState -cne "multiple" -or
    [string]$diagnosticProjection.compensationState -cne "completed" -or
    [string]$diagnosticProjection.transactionRollback -cne "completed" -or
    [int]$diagnosticProjection.tokenLength -lt 1 -or
    [int]$diagnosticProjection.tokenLength -gt 4096 -or
    [string]$diagnosticProjection.tokenSha256 -cnotmatch '^[0-9a-f]{64}$' -or
    [string]$diagnosticProjection.lastOutcomeSha256 -cnotmatch
      '^[0-9a-f]{64}$' -or
    [string]$diagnosticProjection.removeResultCode -cne
      "virtualDisplayDeviceRemoveFailed" -or
    [string]$diagnosticProjection.removeInstallStage -cne "deviceRemove" -or
    [int]$diagnosticProjection.removeExitCode -ne 0 -or
    [int]$diagnosticProjection.removeCount -ne 16 -or
    -not [bool]$diagnosticProjection.removePrimaryWriteFailed -or
    [int]$diagnosticProjection.removeTokenLength -lt 1 -or
    [int]$diagnosticProjection.removeTokenLength -gt 4096 -or
    [string]$diagnosticProjection.removeLastOutcomeSha256 -cnotmatch
      '^[0-9a-f]{64}$' -or
    [string]$diagnosticProjection.postCreateResultCode -cne
      "virtualDisplayRollbackFailed" -or
    [int]$diagnosticProjection.postCreateRemoveCount -ne 0 -or
    [bool]$diagnosticProjection.postCreateFallbackAttempted -or
    [int]$diagnosticProjection.postCreateObservedDeviceCount -ne 1 -or
    [string]$diagnosticProjection.postCreateDeviceRecovery -cne
      "completed" -or
    [string]$diagnosticProjection.postCreateResidualDeviceState -cne
      "exactOneUnbound" -or
    [string]$diagnosticProjection.postCreateCompensationState -cne
      "failed" -or
    [string]$diagnosticProjection.postCreateCompensationFailureReason -cne
      "dependentDevice" -or
    [string]$diagnosticProjection.postCreateTerminalReadbackState -cne
      "completed" -or
    [string]$diagnosticProjection.postCreateTerminalReadbackReason -cne
      "none" -or
    [int]$diagnosticProjection.postCreateInvocationCount -ne 1 -or
    [string]$diagnosticProjection.postCreateInvocationIdSha256 -cnotmatch
      '^[1-9a-f][0-9a-f]{63}$' -or
    [string]$diagnosticProjection.postCreatePreIdentitySha256 -cne
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" -or
    [string]$diagnosticProjection.postCreatePostIdentitySha256 -cne
      "1111111111111111111111111111111111111111111111111111111111111111" -or
    [string]$diagnosticProjection.postCreateIdentityState -cne "completed" -or
    [string]$diagnosticProjection.postCreateIdentityReason -cne "none" -or
    [string]$diagnosticProjection.prePostResultCode -cne
      "virtualDisplayInstallFailed" -or
    [string]$diagnosticProjection.prePostIdentityState -cne "failed" -or
    [string]$diagnosticProjection.prePostIdentityReason -cne "unavailable" -or
    [string]$diagnosticProjection.prePostLastOutcomeSha256 -cnotmatch
      '^[0-9a-f]{64}$' -or
    [string]$diagnosticProjection.prePostZeroResultCode -cne
      "virtualDisplayInstallFailed" -or
    [string]$diagnosticProjection.prePostZeroIdentityState -cne "completed" -or
    [int]$diagnosticProjection.prePostZeroObservedDeviceCount -ne 0 -or
    [string]$diagnosticProjection.prePostZeroLastOutcomeSha256 -cnotmatch
      '^[0-9a-f]{64}$' -or
    -not [bool]$diagnosticProjection.postCreatePrimaryWriteFailed -or
    [string]$diagnosticProjection.postCreateLastOutcomeSha256 -cnotmatch
      '^[0-9a-f]{64}$' -or
    [string]$diagnosticProjection.inventoryStage -cne "hardwareIds" -or
    [string]$diagnosticProjection.inventoryFailureStage -cne "coverage" -or
    [string]$diagnosticProjection.inventoryOutputReason -cne "none" -or
    [int]$diagnosticProjection.inventoryNativeExitCode -ne -1 -or
    [string]$diagnosticProjection.inventoryChildFailureStage -cne "none" -or
    [string]$diagnosticProjection.inventoryCoverageStage -cne "rowCount" -or
    [string]$diagnosticProjection.inventoryCoverageReason -cne "missing" -or
    [int]$diagnosticProjection.inventoryRequestedCount -ne 32 -or
    [int]$diagnosticProjection.inventoryReturnedCount -ne 31 -or
    [int]$diagnosticProjection.inventoryDeviceCount -ne 369 -or
    [int]$diagnosticProjection.inventoryHardwareBatchesCompleted -ne 0 -or
    [int]$diagnosticProjection.inventoryDriverBatchesCompleted -ne 0 -or
    [string]$diagnosticProjection.inventoryCleanupState -cne "completed" -or
    [string]$diagnosticProjection.inventoryLastOutcomeSha256 -cnotmatch
      '^[0-9a-f]{64}$' -or
    [int]$diagnosticProjection.inventoryOutputReasonsPersisted -ne 10 -or
    [string]$diagnosticProjection.postLoopDeadlineFailureStage -cne
      "deadline" -or
    [string]$diagnosticProjection.postLoopDeadlineCoverageStage -cne
      "none" -or
    [string]$diagnosticProjection.postLoopDeadlineLastOutcomeSha256 -cnotmatch
      '^[0-9a-f]{64}$' -or
    [string]$diagnosticProjection.finalizePreReadStage -cne "failed" -or
    [string]$diagnosticProjection.finalizePreReadReason -cne "timeout" -or
    [string]$diagnosticProjection.finalizePreReadCleanupState -cne
      "completed" -or
    -not [bool]$diagnosticProjection.finalizePreReadRootPidZero -or
    [int]$diagnosticProjection.finalizePreReadJobActiveProcesses -ne 0 -or
    -not [bool]$diagnosticProjection.finalizePreReadStdoutClosed -or
    -not [bool]$diagnosticProjection.finalizePreReadStderrClosed -or
    [string]$diagnosticProjection.finalizePreReadPrimaryResultCode -cne
      "virtualDisplayReadbackFailed" -or
    [string]$diagnosticProjection.finalizePreReadLastOutcomeSha256 -cnotmatch
      '^[0-9a-f]{64}$') {
  throw "virtualDisplayDiagnosticProjectionFixtureAssertionFailed"
}

$evidenceSecondaryRoot = Join-Path $root "installer-evidence-secondary-failure"
New-Item -ItemType Directory -Path $evidenceSecondaryRoot | Out-Null
[IO.File]::WriteAllText(
  (Join-Path $evidenceSecondaryRoot "ligase-install-manifest.json"),
  ([ordered]@{
    schemaVersion = 1
    sourceHead = "0000000000000000000000000000000000000000"
  } | ConvertTo-Json -Compress),
  [Text.UTF8Encoding]::new($false))
$previousHarness = $env:LIGASE_INSTALL_VALIDATION_HARNESS
try {
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $evidenceSecondaryRaw = @(& $readbackPowerShell -NoProfile `
    -NonInteractive -ExecutionPolicy Bypass -File $managementScript `
    -Action ValidateInstallerEvidenceSecondaryFailure `
    -InstallDirectory $evidenceSecondaryRoot `
    -ValidationRoot $evidenceSecondaryRoot)
} finally {
  if ($null -eq $previousHarness) {
    Remove-Item Env:LIGASE_INSTALL_VALIDATION_HARNESS `
      -ErrorAction SilentlyContinue
  } else {
    $env:LIGASE_INSTALL_VALIDATION_HARNESS = $previousHarness
  }
}
if ($LASTEXITCODE -ne 0 -or @($evidenceSecondaryRaw).Count -ne 1) {
  throw "installerEvidenceSecondaryFailureFixtureFailed"
}
$evidenceSecondaryProjection =
  [string]$evidenceSecondaryRaw | ConvertFrom-Json
if ([string]$evidenceSecondaryProjection.code -cne
      "installerEvidenceSecondaryFailureValidated" -or
    -not [bool]$evidenceSecondaryProjection.success -or
    [int]$evidenceSecondaryProjection.primarySelectionCasesPassed -ne 4 -or
    [int]$evidenceSecondaryProjection.actualPrimaryConsumersPassed -ne 2 -or
    [int]$evidenceSecondaryProjection.actualPrimaryCrossSpliceRejected -ne 6 -or
    [int]$evidenceSecondaryProjection.primarySelectionCrossSpliceRejected -ne 6 -or
    [int]$evidenceSecondaryProjection.finalizeHandoffCasesPassed -ne 9 -or
    [int]$evidenceSecondaryProjection.finalizeHandoffCrossSpliceRejected -ne 4 -or
    [int]$evidenceSecondaryProjection.finalizeValidationAuthorityCasesPassed -ne 9 -or
    [int]$evidenceSecondaryProjection.writerFaultsPassed -ne 7 -or
    [int]$evidenceSecondaryProjection.parseFaultsPassed -ne 2 -or
    [int]$evidenceSecondaryProjection.schemaSubreasonCasesPassed -ne 9 -or
    [int]$evidenceSecondaryProjection.schemaCountCrossSpliceRejected -ne 4 -or
    [int]$evidenceSecondaryProjection.virtualDisplayWriterFaultsPassed -ne 9 -or
    [int]$evidenceSecondaryProjection.secondaryCrossSpliceRejected -ne 3 -or
    -not [bool]$evidenceSecondaryProjection.persistenceUnavailable -or
    [string]$evidenceSecondaryProjection.primaryResultCode -cne
      "virtualDisplayReadbackFailed" -or
    [int]$evidenceSecondaryProjection.finalOutcomeCount -ne 0 -or
    [int]$evidenceSecondaryProjection.tempResidueCount -ne 0 -or
    [int]$evidenceSecondaryProjection.processStartCount -ne 0 -or
    [bool]$evidenceSecondaryProjection.systemMutation) {
  throw "installerEvidenceSecondaryFailureFixtureAssertionFailed"
}

$finalizeEntryCasesPassed = 0
foreach ($entryMode in @("stopAfterFreeze", "failAfterFreeze")) {
  $entryRoot = Join-Path $root ("finalize-entry-handoff-" + $entryMode)
  New-Item -ItemType Directory -Path $entryRoot | Out-Null
  Copy-Item -LiteralPath (
    Join-Path $evidenceSecondaryRoot "ligase-install-manifest.json") `
    -Destination $entryRoot
  Copy-Item -LiteralPath (
    Join-Path $evidenceSecondaryRoot "virtual-display-outcome.json") `
    -Destination $entryRoot
  $previousFinalizeValidation = $env:LIGASE_FINALIZE_HANDOFF_VALIDATION
  $previousFinalizeStop = $env:LIGASE_FINALIZE_HANDOFF_STOP_AFTER_FREEZE
  $previousFinalizeFail = $env:LIGASE_FINALIZE_HANDOFF_FAIL_AFTER_FREEZE
  try {
    $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
    $env:LIGASE_FINALIZE_HANDOFF_VALIDATION = "1"
    if ($entryMode -ceq "stopAfterFreeze") {
      $env:LIGASE_FINALIZE_HANDOFF_STOP_AFTER_FREEZE = "1"
      Remove-Item Env:LIGASE_FINALIZE_HANDOFF_FAIL_AFTER_FREEZE `
        -ErrorAction SilentlyContinue
    } else {
      $env:LIGASE_FINALIZE_HANDOFF_FAIL_AFTER_FREEZE = "1"
      Remove-Item Env:LIGASE_FINALIZE_HANDOFF_STOP_AFTER_FREEZE `
        -ErrorAction SilentlyContinue
    }
    $entryRaw = @(& $readbackPowerShell -NoProfile -NonInteractive `
      -ExecutionPolicy Bypass -File $managementScript `
      -Action FinalizeInstall -InstallDirectory $entryRoot `
      -ValidationRoot $entryRoot -VirtualDisplaySelected `
      -VirtualDisplayOutcome failed)
  } finally {
    foreach ($restore in @(
        @{ name = "LIGASE_FINALIZE_HANDOFF_VALIDATION";
          value = $previousFinalizeValidation },
        @{ name = "LIGASE_FINALIZE_HANDOFF_STOP_AFTER_FREEZE";
          value = $previousFinalizeStop },
        @{ name = "LIGASE_FINALIZE_HANDOFF_FAIL_AFTER_FREEZE";
          value = $previousFinalizeFail })) {
      if ($null -eq $restore.value) {
        Remove-Item ("Env:" + $restore.name) -ErrorAction SilentlyContinue
      } else { Set-Item ("Env:" + $restore.name) $restore.value }
    }
  }
  if (@($entryRaw).Count -ne 1) { throw "finalizeEntryHandoffFixtureFailed" }
  $entryProjection = [string]$entryRaw | ConvertFrom-Json
  $entryOutcome = [IO.File]::ReadAllText(
    (Join-Path $entryRoot "last-outcome.json"),
    [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
  if ($entryMode -ceq "stopAfterFreeze") {
    if ($LASTEXITCODE -ne 0 -or
        [string]$entryProjection.code -cne "finalizeEntryHandoffValidated" -or
        [string]$entryProjection.handoff.state -cne "frozenPrimary") {
      throw "finalizeEntryHandoffFixtureFailed"
    }
  } elseif ($LASTEXITCODE -ne 10 -or
      [string]$entryOutcome.failedField -cne "virtualDisplay" -or
      [string]$entryOutcome.virtualDisplay.resultCode -cne
        "virtualDisplayDeviceRemoveFallbackFailed" -or
      [int]$entryOutcome.virtualDisplay.childExitCode -ne 25 -or
      [int]$entryOutcome.virtualDisplay.removeExitCode -ne 6 -or
      [string]$entryOutcome.virtualDisplay.fallbackReason -cne
        "outputInvalid" -or
      [string]$entryOutcome.finalizeHandoff.state -cne "completed" -or
      [string]$entryOutcome.persistenceState -cne "lastResort") {
    throw "finalizeEntryHandoffFixtureFailed"
  }
  if (@(Get-ChildItem -LiteralPath $entryRoot -Force | Where-Object {
      $_.Name -like ".finalize-handoff-*.tmp" -or
      $_.Name -like ".last-outcome-*.tmp" }).Count -ne 0) {
    throw "finalizeEntryHandoffFixtureFailed"
  }
  $finalizeEntryCasesPassed++
}

[ordered]@{
  code = "installDirectoryRuntimeHarnessPassed"
  finalizeEntryCasesPassed = $finalizeEntryCasesPassed
  cases = $results
  finalizationStackCases = $finalizationStackResults
  shortcutCases = $shortcutResults
  failureFlows = $failureFlowResults
  virtualDisplayMarkerCases = $markerBehaviorResults
  virtualDisplayInstallerProcessCases = $installerProcessResults
  virtualDisplayInstallerCaseSchemaCases = $installerProcessCaseSchemaResults
  virtualDisplayReadbackCases = $virtualDisplayReadbackResults
  virtualDisplayNativeInventoryHelperCases = $inventoryHelperResults
  virtualDisplayNativeInventoryInvocationCases =
    $nativeInventoryInvocationResults
  installerEvidenceSecondaryFailure = $evidenceSecondaryProjection
  virtualDisplayRemovalCases = $virtualDisplayRemovalResults
  virtualDisplayTerminalReadbackCases =
    $virtualDisplayTerminalReadbackResults
  virtualDisplayDiagnosticProjection = $diagnosticProjection
  boundedHostProcessCases = $boundedHostCases
  boundedHostRunner = [ordered]@{
    sourceSha = $argumentListRunnerSourceSha
    binarySha = $argumentListRunnerBinarySha
    byteEmitterArgumentValidation = "passed"
  }
  sourceContracts = $sourceContractResults
} | ConvertTo-Json -Depth 4 -Compress
$global:LASTEXITCODE = 0
