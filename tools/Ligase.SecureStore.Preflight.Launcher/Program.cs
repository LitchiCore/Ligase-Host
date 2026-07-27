using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

internal static class Program
{
    private const int TimeoutMilliseconds = 30000;
    private const int CleanupTimeoutMilliseconds = 5000;
#if LAUNCHER_VALIDATION
    private const int ValidationTimeoutMilliseconds = 1000;
#endif
    private const int MaximumFrameBytes = 4096;
    private const string ChildFileName = "Ligase.SecureStore.Preflight.exe";
    private const string EvidenceFileName =
        "secure-store-preflight-review-evidence.json";
#if LAUNCHER_VALIDATION
    private const string ValidationChildFileName =
        "Ligase.SecureStore.Preflight.Validation.exe";
    private static readonly HashSet<string> ValidationActions =
        new(StringComparer.Ordinal)
        {
            "--validate-ipc-success",
            "--validate-ipc-early-failure",
            "--validate-ipc-nonce-mismatch",
            "--validate-ipc-client-pid-mismatch",
            "--validate-ipc-client-session-mismatch",
            "--validate-ipc-client-sid-mismatch",
            "--validate-ipc-server-pid-mismatch",
            "--validate-ipc-server-session-mismatch",
            "--validate-ipc-server-sid-mismatch",
            "--validate-ipc-frame-oversize",
            "--validate-ipc-disconnect",
            "--validate-ipc-timeout",
            "--validate-ipc-timeout-tree",
            "--validate-ipc-cleanup-kill-fault",
            "--validate-ipc-cleanup-snapshot-fault",
            "--validate-ipc-cleanup-wait-timeout",
            "--validate-ipc-cleanup-has-exited-fault",
            "--validate-ipc-cleanup-pid-check-fault"
        };
#endif

    private static int Main(string[] args)
    {
#if LAUNCHER_VALIDATION
        if (args.Length != 1 || !ValidationActions.Contains(args[0]))
            return Fail("invalidArguments", "inputValidation", 18);
        var validationAction = args[0];
#else
        if (args.Length != 0)
            return Fail("invalidArguments", "inputValidation", 18);
#endif

        var pipeName = "LigaseSecureStore-" +
            Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var parentPid = Environment.ProcessId;
        var userSid = WindowsIdentity.GetCurrent().User ??
            throw new InvalidOperationException("launcherUserUnavailable");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(
            userSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(
                WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        using var pipe = NamedPipeServerStreamAcl.Create(
            pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            4096, 4096, security);
        var childPath = Path.Combine(
            AppContext.BaseDirectory,
#if LAUNCHER_VALIDATION
            ValidationChildFileName
#else
            ChildFileName
#endif
            );
        if (!File.Exists(childPath) ||
            (File.GetAttributes(childPath) & FileAttributes.ReparsePoint) != 0)
            return Fail("childArtifactUnavailable", "inputValidation", 18);

        var start = new ProcessStartInfo
        {
            FileName = childPath,
            Arguments = "--diagnostic-pipe " + pipeName +
                " --nonce " + nonce +
                " --parent-pid " + parentPid
#if LAUNCHER_VALIDATION
                + " --validation-action " +
                ToChildValidationAction(validationAction)
#endif
                ,
#if LAUNCHER_VALIDATION
            UseShellExecute = false,
            CreateNoWindow = true,
#else
            UseShellExecute = true,
            Verb = "runas",
#endif
            WorkingDirectory = AppContext.BaseDirectory,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process? child = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            child = Process.Start(start);
            if (child is null)
                return Fail("childStartFailed", "launch", 18);
            using var deadline = new CancellationTokenSource(
#if LAUNCHER_VALIDATION
                ValidationTimeoutMilliseconds);
#else
                TimeoutMilliseconds);
#endif
            pipe.WaitForConnectionAsync(deadline.Token)
                .GetAwaiter().GetResult();
            if (!GetNamedPipeClientProcessId(
                    pipe.SafePipeHandle, out var clientPid) ||
                clientPid != (uint)child.Id
#if LAUNCHER_VALIDATION
                || validationAction == "--validate-ipc-client-pid-mismatch"
#endif
                ||
                child.SessionId != Process.GetCurrentProcess().SessionId
#if LAUNCHER_VALIDATION
                || validationAction == "--validate-ipc-client-session-mismatch"
#endif
                ||
                !string.Equals(
                    GetProcessUserSid(child.Handle), userSid.Value,
                    StringComparison.Ordinal)
#if LAUNCHER_VALIDATION
                || validationAction == "--validate-ipc-client-sid-mismatch"
#endif
                )
                throw new InvalidOperationException("childPeerInvalid");

            var hello = ReadFrame(pipe, deadline.Token);
            var expectedHello = Encoding.UTF8.GetBytes(
                "{\"schemaId\":\"secureStoreDiagnosticHelloV1\"," +
                "\"nonce\":\"" + nonce + "\",\"pid\":" + child.Id + "," +
                "\"parentPid\":" + parentPid + "}");
            if (!hello.SequenceEqual(expectedHello))
                throw new InvalidOperationException("childHandshakeInvalid");
            WriteFrame(pipe, Encoding.UTF8.GetBytes(
                "{\"schemaId\":\"secureStoreDiagnosticAckV1\"," +
                "\"nonce\":\"" + nonce + "\"}"));
            var diagnostic = ReadFrame(pipe, deadline.Token);
            child.WaitForExitAsync(deadline.Token).GetAwaiter().GetResult();
            var diagnosticHash = Convert.ToHexString(
                SHA256.HashData(diagnostic));
            var projection = ParseDiagnostic(diagnostic);
            var evidence = Encoding.UTF8.GetBytes(
                "{\"schemaVersion\":1,\"releaseKind\":\"UnsignedDev\"," +
                "\"trustBoundary\":\"localManualExactSha\"," +
                "\"result\":\"observed\",\"nativeExit\":" +
                child.ExitCode + ",\"diagnosticLength\":" +
                diagnostic.Length + ",\"diagnosticSha256\":\"" +
                diagnosticHash + "\",\"resultCode\":\"" +
                projection.ResultCode + "\",\"stage\":\"" +
                projection.Stage + "\",\"nativeCategory\":\"" +
                projection.NativeCategory + "\",\"nativeCode\":" +
                projection.NativeCode + ",\"aclMutationOccurred\":" +
                (projection.AclMutationOccurred ? "true" : "false") +
                ",\"aclRollback\":\"" + projection.AclRollback +
                "\",\"elapsedMilliseconds\":" +
                stopwatch.ElapsedMilliseconds + "}");
            WriteEvidence(evidence);
#if LAUNCHER_VALIDATION
            Console.Out.Write(
                "{\"schemaId\":\"launcherIpcValidationV1\"," +
                "\"result\":\"observed\",\"action\":\"" +
                validationAction.Substring("--validate-".Length) + "\"," +
                "\"childExit\":" + child.ExitCode + "," +
                "\"diagnosticLength\":" + diagnostic.Length + "," +
                "\"diagnosticSha256\":\"" + diagnosticHash + "\"," +
                "\"stage\":\"" + projection.Stage + "\"," +
                "\"nativeCode\":" + projection.NativeCode + "}");
#endif
            return child.ExitCode;
        }
        catch
        {
#if LAUNCHER_VALIDATION
            var cleanup = CleanupChild(child, validationAction);
#else
            var cleanup = CleanupChild(child, "");
#endif
            return cleanup.Completed
                ? Fail(
                    "diagnosticChannelFailed", "ipc", 18, "completed",
                    cleanup)
                : Fail(
                    "childCleanupFailed", "cleanup", 18, "failed",
                    cleanup);
        }
        finally
        {
            child?.Dispose();
        }
    }

#if LAUNCHER_VALIDATION
    private static string ToChildValidationAction(string action) =>
        action switch
        {
            "--validate-ipc-success" => "ipcSuccess",
            "--validate-ipc-early-failure" => "earlyFailure",
            "--validate-ipc-nonce-mismatch" => "nonceMismatch",
            "--validate-ipc-server-pid-mismatch" => "serverPidMismatch",
            "--validate-ipc-server-session-mismatch" =>
                "serverSessionMismatch",
            "--validate-ipc-server-sid-mismatch" => "serverSidMismatch",
            "--validate-ipc-frame-oversize" => "frameOversize",
            "--validate-ipc-disconnect" => "disconnect",
            "--validate-ipc-timeout" => "timeout",
            "--validate-ipc-timeout-tree" => "timeoutTree",
            "--validate-ipc-cleanup-kill-fault" => "timeout",
            "--validate-ipc-cleanup-snapshot-fault" => "timeout",
            "--validate-ipc-cleanup-wait-timeout" => "timeout",
            "--validate-ipc-cleanup-has-exited-fault" => "timeout",
            "--validate-ipc-cleanup-pid-check-fault" => "timeout",
            _ => "ipcSuccess"
        };
#endif

    private static CleanupResult CleanupChild(
        Process? child, string validationAction)
    {
        if (child is null)
            return new(true, 0, false, false, true, true, true);

        var deadline = Stopwatch.StartNew();
        var cleanupFault = false;
        var killAttempted = false;
        var rootFallbackAttempted = false;
        var waitCompleted = false;
        var rootPid = 0;
        var treePids = new HashSet<int>();
        var snapshotAvailable = false;
        try
        {
            rootPid = child.Id;
#if LAUNCHER_VALIDATION
            if (validationAction ==
                "--validate-ipc-cleanup-snapshot-fault")
                throw new InvalidOperationException(
                    "validationSnapshotFault");
#endif
            treePids = GetProcessTree(rootPid);
            snapshotAvailable = true;
        }
        catch
        {
            cleanupFault = true;
        }

        try
        {
#if LAUNCHER_VALIDATION
            if (validationAction ==
                "--validate-ipc-cleanup-has-exited-fault")
            {
                cleanupFault = true;
            }
            else
#endif
            if (child.HasExited)
                return new(
                    !cleanupFault &&
                    snapshotAvailable &&
                    VerifyProcessTreeAbsent(treePids),
                    rootPid, false, false, true, true,
                    deadline.ElapsedMilliseconds <=
                        CleanupTimeoutMilliseconds);
        }
        catch
        {
            cleanupFault = true;
        }

        try
        {
            killAttempted = true;
#if LAUNCHER_VALIDATION
            if (validationAction == "--validate-ipc-cleanup-kill-fault")
                cleanupFault = true;
#endif
            child.Kill(entireProcessTree: true);
        }
        catch
        {
            cleanupFault = true;
            rootFallbackAttempted = true;
            try { child.Kill(); } catch { }
        }
        if (!snapshotAvailable)
        {
            rootFallbackAttempted = true;
            try
            {
                if (!child.HasExited)
                    child.Kill();
            }
            catch { }
        }

        var remaining = RemainingMilliseconds(deadline);
        var waited = false;
        if (remaining > 0)
        {
            try
            {
                waited = child.WaitForExit(remaining);
                waitCompleted = waited;
#if LAUNCHER_VALIDATION
                if (validationAction ==
                    "--validate-ipc-cleanup-wait-timeout")
                {
                    cleanupFault = true;
                    waited = false;
                    waitCompleted = false;
                }
#endif
            }
            catch
            {
                cleanupFault = true;
            }
        }

        if (!waited)
        {
            cleanupFault = true;
            try
            {
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);
            }
            catch { }
            remaining = RemainingMilliseconds(deadline);
            if (remaining > 0)
            {
                try { child.WaitForExit(remaining); } catch { }
                try { waitCompleted = child.HasExited; } catch { }
            }
        }

        bool hasExited;
        try
        {
            hasExited = child.HasExited;
        }
        catch
        {
            return new(
                false, rootPid, killAttempted, rootFallbackAttempted,
                waitCompleted, false,
                deadline.ElapsedMilliseconds <= CleanupTimeoutMilliseconds);
        }
        if (!hasExited)
            return new(
                false, rootPid, killAttempted, rootFallbackAttempted,
                waitCompleted, false,
                deadline.ElapsedMilliseconds <= CleanupTimeoutMilliseconds);

        var rootPidZero = rootPid == 0 ||
            VerifyProcessTreeAbsent(new[] { rootPid });
        if (!rootPidZero)
            return new(
                false, rootPid, killAttempted, rootFallbackAttempted,
                waitCompleted, false,
                deadline.ElapsedMilliseconds <= CleanupTimeoutMilliseconds);

#if LAUNCHER_VALIDATION
        if (validationAction ==
            "--validate-ipc-cleanup-pid-check-fault")
        {
            cleanupFault = true;
        }
        else
#endif
        if (snapshotAvailable &&
            !VerifyProcessTreeAbsent(treePids))
            return new(
                false, rootPid, killAttempted, rootFallbackAttempted,
                waitCompleted, true,
                deadline.ElapsedMilliseconds <= CleanupTimeoutMilliseconds);

        return new(
            !cleanupFault && snapshotAvailable,
            rootPid, killAttempted, rootFallbackAttempted,
            waitCompleted, true,
            deadline.ElapsedMilliseconds <= CleanupTimeoutMilliseconds);
    }

    private static int RemainingMilliseconds(Stopwatch deadline) =>
        Math.Max(
            0,
            CleanupTimeoutMilliseconds -
            checked((int)Math.Min(
                deadline.ElapsedMilliseconds,
                CleanupTimeoutMilliseconds)));

    private static HashSet<int> GetProcessTree(int rootPid)
    {
        var parents = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1))
            throw new InvalidOperationException("processSnapshotFailed");
        try
        {
            var entry = new ProcessEntry32
            {
                Size = (uint)Marshal.SizeOf<ProcessEntry32>()
            };
            if (!Process32First(snapshot, ref entry))
                throw new InvalidOperationException("processSnapshotFailed");
            do
            {
                parents[checked((int)entry.ProcessId)] =
                    checked((int)entry.ParentProcessId);
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            } while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        var result = new HashSet<int> { rootPid };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var pair in parents)
            {
                if (!result.Contains(pair.Key) &&
                    result.Contains(pair.Value))
                {
                    result.Add(pair.Key);
                    changed = true;
                }
            }
        }
        return result;
    }

    private static bool VerifyProcessTreeAbsent(IEnumerable<int> pids)
    {
        foreach (var pid in pids)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (!process.HasExited)
                    return false;
            }
            catch (ArgumentException)
            {
            }
            catch
            {
                return false;
            }
        }
        return true;
    }

    private static int Fail(
        string result, string stage, int exit,
        string cleanup = "notRequired",
        CleanupResult? cleanupResult = null)
    {
        var detail = cleanupResult ??
            new CleanupResult(true, 0, false, false, true, true, true);
        var bytes = Encoding.UTF8.GetBytes(
            "{\"schemaVersion\":1,\"releaseKind\":\"UnsignedDev\"," +
            "\"trustBoundary\":\"localManualExactSha\",\"result\":\"" +
            result + "\",\"stage\":\"" + stage +
            "\",\"nativeExit\":" + exit + ",\"cleanup\":\"" +
            cleanup + "\",\"childPid\":" + detail.ChildPid +
            ",\"killAttempted\":" +
            (detail.KillAttempted ? "true" : "false") +
            ",\"rootFallbackAttempted\":" +
            (detail.RootFallbackAttempted ? "true" : "false") +
            ",\"waitCompleted\":" +
            (detail.WaitCompleted ? "true" : "false") +
            ",\"rootPidZero\":" +
            (detail.RootPidZero ? "true" : "false") +
            ",\"withinDeadline\":" +
            (detail.WithinDeadline ? "true" : "false") + "}");
        try { WriteEvidence(bytes); } catch { }
        try { Console.Error.Write(Encoding.UTF8.GetString(bytes)); } catch { }
        return exit;
    }

    private static void WriteEvidence(byte[] bytes)
    {
        var destination = Path.Combine(
            AppContext.BaseDirectory, EvidenceFileName);
        var temporary = destination + ".pending-" +
            Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, destination, true);
        if (!File.ReadAllBytes(destination).SequenceEqual(bytes))
            throw new InvalidOperationException("evidenceReadbackFailed");
    }

    private static DiagnosticProjection ParseDiagnostic(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("diagnosticSchemaInvalid");
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "schemaVersion", "releaseKind", "trustBoundary", "success",
            "resultCode", "stage", "nativeCategory", "nativeCode", "acl",
            "recovery", "probe", "cleanup", "aclMutationOccurred",
            "aclRollback"
        };
        foreach (var property in root.EnumerateObject())
            if (!expected.Remove(property.Name))
                throw new InvalidOperationException("diagnosticSchemaInvalid");
        if (expected.Count != 0 ||
            root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("releaseKind").GetString() != "UnsignedDev" ||
            root.GetProperty("trustBoundary").GetString() !=
                "localManualExactSha")
            throw new InvalidOperationException("diagnosticSchemaInvalid");
        _ = root.GetProperty("success").GetBoolean();
        _ = ReadClosedString(root, "acl");
        _ = ReadClosedString(root, "recovery");
        _ = ReadClosedString(root, "probe");
        _ = ReadClosedString(root, "cleanup");
        var nativeCode = root.GetProperty("nativeCode").GetInt32();
        if (nativeCode < 0)
            throw new InvalidOperationException("diagnosticSchemaInvalid");
        return new(
            ReadClosedString(root, "resultCode"),
            ReadClosedString(root, "stage"),
            ReadClosedString(root, "nativeCategory"),
            nativeCode,
            root.GetProperty("aclMutationOccurred").GetBoolean(),
            ReadClosedString(root, "aclRollback"));
    }

    private static string ReadClosedString(
        JsonElement root, string propertyName)
    {
        var value = root.GetProperty(propertyName).GetString();
        if (string.IsNullOrEmpty(value) || value.Length > 96 ||
            value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new InvalidOperationException("diagnosticSchemaInvalid");
        return value;
    }

    private readonly record struct DiagnosticProjection(
        string ResultCode,
        string Stage,
        string NativeCategory,
        int NativeCode,
        bool AclMutationOccurred,
        string AclRollback);

    private readonly record struct CleanupResult(
        bool Completed,
        int ChildPid,
        bool KillAttempted,
        bool RootFallbackAttempted,
        bool WaitCompleted,
        bool RootPidZero,
        bool WithinDeadline);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    private static byte[] ReadFrame(
        Stream stream, CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        ReadExactly(stream, prefix, cancellationToken);
        var length = BitConverter.ToInt32(prefix);
        if (length is <= 0 or > MaximumFrameBytes)
            throw new InvalidOperationException("frameInvalid");
        var bytes = new byte[length];
        ReadExactly(stream, bytes, cancellationToken);
        return bytes;
    }

    private static void WriteFrame(Stream stream, byte[] bytes)
    {
        if (bytes.Length is <= 0 or > MaximumFrameBytes)
            throw new InvalidOperationException("frameInvalid");
        Span<byte> prefix = stackalloc byte[4];
        BitConverter.TryWriteBytes(prefix, bytes.Length);
        stream.Write(prefix);
        stream.Write(bytes);
        stream.Flush();
    }

    private static void ReadExactly(
        Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var count = stream.ReadAsync(
                bytes.AsMemory(offset), cancellationToken)
                .AsTask().GetAwaiter().GetResult();
            if (count == 0) throw new EndOfStreamException();
            offset += count;
        }
    }

    private static string GetProcessUserSid(IntPtr processHandle)
    {
        if (!OpenProcessToken(processHandle, 0x0008, out var token))
            throw new InvalidOperationException("peerTokenUnavailable");
        try
        {
            GetTokenInformation(token, 1, IntPtr.Zero, 0, out var length);
            var buffer = Marshal.AllocHGlobal(checked((int)length));
            try
            {
                if (!GetTokenInformation(
                        token, 1, buffer, length, out _))
                    throw new InvalidOperationException(
                        "peerTokenUnavailable");
                return new SecurityIdentifier(
                    Marshal.ReadIntPtr(buffer)).Value;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseHandle(token); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe, out uint clientProcessId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(
        uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32First(
        IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32Next(
        IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle, int tokenInformationClass,
        IntPtr tokenInformation, uint tokenInformationLength,
        out uint returnLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
