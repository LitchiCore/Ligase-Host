using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static class Program
{
    private static string _stage = "resolveProgramData";
    private const int MaxBytes = 4 * 1024 * 1024 + 64 * 1024;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ReadControl = 0x00020000;
    private const uint WriteDac = 0x00040000;
    private const uint WriteOwner = 0x00080000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint FileShareDelete = 4;
    private const uint OpenExisting = 3;
    private const uint CreateNew = 1;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileAttributeNormal = 0x80;
    private const uint MoveReplaceExisting = 1;
    private const uint MoveWriteThrough = 8;
    private const int SeFileObject = 1;
    private const uint OwnerSecurityInformation = 1;
    private const uint DaclSecurityInformation = 4;
    private const uint ProtectedDaclSecurityInformation = 0x80000000;

    private static readonly SecurityIdentifier AdminSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier SystemSid =
        new(WellKnownSidType.LocalSystemSid, null);

    public static int Main(string[] args)
    {
        try
        {
            ApplyValidationHarnessBehavior();
            if (args.Length is < 1 or > 3)
                throw new InvalidOperationException("invalidArguments");
            var action = args[0];
            SetStage("resolveProgramData");
            var root = ResolveRoot(args);
            SetStage("rejectReparse");
            using var store = SecureStore.Open(
                root, create: action is "write" or "preflight");
            return action switch
            {
                "write" => Write(store),
                "read" => Read(store),
                "delete" => Delete(store),
                "validate" => Validate(store),
                "preflight" => Preflight(store),
                _ => throw new InvalidOperationException("invalidArguments")
            };
        }
        catch (Exception exception)
        {
            var code = exception.Message switch
            {
                "installTransactionAclInvalid" => exception.Message,
                "installTransactionUnavailable" => exception.Message,
                _ => "installTransactionInvalid"
            };
            Console.Error.Write(
                $"{{\"code\":\"{code}\",\"stage\":\"{_stage}\"}}");
            return 18;
        }
    }

    private static void ApplyValidationHarnessBehavior()
    {
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") != "1")
            return;

        switch (Environment.GetEnvironmentVariable(
                    "LIGASE_TRANSACTION_TEST_BEHAVIOR"))
        {
            case "hang":
                Thread.Sleep(Timeout.Infinite);
                break;
            case "hangBeforeStdinRead":
                Thread.Sleep(Timeout.Infinite);
                break;
            case "delayedStdinRead":
                Thread.Sleep(250);
                _ = ReadBounded(Console.OpenStandardInput());
                Console.Out.Write(
                    "{\"code\":\"installTransactionWritten\",\"success\":true}");
                Console.Out.Flush();
                Environment.Exit(0);
                return;
            case "delayedPipe":
                Console.Out.Write("pending");
                Console.Out.Flush();
                Thread.Sleep(Timeout.Infinite);
                break;
            case "oversizeStdout":
                Console.Out.Write(new string('x', 131072));
                Console.Out.Flush();
                throw new InvalidOperationException(
                    "installTransactionUnavailable");
            case "oversizeStderr":
                Console.Error.Write(new string('x', 131072));
                Console.Error.Flush();
                throw new InvalidOperationException(
                    "installTransactionUnavailable");
            case "killTree":
                using (var child = Process.Start(new ProcessStartInfo(
                    Environment.ProcessPath!)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Environment =
                    {
                        ["LIGASE_INSTALL_VALIDATION_HARNESS"] = "1",
                        ["LIGASE_TRANSACTION_TEST_BEHAVIOR"] = "hang"
                    }
                }))
                {
                    child?.WaitForExit();
                }
                break;
        }
    }

    private static void SetStage(string stage)
    {
        _stage = stage;
        if (Environment.GetEnvironmentVariable(
                "LIGASE_TRANSACTION_FAILURE_STAGE") == stage &&
            Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") == "1")
            throw new InvalidOperationException("installTransactionUnavailable");
    }

    private static string ResolveRoot(string[] args)
    {
        if (args.Length == 1)
        {
            var validationRoot = Environment.GetEnvironmentVariable(
                "LIGASE_TRANSACTION_TEST_ROOT");
            if (Environment.GetEnvironmentVariable(
                    "LIGASE_INSTALL_VALIDATION_HARNESS") == "1" &&
                !string.IsNullOrWhiteSpace(validationRoot))
                return Path.GetFullPath(validationRoot);
            var common = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(common))
                throw new InvalidOperationException("installTransactionUnavailable");
            return Path.Combine(common, "Ligase Host Admin", "Transactions");
        }
        if (args.Length != 3 || args[1] != "--test-root" ||
            Environment.GetEnvironmentVariable("LIGASE_INSTALL_VALIDATION_HARNESS") != "1")
            throw new InvalidOperationException("installTransactionInvalid");
        return Path.GetFullPath(args[2]);
    }

    private static int Write(SecureStore store)
    {
        var bytes = ReadBounded(Console.OpenStandardInput());
        store.Write(bytes);
        Console.Write("{\"code\":\"installTransactionWritten\",\"success\":true}");
        return 0;
    }

    private static int Read(SecureStore store)
    {
        var bytes = store.Read();
        Console.OpenStandardOutput().Write(bytes);
        return 0;
    }

    private static int Delete(SecureStore store)
    {
        store.Delete();
        Console.Write("{\"code\":\"installTransactionDeleted\",\"success\":true}");
        return 0;
    }

    private static int Validate(SecureStore store)
    {
        store.Validate();
        Console.Write("{\"code\":\"installTransactionStoreValid\",\"success\":true}");
        return 0;
    }

    private static int Preflight(SecureStore store)
    {
        store.Preflight();
        Console.Write(
            "{\"code\":\"installTransactionPreflightReady\",\"stage\":\"finalReadback\"}");
        return 0;
    }

    private static byte[] ReadBounded(Stream input)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = input.Read(buffer);
            if (count == 0) break;
            if (output.Length + count > MaxBytes)
                throw new InvalidOperationException("installTransactionInvalid");
            output.Write(buffer, 0, count);
        }
        if (output.Length == 0)
            throw new InvalidOperationException("installTransactionInvalid");
        return output.ToArray();
    }

    private sealed class SecureStore : IDisposable
    {
        private readonly string _root;
        private readonly SafeFileHandle _rootHandle;
        private readonly FileIdentity _rootIdentity;
        private string TransactionPath => Path.Combine(
            _root, "pending-install-transaction.json");

        private SecureStore(string root, SafeFileHandle handle)
        {
            _root = root;
            _rootHandle = handle;
            _rootIdentity = VerifyHandle(handle, root, directory: true);
        }

        public static SecureStore Open(string root, bool create)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            var programData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
            var test = Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") == "1";
            if (!test && !root.StartsWith(
                    Path.GetFullPath(programData).TrimEnd('\\') + "\\",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("installTransactionInvalid");

            SetStage("rejectReparse");
            RejectReparseChain(root, create);
            if (create)
            {
                SetStage("createSegment");
                HardenDirectoryChain(
                    root,
                    test ? Path.GetDirectoryName(root)! : programData);
            }
            if (!Directory.Exists(root))
                throw new InvalidOperationException("installTransactionUnavailable");

            SetStage("openSegment");
            var handle = OpenPath(root, directory: true, writeSecurity: false);
            try
            {
                SetStage("assertAcl");
                AssertAcl(handle, directory: true);
                SetStage("rejectReparse");
                RejectReparseChain(root, create: false);
                SetStage("finalReadback");
                return new SecureStore(root, handle);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        public void Write(byte[] bytes)
        {
            ValidateRoot();
            var temp = Path.Combine(_root, ".pending-" + Guid.NewGuid().ToString("N"));
            SafeFileHandle? tempHandle = null;
            try
            {
                SetStage("createTemp");
                tempHandle = CreateFileW(temp, GenericRead | GenericWrite | ReadControl |
                    WriteDac | WriteOwner, 0, IntPtr.Zero, CreateNew,
                    FileAttributeNormal | FileFlagOpenReparsePoint, IntPtr.Zero);
                if (tempHandle.IsInvalid)
                    throw new InvalidOperationException("installTransactionInvalid");
                SetStage("applyAcl");
                ApplyExactAcl(tempHandle, directory: false);
                SetStage("assertAcl");
                AssertAcl(tempHandle, directory: false);
                var identity = VerifyHandle(tempHandle, temp, directory: false);
                using (var stream = new FileStream(tempHandle, FileAccess.Write, 8192, false))
                {
                    tempHandle = null;
                    stream.Write(bytes);
                    stream.Flush(true);
                }
                using (var verify = OpenPath(temp, directory: false, writeSecurity: false))
                {
                    AssertAcl(verify, directory: false);
                    if (VerifyHandle(verify, temp, false) != identity)
                        throw new InvalidOperationException("installTransactionInvalid");
                }
                ValidateRoot();
                SetStage("atomicReplace");
                if (!MoveFileExW(temp, TransactionPath,
                        MoveReplaceExisting | MoveWriteThrough))
                    throw new InvalidOperationException("installTransactionInvalid");
                SetStage("finalReadback");
                using var final = OpenPath(TransactionPath, false, false);
                AssertAcl(final, false);
                VerifyHandle(final, TransactionPath, false);
                ValidateRoot();
            }
            finally
            {
                tempHandle?.Dispose();
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        public byte[] Read()
        {
            SetStage("read");
            ValidateRoot();
            using var handle = OpenPath(TransactionPath, false, false);
            AssertAcl(handle, false);
            var before = VerifyHandle(handle, TransactionPath, false);
            byte[] bytes;
            using (var stream = new FileStream(handle, FileAccess.Read, 8192, false))
            {
                bytes = ReadBounded(stream);
            }
            using var afterHandle = OpenPath(TransactionPath, false, false);
            AssertAcl(afterHandle, false);
            var after = VerifyHandle(afterHandle, TransactionPath, false);
            if (before != after)
                throw new InvalidOperationException("installTransactionInvalid");
            ValidateRoot();
            return bytes;
        }

        public void Delete()
        {
            SetStage("delete");
            ValidateRoot();
            if (!File.Exists(TransactionPath)) return;
            using var handle = OpenPath(TransactionPath, false, false);
            AssertAcl(handle, false);
            VerifyHandle(handle, TransactionPath, false);
            handle.Dispose();
            File.Delete(TransactionPath);
            ValidateRoot();
        }

        public void Validate()
        {
            SetStage("finalReadback");
            ValidateRoot();
            if (File.Exists(TransactionPath))
            {
                using var handle = OpenPath(TransactionPath, false, false);
                AssertAcl(handle, false);
                VerifyHandle(handle, TransactionPath, false);
            }
        }

        public void Preflight()
        {
            ValidateRoot();
            var suffix = Guid.NewGuid().ToString("N");
            var temporary = Path.Combine(_root, ".preflight-temp-" + suffix);
            var probe = Path.Combine(_root, ".preflight-final-" + suffix);
            var expected = Encoding.UTF8.GetBytes(
                "{\"schemaVersion\":1,\"probe\":\"transactionStore\"}");
            SafeFileHandle? handle = null;
            try
            {
                SetStage("createTemp");
                handle = CreateFileW(temporary,
                    GenericRead | GenericWrite | ReadControl | WriteDac | WriteOwner,
                    0, IntPtr.Zero, CreateNew,
                    FileAttributeNormal | FileFlagOpenReparsePoint, IntPtr.Zero);
                if (handle.IsInvalid)
                    throw new InvalidOperationException("installTransactionUnavailable");
                SetStage("applyAcl");
                ApplyExactAcl(handle, directory: false);
                SetStage("assertAcl");
                AssertAcl(handle, directory: false);
                var before = VerifyHandle(handle, temporary, false);
                using (var stream = new FileStream(handle, FileAccess.ReadWrite, 4096, false))
                {
                    handle = null;
                    stream.Write(expected);
                    stream.Flush(true);
                }
                SetStage("atomicReplace");
                if (!MoveFileExW(
                        temporary, probe, MoveReplaceExisting | MoveWriteThrough))
                    throw new InvalidOperationException("installTransactionInvalid");
                SetStage("read");
                using (var readHandle = OpenPath(probe, false, false))
                {
                    AssertAcl(readHandle, false);
                    if (VerifyHandle(readHandle, probe, false) != before)
                        throw new InvalidOperationException("installTransactionInvalid");
                    using var stream = new FileStream(
                        readHandle, FileAccess.Read, 4096, false);
                    if (!ReadBounded(stream).SequenceEqual(expected))
                        throw new InvalidOperationException("installTransactionInvalid");
                }
                SetStage("delete");
                File.Delete(probe);
                SetStage("finalReadback");
                if (File.Exists(probe))
                    throw new InvalidOperationException("installTransactionInvalid");
                ValidateRoot();
            }
            finally
            {
                handle?.Dispose();
                if (File.Exists(temporary))
                    File.Delete(temporary);
                if (File.Exists(probe))
                    File.Delete(probe);
            }
        }

        private void ValidateRoot()
        {
            SetStage("assertAcl");
            AssertAcl(_rootHandle, true);
            SetStage("finalReadback");
            if (VerifyHandle(_rootHandle, _root, true) != _rootIdentity)
                throw new InvalidOperationException("installTransactionInvalid");
            SetStage("rejectReparse");
            RejectReparseChain(_root, false);
            SetStage("finalReadback");
        }

        public void Dispose() => _rootHandle.Dispose();
    }

    private static void RejectReparseChain(string path, bool create)
    {
        var current = Path.GetPathRoot(path)!;
        foreach (var part in path[current.Length..].Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (!Directory.Exists(current))
            {
                if (create) continue;
                throw new InvalidOperationException("installTransactionUnavailable");
            }
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("installTransactionInvalid");
        }
    }

    private static void HardenDirectoryChain(string root, string trustedBase)
    {
        var current = Path.GetFullPath(trustedBase).TrimEnd('\\');
        var relative = Path.GetRelativePath(current, root);
        foreach (var part in relative.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var existed = Directory.Exists(current);
            if (!existed)
                Directory.CreateDirectory(current);
            SetStage("openSegment");
            using var handle = OpenPath(current, true, writeSecurity: true);
            VerifyHandle(handle, current, true);
            if (!existed)
            {
                SetStage("applyAcl");
                ApplyExactAcl(handle, true);
            }
            SetStage("assertAcl");
            AssertAcl(handle, true);
        }
    }

    private static SafeFileHandle OpenPath(
        string path, bool directory, bool writeSecurity)
    {
        var access = GenericRead | ReadControl |
            (writeSecurity ? WriteDac | WriteOwner : 0);
        var flags = FileFlagOpenReparsePoint |
            (directory ? FileFlagBackupSemantics : FileAttributeNormal);
        var handle = CreateFileW(path, access,
            FileShareRead | FileShareWrite | FileShareDelete, IntPtr.Zero,
            OpenExisting, flags, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new InvalidOperationException("installTransactionUnavailable");
        return handle;
    }

    private static FileIdentity VerifyHandle(
        SafeFileHandle handle, string expected, bool directory)
    {
        if (!GetFileInformationByHandle(handle, out var info) ||
            (info.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0 ||
            directory != ((info.FileAttributes & (uint)FileAttributes.Directory) != 0))
            throw new InvalidOperationException("installTransactionInvalid");
        var builder = new StringBuilder(32768);
        if (GetFinalPathNameByHandleW(handle, builder, builder.Capacity, 0) == 0)
            throw new InvalidOperationException("installTransactionInvalid");
        var final = builder.ToString();
        if (final.StartsWith(@"\\?\", StringComparison.Ordinal)) final = final[4..];
        if (!string.Equals(Path.GetFullPath(final).TrimEnd('\\'),
                Path.GetFullPath(expected).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("installTransactionInvalid");
        return new(info.VolumeSerialNumber,
            ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }

    private static void ApplyExactAcl(SafeFileHandle handle, bool directory)
    {
        var security = BuildSecurity(directory);
        var binary = new byte[security.BinaryLength];
        security.GetBinaryForm(binary, 0);
        var pin = GCHandle.Alloc(binary, GCHandleType.Pinned);
        try
        {
            var descriptor = pin.AddrOfPinnedObject();
            if (!GetSecurityDescriptorOwner(descriptor, out var owner, out _) ||
                !GetSecurityDescriptorDacl(descriptor, out _, out var dacl, out _))
                throw new InvalidOperationException("installTransactionAclInvalid");
            var result = SetSecurityInfo(handle, SeFileObject,
                OwnerSecurityInformation | DaclSecurityInformation |
                ProtectedDaclSecurityInformation,
                owner, IntPtr.Zero, dacl, IntPtr.Zero);
            if (result != 0)
                throw new InvalidOperationException("installTransactionAclInvalid");
        }
        finally { pin.Free(); }
    }

    private static CommonSecurityDescriptor BuildSecurity(bool directory)
    {
        var inheritance = directory
            ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
            : InheritanceFlags.None;
        var dacl = new DiscretionaryAcl(false, false, 2);
        foreach (var sid in new[] { SystemSid, AdminSid })
            dacl.AddAccess(AccessControlType.Allow, sid,
                (int)FileSystemRights.FullControl, inheritance,
                PropagationFlags.None);
        return new(false, false, ControlFlags.DiscretionaryAclProtected,
            AdminSid, AdminSid, null, dacl);
    }

    private static void AssertAcl(SafeFileHandle handle, bool directory)
    {
        var result = GetSecurityInfo(handle, SeFileObject,
            OwnerSecurityInformation | DaclSecurityInformation,
            out _, out _, out _, out _, out var descriptor);
        if (result != 0 || descriptor == IntPtr.Zero)
            throw new InvalidOperationException("installTransactionAclInvalid");
        try
        {
            var length = GetSecurityDescriptorLength(descriptor);
            var binary = new byte[length];
            Marshal.Copy(descriptor, binary, 0, (int)length);
            var actual = new RawSecurityDescriptor(binary, 0);
            var expected = BuildSecurity(directory);
            var expectedBytes = new byte[expected.BinaryLength];
            expected.GetBinaryForm(expectedBytes, 0);
            var actualBytes = new byte[actual.BinaryLength];
            actual.GetBinaryForm(actualBytes, 0);
            if (!actualBytes.SequenceEqual(expectedBytes))
                throw new InvalidOperationException("installTransactionAclInvalid");
        }
        finally { LocalFree(descriptor); }
    }

    private readonly record struct FileIdentity(uint Volume, ulong FileId);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName,
        uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle, out ByHandleFileInformation information);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle handle, StringBuilder path, int length, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileExW(
        string existing, string destination, uint flags);
    [DllImport("advapi32.dll")]
    private static extern uint SetSecurityInfo(SafeFileHandle handle,
        int objectType, uint securityInfo, IntPtr owner, IntPtr group,
        IntPtr dacl, IntPtr sacl);
    [DllImport("advapi32.dll")]
    private static extern uint GetSecurityInfo(SafeFileHandle handle,
        int objectType, uint securityInfo, out IntPtr owner, out IntPtr group,
        out IntPtr dacl, out IntPtr sacl, out IntPtr securityDescriptor);
    [DllImport("advapi32.dll")]
    private static extern bool GetSecurityDescriptorOwner(
        IntPtr descriptor, out IntPtr owner, out bool defaulted);
    [DllImport("advapi32.dll")]
    private static extern bool GetSecurityDescriptorDacl(
        IntPtr descriptor, out bool present, out IntPtr dacl, out bool defaulted);
    [DllImport("advapi32.dll")]
    private static extern uint GetSecurityDescriptorLength(IntPtr descriptor);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
