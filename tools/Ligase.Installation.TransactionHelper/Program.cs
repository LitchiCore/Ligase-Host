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
    private const uint Synchronize = 0x00100000;
    private const uint WriteDac = 0x00040000;
    private const uint WriteOwner = 0x00080000;
    private const uint FileListDirectory = 0x00000001;
    private const uint FileReadAttributes = 0x00000080;
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
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidHandle = 6;
    private const int ErrorSharingViolation = 32;
    private const int ErrorNotSupported = 50;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorAlreadyExists = 183;
    private const int ErrorInvalidOwner = 1307;
    private const int ErrorPrivilegeNotHeld = 1314;
    private const int ErrorInvalidAcl = 1336;
    private const int FileDirectoryInformation = 1;
    private const int FileStreamInformation = 22;
    private const int StatusSuccess = 0;
    private const int StatusNoMoreFiles = unchecked((int)0x80000006);
    private const int NtQueryBufferBytes = 64 * 1024;
    private static string _nativeCategory = "none";
    private static int _nativeCode;
    private static bool _aclMutationOccurred;
    private static string _aclRollback = "notRequired";

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
                $"{{\"code\":\"{code}\",\"stage\":\"{_stage}\"," +
                $"\"nativeCategory\":\"{_nativeCategory}\"," +
                $"\"nativeCode\":{_nativeCode}," +
                $"\"aclMutationOccurred\":" +
                $"{_aclMutationOccurred.ToString().ToLowerInvariant()}," +
                $"\"aclRollback\":\"{_aclRollback}\"}}");
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

    private static InvalidOperationException NativeFailure(
        string code, int nativeCode)
    {
        _nativeCode = nativeCode is > 0 and <= ushort.MaxValue
            ? nativeCode
            : 0;
        _nativeCategory = nativeCode switch
        {
            ErrorFileNotFound => "fileNotFound",
            ErrorPathNotFound => "pathNotFound",
            ErrorAccessDenied => "accessDenied",
            ErrorInvalidHandle => "invalidHandle",
            ErrorSharingViolation => "busy",
            ErrorPrivilegeNotHeld => "privilegeNotHeld",
            ErrorInvalidOwner => "invalidOwner",
            ErrorInvalidAcl => "invalidAcl",
            ErrorNotSupported => "notSupported",
            ErrorInvalidParameter => "invalidParameter",
            _ => "unknown"
        };
        return new InvalidOperationException(code);
    }

    private static InvalidOperationException IdentityChanged()
    {
        _nativeCategory = "identityChanged";
        _nativeCode = 0;
        return new InvalidOperationException("installTransactionInvalid");
    }

    private static void InjectValidationNativeFailure(
        string behavior, int nativeCode)
    {
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") == "1" &&
            Environment.GetEnvironmentVariable(
                "LIGASE_TRANSACTION_TEST_BEHAVIOR") == behavior)
            throw NativeFailure("installTransactionUnavailable", nativeCode);
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
            "{\"code\":\"installTransactionPreflightReady\"," +
            "\"stage\":\"finalReadback\",\"recoveryAction\":\"" +
            store.RecoveryAction + "\"}");
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
        private readonly bool _recoveredEmptyAdminRoot;
        private string TransactionPath => Path.Combine(
            _root, "pending-install-transaction.json");

        private SecureStore(
            string root, SafeFileHandle handle, bool recoveredEmptyAdminRoot)
        {
            _root = root;
            _rootHandle = handle;
            _rootIdentity = VerifyHandle(handle, root, directory: true);
            _recoveredEmptyAdminRoot = recoveredEmptyAdminRoot;
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
                var recoveredEmptyAdminRoot = HardenDirectoryChain(
                    root,
                    test ? Path.GetDirectoryName(root)! : programData);
                return OpenVerified(root, recoveredEmptyAdminRoot);
            }
            if (!Directory.Exists(root))
                throw new InvalidOperationException("installTransactionUnavailable");
            return OpenVerified(root, false);
        }

        private static SecureStore OpenVerified(
            string root, bool recoveredEmptyAdminRoot)
        {
            var handle = OpenPath(root, directory: true, writeSecurity: false);
            try
            {
                SetStage("assertAcl");
                AssertAcl(handle, directory: true);
                SetStage("rejectReparse");
                RejectReparseChain(root, create: false);
                SetStage("finalReadback");
                return new SecureStore(root, handle, recoveredEmptyAdminRoot);
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

        public string RecoveryAction => _recoveredEmptyAdminRoot
            ? "recoverEmptyAdminRoot"
            : "none";

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

    private static bool HardenDirectoryChain(string root, string trustedBase)
    {
        var current = Path.GetFullPath(trustedBase).TrimEnd('\\');
        var relative = Path.GetRelativePath(current, root);
        var recoveredEmptyAdminRoot = false;
        var segmentIndex = 0;
        foreach (var part in relative.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            segmentIndex++;
            current = Path.Combine(current, part);
            var existed = Directory.Exists(current);
            if (!existed)
                CreateDirectoryWithExactAcl(current);
            using var handle = OpenPath(current, true, writeSecurity: false);
            var identity = VerifyHandle(handle, current, true);
            if (existed && segmentIndex == 1 &&
                IsCanonicalAdminRoot(current, trustedBase) &&
                !HasExactAcl(handle, directory: true))
            {
                handle.Dispose();
                byte[] originalSecurity;
                if (Environment.GetEnvironmentVariable(
                        "LIGASE_INSTALL_VALIDATION_HARNESS") == "1" &&
                    Environment.GetEnvironmentVariable(
                        "LIGASE_TRANSACTION_TEST_BEHAVIOR") ==
                    "holdRecoveryHandle")
                {
                    using var blocker = OpenPath(
                        current, directory: true, writeSecurity: false);
                    using var rejected = OpenRecoveryDirectory(current);
                    throw new InvalidOperationException(
                        "installTransactionInvalid");
                }
                using (var exclusive = OpenRecoveryDirectory(current))
                {
                    if (VerifyHandle(exclusive, current, true) != identity)
                        throw IdentityChanged();
                    VerifyEmptyAdminRoot(exclusive);
                    AttemptValidationResidueInjection(current);
                    VerifyEmptyAdminRoot(exclusive);
                    originalSecurity = ReadSecurityDescriptor(exclusive);
                    SetStage("applyAcl");
                    ApplyExactAcl(exclusive, true);
                    _aclMutationOccurred = true;
                    SetStage("finalReadback");
                    try
                    {
                        if (VerifyHandle(exclusive, current, true) != identity)
                            throw IdentityChanged();
                        VerifyEmptyAdminRoot(exclusive);
                    }
                    catch
                    {
                        _aclRollback = TryRestoreSecurityDescriptor(
                            exclusive, originalSecurity)
                            ? "completed"
                            : "failed";
                        throw;
                    }
                }
                try
                {
                    using var reopened = OpenPath(
                        current, directory: true, writeSecurity: false);
                    AssertAcl(reopened, directory: true);
                    if (VerifyHandle(reopened, current, true) != identity)
                        throw IdentityChanged();
                    VerifyEmptyAdminRoot(reopened);
                }
                catch
                {
                    _aclRollback = TryRestoreSecurityDescriptor(
                        current, identity, originalSecurity)
                        ? "completed"
                        : "failed";
                    throw;
                }
                recoveredEmptyAdminRoot = true;
                continue;
            }
            SetStage("assertAcl");
            AssertAcl(handle, true);
        }
        return recoveredEmptyAdminRoot;
    }

    private static bool IsCanonicalAdminRoot(string path, string trustedBase)
    {
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") == "1")
            return true;
        return string.Equals(
            Path.GetFullPath(path).TrimEnd('\\'),
            Path.Combine(Path.GetFullPath(trustedBase).TrimEnd('\\'),
                "Ligase Host Admin"),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void VerifyEmptyAdminRoot(SafeFileHandle handle)
    {
        if (!HasExpectedOwner(handle) ||
            HasDirectoryEntries(handle) ||
            HasAlternateDataStream(handle))
            throw new InvalidOperationException("installTransactionInvalid");
    }

    private static void AttemptValidationResidueInjection(string path)
    {
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") != "1")
            return;
        var behavior = Environment.GetEnvironmentVariable(
            "LIGASE_TRANSACTION_TEST_BEHAVIOR");
        try
        {
            if (behavior == "injectResidueChild")
                File.WriteAllText(Path.Combine(path, "injected.bin"), "x");
            else if (behavior == "injectResidueAds")
                File.WriteAllText(path + ":injected", "x");
        }
        catch (IOException)
        {
            // The exclusive directory handle is expected to reject the
            // concurrent write before the ACL transition.
        }
        catch (UnauthorizedAccessException)
        {
            // Equivalent fail-closed rejection by the current directory ACL.
        }
    }

    private static bool HasExpectedOwner(SafeFileHandle handle)
    {
        var result = GetSecurityInfo(handle, SeFileObject,
            OwnerSecurityInformation, out _, out _, out _, out _,
            out var descriptor);
        if (result != 0 || descriptor == IntPtr.Zero)
            throw NativeFailure(
                "installTransactionAclInvalid", checked((int)result));
        try
        {
            var length = GetSecurityDescriptorLength(descriptor);
            var binary = new byte[length];
            Marshal.Copy(descriptor, binary, 0, (int)length);
            var actual = new RawSecurityDescriptor(binary, 0);
            return actual.Owner == AdminSid;
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    private static byte[] ReadSecurityDescriptor(SafeFileHandle handle)
    {
        var result = GetSecurityInfo(handle, SeFileObject,
            OwnerSecurityInformation | DaclSecurityInformation,
            out _, out _, out _, out _, out var descriptor);
        if (result != 0 || descriptor == IntPtr.Zero)
            throw NativeFailure(
                "installTransactionAclInvalid", checked((int)result));
        try
        {
            var length = checked((int)GetSecurityDescriptorLength(descriptor));
            var binary = new byte[length];
            Marshal.Copy(descriptor, binary, 0, length);
            return binary;
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    private static bool TryRestoreSecurityDescriptor(
        SafeFileHandle handle, byte[] binary)
    {
        var pin = GCHandle.Alloc(binary, GCHandleType.Pinned);
        try
        {
            var descriptor = pin.AddrOfPinnedObject();
            if (!GetSecurityDescriptorOwner(
                    descriptor, out var owner, out _) ||
                !GetSecurityDescriptorDacl(
                    descriptor, out _, out var dacl, out _))
                return false;
            var result = SetSecurityInfo(handle, SeFileObject,
                OwnerSecurityInformation | DaclSecurityInformation,
                owner, IntPtr.Zero, dacl, IntPtr.Zero);
            if (result != 0)
                return false;
            var restored = ReadSecurityDescriptor(handle);
            return restored.SequenceEqual(binary);
        }
        catch
        {
            return false;
        }
        finally
        {
            pin.Free();
        }
    }

    private static bool TryRestoreSecurityDescriptor(
        string path, FileIdentity expectedIdentity, byte[] binary)
    {
        try
        {
            using var handle = OpenRecoveryDirectory(path);
            if (VerifyHandle(handle, path, directory: true) !=
                expectedIdentity)
                return false;
            return TryRestoreSecurityDescriptor(handle, binary);
        }
        catch
        {
            return false;
        }
    }

    private static void CreateDirectoryWithExactAcl(string path)
    {
        var security = BuildSecurity(directory: true);
        var binary = new byte[security.BinaryLength];
        security.GetBinaryForm(binary, 0);
        var descriptorPin = GCHandle.Alloc(binary, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptorPin.AddrOfPinnedObject(),
                InheritHandle = false
            };
            if (!CreateDirectoryW(path, ref attributes))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorAlreadyExists)
                    throw IdentityChanged();
                throw NativeFailure("installTransactionAclInvalid", error);
            }
        }
        finally
        {
            descriptorPin.Free();
        }
        FileIdentity? createdIdentity = null;
        try
        {
            using var handle = OpenPath(
                path, directory: true, writeSecurity: false);
            createdIdentity = VerifyHandle(handle, path, directory: true);
            SetStage("assertAcl");
            AssertAcl(handle, directory: true);
        }
        catch
        {
            if (createdIdentity is not null)
                TryCleanupCreatedDirectory(path, createdIdentity.Value);
            throw;
        }
    }

    private static void TryCleanupCreatedDirectory(
        string path, FileIdentity expectedIdentity)
    {
        try
        {
            using var handle = OpenPath(
                path, directory: true, writeSecurity: false);
            if (VerifyHandle(handle, path, directory: true) !=
                    expectedIdentity ||
                HasDirectoryEntries(handle) ||
                HasAlternateDataStream(handle))
                return;
            handle.Dispose();
            Directory.Delete(path, recursive: false);
        }
        catch
        {
            // A residue is safer than deleting an object whose identity or
            // contents can no longer be proven to be this transaction's.
        }
    }

    private static SafeFileHandle OpenRecoveryDirectory(string path)
    {
        var access = FileListDirectory | FileReadAttributes | ReadControl |
            WriteDac | WriteOwner | Synchronize;
        SetStage("openHandle");
        InjectValidationNativeFailure(
            "failOpenHandleAccessDenied", ErrorAccessDenied);
        var handle = CreateFileW(path, access, 0, IntPtr.Zero, OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw NativeFailure("installTransactionUnavailable", error);
        }
        return handle;
    }

    private static bool HasDirectoryEntries(SafeFileHandle handle)
    {
        var buffer = Marshal.AllocHGlobal(NtQueryBufferBytes);
        try
        {
            var restartScan = true;
            for (var query = 0; query < 4; query++)
            {
                var status = NtQueryDirectoryFile(
                    handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    out _, buffer, NtQueryBufferBytes,
                    FileDirectoryInformation, true, IntPtr.Zero, restartScan);
                restartScan = false;
                if (status == StatusNoMoreFiles)
                    return false;
                if (status != StatusSuccess)
                    throw NativeFailure(
                        "installTransactionUnavailable",
                        NtStatusToSafeWin32(status));
                var nameLength = Marshal.ReadInt32(buffer, 60);
                if (nameLength < 0 || nameLength > NtQueryBufferBytes - 64 ||
                    (nameLength & 1) != 0)
                    throw new InvalidOperationException(
                        "installTransactionInvalid");
                var name = Marshal.PtrToStringUni(
                    IntPtr.Add(buffer, 64), nameLength / 2);
                if (name is not "." and not "..")
                    return true;
            }
            throw new InvalidOperationException("installTransactionInvalid");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool HasAlternateDataStream(SafeFileHandle handle)
    {
        var buffer = Marshal.AllocHGlobal(NtQueryBufferBytes);
        try
        {
            var status = NtQueryInformationFile(
                handle, out _, buffer, NtQueryBufferBytes,
                FileStreamInformation);
            if (status != StatusSuccess)
                throw NativeFailure(
                    "installTransactionUnavailable",
                    NtStatusToSafeWin32(status));
            var offset = 0;
            for (var entry = 0; entry < 64; entry++)
            {
                if (offset < 0 || offset > NtQueryBufferBytes - 24)
                    throw new InvalidOperationException(
                        "installTransactionInvalid");
                var current = IntPtr.Add(buffer, offset);
                var next = Marshal.ReadInt32(current, 0);
                var nameLength = Marshal.ReadInt32(current, 4);
                if (nameLength < 0 ||
                    nameLength > NtQueryBufferBytes - offset - 24 ||
                    (nameLength & 1) != 0)
                    throw new InvalidOperationException(
                        "installTransactionInvalid");
                var name = Marshal.PtrToStringUni(
                    IntPtr.Add(current, 24), nameLength / 2);
                if (!string.Equals(name, "::$DATA",
                        StringComparison.OrdinalIgnoreCase))
                    return true;
                if (next == 0)
                    return false;
                if (next < 24 || (next & 7) != 0)
                    throw new InvalidOperationException(
                        "installTransactionInvalid");
                offset = checked(offset + next);
            }
            throw new InvalidOperationException("installTransactionInvalid");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int NtStatusToSafeWin32(int status)
    {
        var result = RtlNtStatusToDosError(status);
        return result > int.MaxValue ? 0 : checked((int)result);
    }

    private static SafeFileHandle OpenPath(
        string path, bool directory, bool writeSecurity,
        uint? shareMode = null)
    {
        var access = (directory
                ? FileListDirectory | FileReadAttributes | ReadControl |
                    Synchronize
                : GenericRead | ReadControl) |
            (writeSecurity ? WriteDac | WriteOwner : 0);
        var flags = FileFlagOpenReparsePoint |
            (directory ? FileFlagBackupSemantics : FileAttributeNormal);
        SetStage("openHandle");
        InjectValidationNativeFailure(
            "failOpenHandleAccessDenied", ErrorAccessDenied);
        var handle = CreateFileW(path, access,
            shareMode ?? (FileShareRead | FileShareWrite | FileShareDelete),
            IntPtr.Zero,
            OpenExisting, flags, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw NativeFailure("installTransactionUnavailable", error);
        }
        return handle;
    }

    private static FileIdentity VerifyHandle(
        SafeFileHandle handle, string expected, bool directory)
    {
        SetStage("verifyIdentity");
        InjectValidationNativeFailure(
            "failVerifyIdentityInvalidHandle", ErrorInvalidHandle);
        if (!GetFileInformationByHandle(handle, out var info))
            throw NativeFailure("installTransactionUnavailable",
                Marshal.GetLastWin32Error());
        if ((info.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0 ||
            directory !=
                ((info.FileAttributes & (uint)FileAttributes.Directory) != 0))
            throw IdentityChanged();
        SetStage("resolveFinalPath");
        InjectValidationNativeFailure(
            "failResolveFinalPathInvalidParameter", ErrorInvalidParameter);
        var builder = new StringBuilder(32768);
        if (GetFinalPathNameByHandleW(handle, builder, builder.Capacity, 0) == 0)
            throw NativeFailure("installTransactionUnavailable",
                Marshal.GetLastWin32Error());
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
                throw NativeFailure(
                    "installTransactionAclInvalid", checked((int)result));
        }
        finally { pin.Free(); }
    }

    private static CommonSecurityDescriptor BuildSecurity(bool directory)
    {
        var inheritance = directory
            ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
            : InheritanceFlags.None;
        var dacl = new DiscretionaryAcl(directory, false, 2);
        foreach (var sid in new[] { SystemSid, AdminSid })
            dacl.AddAccess(AccessControlType.Allow, sid,
                (int)FileSystemRights.FullControl, inheritance,
                PropagationFlags.None);
        return new(directory, false, ControlFlags.DiscretionaryAclProtected,
            AdminSid, AdminSid, null, dacl);
    }

    private static void AssertAcl(SafeFileHandle handle, bool directory)
    {
        if (!HasExactAcl(handle, directory))
            throw new InvalidOperationException("installTransactionAclInvalid");
    }

    private static bool HasExactAcl(SafeFileHandle handle, bool directory)
    {
        var result = GetSecurityInfo(handle, SeFileObject,
            OwnerSecurityInformation | DaclSecurityInformation,
            out _, out _, out _, out _, out var descriptor);
        if (result != 0 || descriptor == IntPtr.Zero)
            throw NativeFailure(
                "installTransactionAclInvalid", checked((int)result));
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
            return actualBytes.SequenceEqual(expectedBytes);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public IntPtr Status;
        public IntPtr Information;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateDirectoryW(
        string path, ref SecurityAttributes securityAttributes);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryDirectoryFile(
        SafeFileHandle fileHandle, IntPtr eventHandle, IntPtr apcRoutine,
        IntPtr apcContext, out IoStatusBlock ioStatusBlock,
        IntPtr fileInformation, int length, int fileInformationClass,
        [MarshalAs(UnmanagedType.U1)] bool returnSingleEntry,
        IntPtr fileName, [MarshalAs(UnmanagedType.U1)] bool restartScan);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationFile(
        SafeFileHandle fileHandle, out IoStatusBlock ioStatusBlock,
        IntPtr fileInformation, int length, int fileInformationClass);
    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);
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
