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
    private const int MaximumFrameBytes = 4096;
    private const string ChildFileName = "Ligase.SecureStore.Preflight.exe";
    private const string EvidenceFileName =
        "secure-store-preflight-review-evidence.json";

    private static int Main(string[] args)
    {
        if (args.Length != 0)
            return Fail("invalidArguments", "inputValidation", 18);

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
            AppContext.BaseDirectory, ChildFileName);
        if (!File.Exists(childPath) ||
            (File.GetAttributes(childPath) & FileAttributes.ReparsePoint) != 0)
            return Fail("childArtifactUnavailable", "inputValidation", 18);

        var start = new ProcessStartInfo
        {
            FileName = childPath,
            Arguments = "--diagnostic-pipe " + pipeName +
                " --nonce " + nonce +
                " --parent-pid " + parentPid,
            UseShellExecute = true,
            Verb = "runas",
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
                TimeoutMilliseconds);
            pipe.WaitForConnectionAsync(deadline.Token)
                .GetAwaiter().GetResult();
            if (!GetNamedPipeClientProcessId(
                    pipe.SafePipeHandle, out var clientPid) ||
                clientPid != (uint)child.Id ||
                child.SessionId != Process.GetCurrentProcess().SessionId ||
                !string.Equals(
                    GetProcessUserSid(child.Handle), userSid.Value,
                    StringComparison.Ordinal))
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
            return child.ExitCode;
        }
        catch
        {
            if (child is not null && !child.HasExited)
            {
                try
                {
                    child.Kill(entireProcessTree: true);
                    child.WaitForExit(2000);
                }
                catch
                {
                    return Fail("childCleanupFailed", "cleanup", 18);
                }
            }
            return Fail("diagnosticChannelFailed", "ipc", 18);
        }
        finally
        {
            child?.Dispose();
        }
    }

    private static int Fail(string result, string stage, int exit)
    {
        var bytes = Encoding.UTF8.GetBytes(
            "{\"schemaVersion\":1,\"releaseKind\":\"UnsignedDev\"," +
            "\"trustBoundary\":\"localManualExactSha\",\"result\":\"" +
            result + "\",\"stage\":\"" + stage +
            "\",\"nativeExit\":" + exit + "}");
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
