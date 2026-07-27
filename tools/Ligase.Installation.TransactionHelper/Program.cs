using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static class Program
{
    private static string _stage = "inputValidation";
    private static string _recoveryAction = "notAttempted";
#if PREFLIGHT_ONLY
    private static NamedPipeClientStream? _diagnosticPipe;
#endif
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
    private const uint VolumeNameNt = 0x2;
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
    private const int ErrorChildProcessBlocked = 367;
    private const int ErrorHandleEof = 38;
    private const int StreamQueryWithoutNativeCode = 20015;
    private const int ErrorNotSupported = 50;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorAlreadyExists = 183;
    private const int ErrorInvalidOwner = 1307;
    private const int ErrorPrivilegeNotHeld = 1314;
    private const int ErrorInvalidAcl = 1336;
    private const int FileDirectoryInformation = 1;
    private const int FileStreamInfo = 7;
    private const int ProcessChildProcessPolicy = 13;
    private const uint NoChildProcessCreation = 1;
#if PREFLIGHT_VALIDATION
    private const string ValidationChildPolicySchema =
        "validationChildPolicyV1";
    private const string ValidationChildCleanupSchema =
        "validationChildCleanupV1";
    private const string ValidationChildFailureSchema =
        "validationChildFailureV1";
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateSuspended = 0x00000004;
    private const uint WaitObject0 = 0;
    private const string ValidationArgvToken = "--validate-argv-token";
    private const string ValidationChildPolicy = "--validate-child-policy";
    private const string ValidationHangPipes = "--validate-hang-pipes";
    private const string ValidationPolicySetFault =
        "--validate-policy-set-fault";
    private const string ValidationPolicyReadbackMismatch =
        "--validate-policy-readback-mismatch";
    private const string ValidationDiagnosticAction =
        "--validation-action";
    private const string ValidationDiagnosticSuccess =
        "ipcSuccess";
    private const string ValidationDiagnosticEarlyFailure =
        "earlyFailure";
    private const string ValidationDiagnosticNonceMismatch =
        "nonceMismatch";
    private const string ValidationDiagnosticServerPidMismatch =
        "serverPidMismatch";
    private const string ValidationDiagnosticServerSessionMismatch =
        "serverSessionMismatch";
    private const string ValidationDiagnosticServerSidMismatch =
        "serverSidMismatch";
    private const string ValidationDiagnosticFrameOversize =
        "frameOversize";
    private const string ValidationDiagnosticDisconnect =
        "disconnect";
    private const string ValidationDiagnosticTimeout =
        "timeout";
    private const string ValidationDiagnosticTimeoutTree =
        "timeoutTree";
    private const string ValidationDiagnosticTreeLeaf =
        "--validation-tree-leaf";
    private static string _preflightValidationAction = "";
    private static string _preflightDiagnosticValidationAction = "";
    private static int _preflightValidationChildPid;
    private static string _preflightValidationChildCleanup = "notRequired";
    private static IntPtr _preflightValidationChildProcess;
    private static IntPtr _preflightValidationChildThread;
#endif
    private const int StatusSuccess = 0;
    private const int StatusNoMoreFiles = unchecked((int)0x80000006);
    private const int NtQueryBufferBytes = 64 * 1024;
    private static string _nativeCategory = "none";
    private static int _nativeCode;
    private static bool _aclMutationOccurred;
    private static string _aclRollback = "notRequired";
    private static string _bindingReason = "none";
    private static string _bindingRootKind = "none";
    private static int _bindingSegmentCount;
    private static bool _bindingPrefixMatched;
    private static bool _bindingVolumeMatched;
    private static bool _bindingFileIdentityMatched;
    private static int _bindingAttempt;
    private static string _aclInspectionReason = "none";
    private static string _emptyRootInspectionReason = "none";

    private static readonly SecurityIdentifier AdminSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier SystemSid =
        new(WellKnownSidType.LocalSystemSid, null);

    public static int Main(string[] args)
    {
#if PREFLIGHT_ONLY
        return RunStandaloneSecureStorePreflight(args);
#else
        try
        {
            ApplyValidationHarnessBehavior();
            if (args.Length is < 1 or > 3)
                throw new InvalidOperationException("invalidArguments");
            var action = args[0];
            SetStage("resolveProgramData");
            if (action == "inspectSystemBinding")
                return InspectSystemBinding();
            if (action == "inspectSystemAcl")
                return InspectSystemAcl();
            if (action == "inspectSystemEmptyRoot")
                return InspectSystemEmptyRoot();
            if (action == "inspectEmptyRoot")
                return InspectEmptyRoot(args);
            if (action == "inspectSequentialBinding")
                return InspectSequentialBinding(args);
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
            var failureJson =
                $"{{\"code\":\"{code}\",\"stage\":\"{_stage}\"," +
                $"\"nativeCategory\":\"{_nativeCategory}\"," +
                $"\"nativeCode\":{_nativeCode}," +
                $"\"bindingReason\":\"{_bindingReason}\"," +
                $"\"bindingRootKind\":\"{_bindingRootKind}\"," +
                $"\"bindingSegmentCount\":{_bindingSegmentCount}," +
                $"\"bindingPrefixMatched\":" +
                $"{_bindingPrefixMatched.ToString().ToLowerInvariant()}," +
                $"\"bindingVolumeMatched\":" +
                $"{_bindingVolumeMatched.ToString().ToLowerInvariant()}," +
                $"\"bindingFileIdentityMatched\":" +
                $"{_bindingFileIdentityMatched.ToString().ToLowerInvariant()}," +
                $"\"aclInspectionReason\":\"{_aclInspectionReason}\"," +
                $"\"emptyRootInspectionReason\":" +
                $"\"{_emptyRootInspectionReason}\"," +
                $"\"aclMutationOccurred\":" +
                $"{_aclMutationOccurred.ToString().ToLowerInvariant()}," +
                $"\"aclRollback\":\"{_aclRollback}\"}}";
            var validationBehavior = Environment.GetEnvironmentVariable(
                "LIGASE_TRANSACTION_TEST_BEHAVIOR");
            failureJson = validationBehavior switch
            {
                "emitDuplicateCode" => failureJson.Replace(
                    "{\"code\":",
                    "{\"code\":\"installTransactionUnavailable\",\"code\":",
                    StringComparison.Ordinal),
                "emitDuplicateCodeLastConflicting" => failureJson.Replace(
                    "\"stage\":",
                    "\"code\":\"installTransactionInvalid\",\"stage\":",
                    StringComparison.Ordinal),
                "emitDuplicateNativeCode" => failureJson.Replace(
                    "\"nativeCode\":",
                    "\"nativeCode\":5,\"nativeCode\":",
                    StringComparison.Ordinal),
                "emitDuplicateBindingReason" => failureJson.Replace(
                    "\"bindingReason\":",
                    "\"bindingReason\":\"segmentMismatch\"," +
                    "\"bindingReason\":",
                    StringComparison.Ordinal),
                "emitDuplicateBindingRootKind" => failureJson.Replace(
                    "\"bindingRootKind\":",
                    "\"bindingRootKind\":\"device\",\"bindingRootKind\":",
                    StringComparison.Ordinal),
                "emitDuplicateBindingSegmentCount" => failureJson.Replace(
                    "\"bindingSegmentCount\":",
                    "\"bindingSegmentCount\":1,\"bindingSegmentCount\":",
                    StringComparison.Ordinal),
                "emitDuplicateBindingPrefixMatched" => failureJson.Replace(
                    "\"bindingPrefixMatched\":",
                    "\"bindingPrefixMatched\":true," +
                    "\"bindingPrefixMatched\":",
                    StringComparison.Ordinal),
                "emitDuplicateBindingVolumeMatched" => failureJson.Replace(
                    "\"bindingVolumeMatched\":",
                    "\"bindingVolumeMatched\":true," +
                    "\"bindingVolumeMatched\":",
                    StringComparison.Ordinal),
                "emitDuplicateBindingFileIdentityMatched" =>
                    failureJson.Replace(
                        "\"bindingFileIdentityMatched\":",
                        "\"bindingFileIdentityMatched\":true," +
                        "\"bindingFileIdentityMatched\":",
                        StringComparison.Ordinal),
                "emitDuplicateAclInspectionReason" =>
                    failureJson.Replace(
                        "\"aclInspectionReason\":",
                        "\"aclInspectionReason\":\"descriptorParseFailed\"," +
                        "\"aclInspectionReason\":",
                        StringComparison.Ordinal),
                "emitDuplicateEmptyRootInspectionReason" =>
                    failureJson.Replace(
                        "\"emptyRootInspectionReason\":",
                        "\"emptyRootInspectionReason\":" +
                        "\"ownerNotAdministrators\"," +
                        "\"emptyRootInspectionReason\":",
                        StringComparison.Ordinal),
                "emitAclTupleWrongStage" => failureJson.Replace(
                    "\"stage\":\"canonicalRoot\"",
                    "\"stage\":\"delete\"",
                    StringComparison.Ordinal),
                "emitAclTupleWrongCode" => failureJson.Replace(
                    "\"nativeCode\":20001",
                    "\"nativeCode\":20007",
                    StringComparison.Ordinal),
                "emitAclTupleWrongReason" => failureJson.Replace(
                    "\"aclInspectionReason\":\"canonicalRootInspectionFailed\"",
                    "\"aclInspectionReason\":\"securityDescriptorReadFailed\"",
                    StringComparison.Ordinal),
                "emitAclTupleCrossSplice" => failureJson
                    .Replace(
                        "\"stage\":\"canonicalRoot\"",
                        "\"stage\":\"descriptorCopy\"",
                        StringComparison.Ordinal)
                    .Replace(
                        "\"nativeCode\":20001",
                        "\"nativeCode\":20005",
                        StringComparison.Ordinal)
                    .Replace(
                        "\"aclInspectionReason\":" +
                        "\"canonicalRootInspectionFailed\"",
                        "\"aclInspectionReason\":" +
                        "\"descriptorCompareFailed\"",
                        StringComparison.Ordinal),
                "emitEmptyRootTupleWrongStage" => failureJson.Replace(
                    "\"stage\":\"inspectEmptyRootOwner\"",
                    "\"stage\":\"delete\"",
                    StringComparison.Ordinal),
                "emitEmptyRootTupleWrongCode" => failureJson.Replace(
                    "\"nativeCode\":20008",
                    "\"nativeCode\":20011",
                    StringComparison.Ordinal),
                "emitEmptyRootTupleWrongReason" => failureJson.Replace(
                    "\"emptyRootInspectionReason\":" +
                    "\"ownerNotAdministrators\"",
                    "\"emptyRootInspectionReason\":\"childEntryPresent\"",
                    StringComparison.Ordinal),
                "emitEmptyRootTupleCrossSplice" => failureJson
                    .Replace(
                        "\"stage\":\"inspectEmptyRootOwner\"",
                        "\"stage\":\"inspectEmptyRootStreams\"",
                        StringComparison.Ordinal)
                    .Replace(
                        "\"nativeCode\":20008",
                        "\"nativeCode\":20010",
                        StringComparison.Ordinal)
                    .Replace(
                        "\"emptyRootInspectionReason\":" +
                        "\"ownerNotAdministrators\"",
                        "\"emptyRootInspectionReason\":" +
                        "\"streamMetadataInvalid\"",
                        StringComparison.Ordinal),
                _ => failureJson
            };
            Console.Error.Write(failureJson);
            return 18;
        }
#endif
    }

#if PREFLIGHT_ONLY
    private const string PreflightReleaseKind = "UnsignedDev";
    private const string PreflightTrustBoundary = "localManualExactSha";
    private const int KnownResidueMismatchCode = 20014;

    private static int RunStandaloneSecureStorePreflight(string[] args)
    {
        SecureStore? store = null;
        var evidenceWriteInProgress = false;
        var result = new StandalonePreflightResult
        {
            SchemaVersion = 1,
            ReleaseKind = PreflightReleaseKind,
            TrustBoundary = PreflightTrustBoundary,
            Success = false,
            ResultCode = "secureStorePreflightPending",
            Stage = "inputValidation",
            Acl = "notAttempted",
            Recovery = "notAttempted",
            Probe = "notAttempted",
            Cleanup = "notAttempted"
        };

        try
        {
#if PREFLIGHT_VALIDATION
            if (args.Length == 1 &&
                args[0] == ValidationDiagnosticTreeLeaf)
            {
                Thread.Sleep(Timeout.Infinite);
                throw new InvalidOperationException(
                    "validationTreeLeafReturned");
            }
            if (args.Length == 8 &&
                args[6] == ValidationDiagnosticAction &&
                args[7] is (
                    ValidationDiagnosticSuccess or
                    ValidationDiagnosticEarlyFailure or
                    ValidationDiagnosticNonceMismatch or
                    ValidationDiagnosticServerPidMismatch or
                    ValidationDiagnosticServerSessionMismatch or
                    ValidationDiagnosticServerSidMismatch or
                    ValidationDiagnosticFrameOversize or
                    ValidationDiagnosticDisconnect or
                    ValidationDiagnosticTimeout or
                    ValidationDiagnosticTimeoutTree))
            {
                _preflightDiagnosticValidationAction = args[7];
                _stage = "diagnosticChannelValidation";
                if (!TryOpenDiagnosticPipe(args[..6]))
                    throw new InvalidOperationException("invalidArguments");
                _stage = "securityInitialization";
                EnableChildProcessMitigation();
                return RunDiagnosticChannelValidation();
            }
            _stage = "securityInitialization";
            if (args.Length != 1 ||
                args[0] is not (
                    ValidationArgvToken or
                    ValidationChildPolicy or
                    ValidationHangPipes or
                    ValidationPolicySetFault or
                    ValidationPolicyReadbackMismatch))
                throw new InvalidOperationException("invalidArguments");
            _preflightValidationAction = args[0];
#else
            _stage = "diagnosticChannelValidation";
            if (args.Length == 0)
                throw new InvalidOperationException(
                    "diagnosticChannelRequired");
            if (!TryOpenDiagnosticPipe(args))
                throw new InvalidOperationException("invalidArguments");
            _stage = "securityInitialization";
#endif
            EnableChildProcessMitigation();
            _stage = "inputValidation";
#if PREFLIGHT_VALIDATION
            return RunPreflightValidation();
#else
            Environment.SetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS", null);
            Environment.SetEnvironmentVariable(
                "LIGASE_TRANSACTION_TEST_BEHAVIOR", null);
            Environment.SetEnvironmentVariable(
                "LIGASE_TRANSACTION_FAILURE_STAGE", null);
            Environment.SetEnvironmentVariable(
                "LIGASE_TRANSACTION_TEST_ROOT", null);
            SetStage("inputValidation");
            var programData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
            var transactionRoot = Path.Combine(
                programData, "Ligase Host Admin", "Transactions");
            SetStage("rejectReparse");
            store = SecureStore.Open(transactionRoot, create: true);
            result.Acl = "exact";
            result.Recovery = store.RecoveryAction;
            evidenceWriteInProgress = true;
            store.DeleteEvidence();
            evidenceWriteInProgress = false;
            result.Stage = "evidencePending";
            evidenceWriteInProgress = true;
            store.WriteEvidence(SerializeStandalonePreflightEvidence(result));
            evidenceWriteInProgress = false;
            result.Stage = "probe";
            store.Preflight();
            result.Probe = "completed";
            result.Cleanup = "completed";
            result.Stage = "finalReadback";
            result.Success = true;
            result.ResultCode = "secureStorePreflightReady";
            evidenceWriteInProgress = true;
            var committingStore = store;
            store = null;
            committingStore.WriteEvidence(
                SerializeStandalonePreflightEvidence(result));
            TrySendDiagnostic(
                SerializeStandalonePreflightEvidence(result));
            return 0;
#endif
        }
        catch (Exception exception)
        {
            result.Success = false;
            result.ResultCode = exception.Message switch
            {
                "invalidArguments" => "invalidArguments",
                "diagnosticChannelRequired" =>
                    "diagnosticChannelRequired",
                _ => evidenceWriteInProgress
                    ? "secureStorePreflightEvidenceFailed"
                    : "secureStorePreflightFailed"
            };
            result.Stage = _stage;
            result.NativeCategory = _nativeCategory;
            result.NativeCode = _nativeCode;
            result.AclMutationOccurred = _aclMutationOccurred;
            result.AclRollback = _aclRollback;
            result.Recovery = _recoveryAction;
            if (result.Probe == "notAttempted" &&
                _stage is "createTemp" or "atomicReplace" or "read" or "delete")
                result.Probe = "failed";
            if (result.Probe == "failed")
                result.Cleanup = "unknown";
            var failureBytes = SerializeStandalonePreflightEvidence(result);
#if PREFLIGHT_VALIDATION
            if (_preflightValidationChildCleanup == "failed")
                CompleteValidationChildCleanup();
            if (_preflightValidationChildPid != 0 ||
                _preflightValidationChildCleanup != "notRequired")
            {
                failureBytes = Encoding.UTF8.GetBytes(
                    "{\"schemaId\":\"" +
                    ValidationChildCleanupSchema +
                    "\",\"result\":\"failed\",\"stage\":\"childCleanup\"," +
                    "\"childPid\":" + _preflightValidationChildPid + "," +
                    "\"childCleanup\":\"" +
                    _preflightValidationChildCleanup + "\"}");
            }
            else if (_preflightValidationAction == ValidationChildPolicy)
            {
                failureBytes = SerializeValidationChildFailure();
            }
#endif
            if (store is not null && !evidenceWriteInProgress)
            {
                evidenceWriteInProgress = true;
                try
                {
                    store.WriteEvidence(failureBytes);
                }
                catch
                {
                    // The original secure operation remains failed. Evidence
                    // never opens a second storage authority.
                }
            }
            TrySendDiagnostic(failureBytes);
#if PREFLIGHT_VALIDATION
            if (string.IsNullOrEmpty(
                    _preflightDiagnosticValidationAction))
#endif
            try
            {
                Console.Error.Write(Encoding.UTF8.GetString(failureBytes));
            }
            catch
            {
                // Console is advisory. Native exit and the secure evidence
                // commit point remain authoritative.
            }
            return 18;
        }
        finally
        {
            store?.Dispose();
#if PREFLIGHT_ONLY
            _diagnosticPipe?.Dispose();
            _diagnosticPipe = null;
#endif
        }
    }

#if PREFLIGHT_ONLY
    private static bool TryOpenDiagnosticPipe(string[] args)
    {
        if (args.Length != 6 ||
            args[0] != "--diagnostic-pipe" ||
            args[2] != "--nonce" ||
            args[4] != "--parent-pid" ||
            args[1].Length is < 32 or > 96 ||
            !args[1].StartsWith("LigaseSecureStore-", StringComparison.Ordinal) ||
            args[3].Length != 64 ||
            args[3].Any(character => !Uri.IsHexDigit(character)) ||
            !int.TryParse(args[5], out var expectedParentPid) ||
            expectedParentPid <= 0)
            return false;

        var pipe = new NamedPipeClientStream(
            ".", args[1], PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            TokenImpersonationLevel.Identification);
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(15));
        pipe.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
        if (!GetNamedPipeServerProcessId(
                pipe.SafePipeHandle, out var actualParentPid) ||
            actualParentPid != (uint)expectedParentPid
#if PREFLIGHT_VALIDATION
            || _preflightDiagnosticValidationAction ==
                ValidationDiagnosticServerPidMismatch
#endif
            )
            throw new InvalidOperationException("diagnosticPeerInvalid");
        using var parent = Process.GetProcessById(expectedParentPid);
        if (parent.SessionId != Process.GetCurrentProcess().SessionId ||
            !string.Equals(
                GetProcessUserSid(parent.Handle),
                WindowsIdentity.GetCurrent().User?.Value,
                StringComparison.Ordinal)
#if PREFLIGHT_VALIDATION
            || _preflightDiagnosticValidationAction is
                ValidationDiagnosticServerSessionMismatch or
                ValidationDiagnosticServerSidMismatch
#endif
            )
            throw new InvalidOperationException("diagnosticPeerInvalid");

        var helloNonce = args[3].ToUpperInvariant();
#if PREFLIGHT_VALIDATION
        if (_preflightDiagnosticValidationAction ==
            ValidationDiagnosticNonceMismatch)
            helloNonce = new string('0', 64);
        if (_preflightDiagnosticValidationAction ==
            ValidationDiagnosticDisconnect)
            throw new EndOfStreamException();
        if (_preflightDiagnosticValidationAction ==
            ValidationDiagnosticTimeout)
            Thread.Sleep(TimeSpan.FromSeconds(60));
        if (_preflightDiagnosticValidationAction ==
            ValidationDiagnosticTimeoutTree)
        {
            using var descendant = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ??
                    throw new InvalidOperationException(
                        "validationProcessPathUnavailable"),
                Arguments = ValidationDiagnosticTreeLeaf,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (descendant is null)
                throw new InvalidOperationException(
                    "validationDescendantUnavailable");
            Thread.Sleep(TimeSpan.FromSeconds(60));
        }
#endif
        var hello = Encoding.UTF8.GetBytes(
            "{\"schemaId\":\"secureStoreDiagnosticHelloV1\"," +
            "\"nonce\":\"" + helloNonce + "\"," +
            "\"pid\":" + Environment.ProcessId + "," +
            "\"parentPid\":" + expectedParentPid + "}");
        WriteDiagnosticFrame(pipe, hello);
        var ack = ReadDiagnosticFrame(pipe, 512, timeout.Token);
        var expectedAck = Encoding.UTF8.GetBytes(
            "{\"schemaId\":\"secureStoreDiagnosticAckV1\"," +
            "\"nonce\":\"" + args[3].ToUpperInvariant() + "\"}");
        if (!ack.SequenceEqual(expectedAck))
            throw new InvalidOperationException("diagnosticPeerInvalid");
        _diagnosticPipe = pipe;
        return true;
    }
#endif

#if PREFLIGHT_ONLY
    private static void TrySendDiagnostic(byte[] bytes)
    {
        if (_diagnosticPipe is null) return;
        try
        {
            WriteDiagnosticFrame(_diagnosticPipe, bytes);
        }
        catch
        {
            // Diagnostic IPC is observation-only. It cannot change the
            // secure-store transaction or terminal native result.
        }
    }

    private static void WriteDiagnosticFrame(Stream stream, byte[] bytes)
    {
        if (bytes.Length is <= 0 or > 4096)
            throw new InvalidOperationException("diagnosticFrameInvalid");
        Span<byte> prefix = stackalloc byte[4];
        BitConverter.TryWriteBytes(prefix, bytes.Length);
        stream.Write(prefix);
        stream.Write(bytes);
        stream.Flush();
    }

    private static byte[] ReadDiagnosticFrame(
        Stream stream, int maximum, CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        ReadDiagnosticExactly(stream, prefix, cancellationToken);
        var length = BitConverter.ToInt32(prefix);
        if (length is <= 0 || length > maximum)
            throw new InvalidOperationException("diagnosticFrameInvalid");
        var bytes = new byte[length];
        ReadDiagnosticExactly(stream, bytes, cancellationToken);
        return bytes;
    }

    private static void ReadDiagnosticExactly(
        Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.ReadAsync(
                bytes.AsMemory(offset), cancellationToken)
                .AsTask().GetAwaiter().GetResult();
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
    }

    private static string GetProcessUserSid(IntPtr processHandle)
    {
        if (!OpenProcessToken(processHandle, 0x0008, out var token))
            throw new InvalidOperationException("diagnosticPeerInvalid");
        try
        {
            GetTokenInformation(token, 1, IntPtr.Zero, 0, out var length);
            if (length == 0)
                throw new InvalidOperationException("diagnosticPeerInvalid");
            var buffer = Marshal.AllocHGlobal(checked((int)length));
            try
            {
                if (!GetTokenInformation(
                        token, 1, buffer, length, out _))
                    throw new InvalidOperationException(
                        "diagnosticPeerInvalid");
                var sidPointer = Marshal.ReadIntPtr(buffer);
                return new SecurityIdentifier(sidPointer).Value;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }
#endif

    private static byte[] SerializeStandalonePreflightEvidence(
        StandalonePreflightResult result)
    {
        var json = new StringBuilder(512);
        json.Append("{\"schemaVersion\":").Append(result.SchemaVersion);
        AppendJsonString(json, "releaseKind", result.ReleaseKind);
        AppendJsonString(json, "trustBoundary", result.TrustBoundary);
        json.Append(",\"success\":")
            .Append(result.Success ? "true" : "false");
        AppendJsonString(json, "resultCode", result.ResultCode);
        AppendJsonString(json, "stage", result.Stage);
        AppendJsonString(json, "nativeCategory", result.NativeCategory);
        json.Append(",\"nativeCode\":").Append(result.NativeCode);
        AppendJsonString(json, "acl", result.Acl);
        AppendJsonString(json, "recovery", result.Recovery);
        AppendJsonString(json, "probe", result.Probe);
        AppendJsonString(json, "cleanup", result.Cleanup);
        json.Append(",\"aclMutationOccurred\":")
            .Append(result.AclMutationOccurred ? "true" : "false");
        AppendJsonString(json, "aclRollback", result.AclRollback);
        json.Append('}');
        if (json.Length > 4096)
            return Encoding.UTF8.GetBytes(
                "{\"schemaVersion\":1,\"releaseKind\":\"UnsignedDev\"," +
                "\"trustBoundary\":\"localManualExactSha\"," +
                "\"success\":false," +
                "\"resultCode\":\"secureStorePreflightEncodingFailed\"," +
                "\"stage\":\"evidenceEncoding\"," +
                "\"nativeCategory\":\"managedFailure\"," +
                "\"nativeCode\":20012,\"acl\":\"unknown\"," +
                "\"recovery\":\"unknown\",\"probe\":\"unknown\"," +
                "\"cleanup\":\"unknown\"," +
                "\"aclMutationOccurred\":false," +
                "\"aclRollback\":\"unknown\"}");
        return Encoding.UTF8.GetBytes(json.ToString());
    }

    private static void EnableChildProcessMitigation()
    {
#if PREFLIGHT_VALIDATION
        if (_preflightValidationAction == ValidationPolicySetFault)
        {
            _nativeCategory = "accessDenied";
            _nativeCode = ErrorAccessDenied;
            throw new InvalidOperationException(
                "installTransactionUnavailable");
        }
#endif
        var policy = NoChildProcessCreation;
        if (!SetProcessMitigationPolicy(
                ProcessChildProcessPolicy,
                ref policy,
                (nuint)sizeof(uint)))
            throw NativeFailure(
                "installTransactionUnavailable",
                Marshal.GetLastWin32Error());

        if (!GetProcessMitigationPolicy(
                new IntPtr(-1),
                ProcessChildProcessPolicy,
                out var readback,
                (nuint)sizeof(uint)))
            throw NativeFailure(
                "installTransactionUnavailable",
                Marshal.GetLastWin32Error());
#if PREFLIGHT_VALIDATION
        if (_preflightValidationAction ==
            ValidationPolicyReadbackMismatch)
            readback = 0;
#endif
        if (readback != NoChildProcessCreation)
        {
            _nativeCategory = "managedFailure";
            _nativeCode = 20013;
            throw new InvalidOperationException("installTransactionUnavailable");
        }
    }

#if PREFLIGHT_VALIDATION
    private static int RunDiagnosticChannelValidation()
    {
        if (_preflightDiagnosticValidationAction ==
            ValidationDiagnosticEarlyFailure)
        {
            _nativeCategory = "accessDenied";
            _nativeCode = ErrorAccessDenied;
            throw new InvalidOperationException(
                "installTransactionUnavailable");
        }
        if (_preflightDiagnosticValidationAction ==
            ValidationDiagnosticFrameOversize)
        {
            Span<byte> prefix = stackalloc byte[4];
            BitConverter.TryWriteBytes(prefix, 4097);
            _diagnosticPipe!.Write(prefix);
            _diagnosticPipe.Flush();
            return 18;
        }

        var result = new StandalonePreflightResult
        {
            SchemaVersion = 1,
            ReleaseKind = PreflightReleaseKind,
            TrustBoundary = PreflightTrustBoundary,
            Success = true,
            ResultCode = "secureStoreDiagnosticValidationReady",
            Stage = "diagnosticReadback",
            NativeCategory = "none",
            NativeCode = 0,
            Acl = "notAttempted",
            Recovery = "notAttempted",
            Probe = "notAttempted",
            Cleanup = "notRequired",
            AclMutationOccurred = false,
            AclRollback = "notRequired"
        };
        TrySendDiagnostic(SerializeStandalonePreflightEvidence(result));
        return 0;
    }

    private static int RunPreflightValidation()
    {
        if (_preflightValidationAction == ValidationArgvToken)
        {
            Console.Out.Write(
                "{\"result\":\"passed\",\"stage\":\"inputValidation\"," +
                "\"policyActive\":true,\"argumentCount\":1," +
                "\"token\":\"--validate-argv-token\"}");
            return 0;
        }

        if (_preflightValidationAction == ValidationHangPipes)
        {
            Thread.Sleep(Timeout.Infinite);
            throw new InvalidOperationException(
                "validationHangReturned");
        }

        if (_preflightValidationAction != ValidationChildPolicy)
            throw new InvalidOperationException(
                "installTransactionUnavailable");

        _stage = "childPolicy";
        const string sentinelName =
            "ligase-preflight-child-sentinel.txt";
        if (File.Exists(sentinelName))
            throw new InvalidOperationException("validationSentinelExists");

        var systemDirectory = new StringBuilder(260);
        var systemDirectoryLength = GetSystemDirectoryW(
            systemDirectory, systemDirectory.Capacity);
        if (systemDirectoryLength == 0 ||
            systemDirectoryLength >= systemDirectory.Capacity)
            throw NativeFailure(
                "installTransactionUnavailable",
                Marshal.GetLastWin32Error());
        var command = Path.Combine(
            systemDirectory.ToString(), "cmd.exe");
        var commandLine = new StringBuilder(
            "cmd.exe /d /c echo child>" + sentinelName);
        var startup = new StartupInfo
        {
            Size = Marshal.SizeOf<StartupInfo>()
        };
        var created = CreateProcessW(
            command,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            CreateNoWindow | CreateSuspended,
            IntPtr.Zero,
            Environment.CurrentDirectory,
            ref startup,
            out var process);
        var nativeCode = created ? 0 : Marshal.GetLastWin32Error();
        var processHandleZero = process.Process == IntPtr.Zero;
        var threadHandleZero = process.Thread == IntPtr.Zero;
        if (created)
        {
            _preflightValidationChildPid = process.ProcessId;
            _preflightValidationChildProcess = process.Process;
            _preflightValidationChildThread = process.Thread;
            CompleteValidationChildCleanup();
            _stage = "childCleanup";
            throw new InvalidOperationException(
                _preflightValidationChildCleanup == "completed"
                    ? "childCreationUnexpectedlyAllowed"
                    : "childCleanupFailed");
        }
        var failedThreadClosed = threadHandleZero ||
            CloseHandle(process.Thread);
        var failedProcessClosed = processHandleZero ||
            CloseHandle(process.Process);
        if (nativeCode != ErrorChildProcessBlocked ||
            !processHandleZero ||
            !threadHandleZero ||
            !failedThreadClosed ||
            !failedProcessClosed ||
            File.Exists(sentinelName))
            throw NativeFailure(
                "installTransactionUnavailable",
                nativeCode);

        Console.Out.Write(
            "{\"schemaId\":\"" + ValidationChildPolicySchema +
            "\",\"result\":\"passed\",\"stage\":\"childPolicy\"," +
            "\"policyActive\":true,\"argumentCount\":1," +
            "\"token\":\"--validate-child-policy\"," +
            "\"childCreationBlocked\":true," +
            "\"childProcessCreated\":false," +
            "\"processHandlesZero\":true," +
            "\"nativeCode\":" + ErrorChildProcessBlocked + "," +
            "\"sentinelExists\":false}");
        return 0;
    }

    private static byte[] SerializeValidationChildFailure()
    {
        var json = new StringBuilder(256);
        json.Append("{\"schemaId\":\"")
            .Append(ValidationChildFailureSchema)
            .Append("\",\"result\":\"failed\",")
            .Append("\"resultCode\":\"secureStorePreflightFailed\"");
        AppendJsonString(json, "stage", _stage);
        AppendJsonString(json, "nativeCategory", _nativeCategory);
        json.Append(",\"nativeCode\":").Append(_nativeCode).Append('}');
        return Encoding.UTF8.GetBytes(json.ToString());
    }

    private static void CompleteValidationChildCleanup()
    {
        _stage = "childCleanup";
        var cleanup = Stopwatch.StartNew();
        var terminated = false;
        var waited = false;
        while (cleanup.ElapsedMilliseconds < 4000)
        {
            if (!terminated)
                terminated =
                    _preflightValidationChildProcess != IntPtr.Zero &&
                    TerminateProcess(
                        _preflightValidationChildProcess, 18);
            if (terminated)
            {
                waited = WaitForSingleObject(
                    _preflightValidationChildProcess, 100) ==
                    WaitObject0;
                if (waited)
                    break;
            }
            Thread.Sleep(25);
        }
        if (!terminated || !waited)
        {
            _preflightValidationChildCleanup = "failed";
            return;
        }

        var threadClosed =
            _preflightValidationChildThread == IntPtr.Zero ||
            CloseHandle(_preflightValidationChildThread);
        var processClosed =
            _preflightValidationChildProcess == IntPtr.Zero ||
            CloseHandle(_preflightValidationChildProcess);
        if (threadClosed)
            _preflightValidationChildThread = IntPtr.Zero;
        if (processClosed)
            _preflightValidationChildProcess = IntPtr.Zero;
        _preflightValidationChildCleanup =
            threadClosed && processClosed ? "completed" : "failed";
    }
#endif

    private static void AppendJsonString(
        StringBuilder json,
        string name,
        string value)
    {
        json.Append(",\"").Append(name).Append("\":\"");
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    json.Append("\\\"");
                    break;
                case '\\':
                    json.Append("\\\\");
                    break;
                case '\b':
                    json.Append("\\b");
                    break;
                case '\f':
                    json.Append("\\f");
                    break;
                case '\n':
                    json.Append("\\n");
                    break;
                case '\r':
                    json.Append("\\r");
                    break;
                case '\t':
                    json.Append("\\t");
                    break;
                default:
                    if (character < 0x20)
                        json.Append("\\u").Append(((int)character).ToString("x4"));
                    else
                        json.Append(character);
                    break;
            }
        }
        json.Append('"');
    }

    private sealed class StandalonePreflightResult
    {
        public int SchemaVersion { get; init; }
        public string ReleaseKind { get; init; } = "";
        public string TrustBoundary { get; init; } = "";
        public bool Success { get; set; }
        public string ResultCode { get; set; } = "";
        public string Stage { get; set; } = "";
        public string NativeCategory { get; set; } = "none";
        public int NativeCode { get; set; }
        public string Acl { get; set; } = "";
        public string Recovery { get; set; } = "";
        public string Probe { get; set; } = "";
        public string Cleanup { get; set; } = "";
        public bool AclMutationOccurred { get; set; }
        public string AclRollback { get; set; } = "notRequired";
    }
#endif

#if !PREFLIGHT_ONLY
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
            case "emitDuplicateCode":
            case "emitDuplicateCodeLastConflicting":
            case "emitDuplicateNativeCode":
            case "emitDuplicateBindingReason":
            case "emitDuplicateBindingRootKind":
            case "emitDuplicateBindingSegmentCount":
            case "emitDuplicateBindingPrefixMatched":
            case "emitDuplicateBindingVolumeMatched":
            case "emitDuplicateBindingFileIdentityMatched":
            case "emitDuplicateAclInspectionReason":
            case "emitDuplicateEmptyRootInspectionReason":
                throw new InvalidOperationException(
                    "installTransactionUnavailable");
            case "emitAclTupleWrongStage":
            case "emitAclTupleWrongCode":
            case "emitAclTupleWrongReason":
            case "emitAclTupleCrossSplice":
                SetStage("canonicalRoot");
                throw AclInspectionFailure(
                    "canonicalRootInspectionFailed", 20001);
            case "emitEmptyRootTupleWrongStage":
            case "emitEmptyRootTupleWrongCode":
            case "emitEmptyRootTupleWrongReason":
            case "emitEmptyRootTupleCrossSplice":
                SetStage("inspectEmptyRootOwner");
                _emptyRootInspectionReason = "ownerNotAdministrators";
                throw ManagedEmptyRootFailure(20008);
        }
    }
#endif

    private static void SetStage(string stage)
    {
        _stage = stage;
        if (Environment.GetEnvironmentVariable(
                "LIGASE_TRANSACTION_FAILURE_STAGE") == stage &&
            Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") == "1")
            throw new InvalidOperationException("installTransactionUnavailable");
    }

    private static int InspectSystemBinding()
    {
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") != "1")
            throw new InvalidOperationException("invalidArguments");
        var common = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(common))
            throw new InvalidOperationException("installTransactionUnavailable");
        var adminRoot = Path.Combine(common, "Ligase Host Admin");
        using var handle = OpenPath(
            adminRoot, directory: true, writeSecurity: false);
        _ = VerifyHandle(handle, adminRoot, directory: true);
        Console.Write(
            "{\"code\":\"installTransactionBindingValid\"," +
            "\"rootKind\":\"" + _bindingRootKind + "\"," +
            "\"segmentCount\":" + _bindingSegmentCount + "," +
            "\"prefixMatched\":" +
            _bindingPrefixMatched.ToString().ToLowerInvariant() + "," +
            "\"volumeMatched\":" +
            _bindingVolumeMatched.ToString().ToLowerInvariant() + "," +
            "\"fileIdentityMatched\":" +
            _bindingFileIdentityMatched.ToString().ToLowerInvariant() + "}");
        return 0;
    }

    private static int InspectSystemAcl()
    {
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") != "1")
            throw new InvalidOperationException("invalidArguments");
        var common = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(common))
            throw new InvalidOperationException("installTransactionUnavailable");
        var adminRoot = Path.Combine(common, "Ligase Host Admin");
        using var handle = OpenPath(
            adminRoot, directory: true, writeSecurity: false);
        _ = VerifyHandle(handle, adminRoot, directory: true);
        SetStage("canonicalRoot");
        if (!IsCanonicalAdminRoot(adminRoot, common))
            throw AclInspectionFailure(
                "canonicalRootInspectionFailed", 20001);
        SetStage("inspectAcl");
        var exact = HasExactAcl(handle, directory: true);
        Console.Write(
            "{\"code\":\"installTransactionAclInspectionValid\"," +
            $"\"exact\":{exact.ToString().ToLowerInvariant()}," +
            $"\"reason\":\"{_aclInspectionReason}\"}}");
        return 0;
    }

    private static int InspectSystemEmptyRoot()
    {
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") != "1")
            throw new InvalidOperationException("invalidArguments");
        var common = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);
        var adminRoot = Path.Combine(common, "Ligase Host Admin");
        using var handle = OpenPath(
            adminRoot, directory: true, writeSecurity: false);
        VerifyHandle(handle, adminRoot, directory: true);
        VerifyEmptyAdminRoot(handle);
        Console.Out.Write("{\"code\":\"emptyAdminRootValid\"," +
            $"\"reason\":\"{_emptyRootInspectionReason}\"}}");
        return 0;
    }

    private static int InspectEmptyRoot(string[] args)
    {
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") != "1")
            throw new InvalidOperationException("invalidArguments");
        var root = ResolveRoot(args);
        using var handle = OpenPath(
            root, directory: true, writeSecurity: false, shareMode: 0);
        VerifyHandle(handle, root, directory: true);
        VerifyEmptyAdminRoot(handle);
        Console.Out.Write("{\"code\":\"emptyAdminRootValid\"," +
            $"\"reason\":\"{_emptyRootInspectionReason}\"}}");
        return 0;
    }

    private static int InspectSequentialBinding(string[] args)
    {
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") != "1" ||
            args.Length != 3 || args[1] != "--test-root")
            throw new InvalidOperationException("invalidArguments");
        var second = Path.GetFullPath(args[2]);
        var first = Path.Combine(
            Path.GetDirectoryName(second)!, "binding-first");
        using (var firstHandle = OpenPath(
                   first, directory: true, writeSecurity: false))
            _ = VerifyHandle(firstHandle, first, directory: true);
        using var secondHandle = OpenPath(
            second, directory: true, writeSecurity: false);
        _ = VerifyHandle(secondHandle, second, directory: true);
        Console.Write(
            "{\"code\":\"installTransactionSequentialBindingValid\"," +
            "\"success\":true}");
        return 0;
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

    private static InvalidOperationException BindingFailure(string reason)
    {
        _nativeCategory = "bindingMismatch";
        _nativeCode = 0;
        _bindingReason = reason;
        return new InvalidOperationException("installTransactionInvalid");
    }

    private static InvalidOperationException AclInspectionFailure(
        string reason, int code)
    {
        _nativeCategory = "managedFailure";
        _nativeCode = code;
        _aclInspectionReason = reason;
        return new InvalidOperationException("installTransactionAclInvalid");
    }

    private static void InjectValidationAclFailure(
        string behavior, string reason, int code)
    {
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") == "1" &&
            Environment.GetEnvironmentVariable(
                "LIGASE_TRANSACTION_TEST_BEHAVIOR") == behavior)
            throw AclInspectionFailure(reason, code);
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

    private sealed class AdminRootRecoveryLease : IDisposable
    {
        private enum RecoveryLeaseState
        {
            Unarmed,
            Frozen,
            Mutated,
            Committed
        }

        private readonly SafeFileHandle _handle;
        private readonly string _path;
        private readonly FileIdentity _identity;
        private byte[]? _originalSecurity;
        private string? _createdTransactionPath;
        private FileIdentity? _createdTransactionIdentity;
        private RecoveryLeaseState _state = RecoveryLeaseState.Unarmed;
        private bool _mutationObserved;
        private bool _rollbackAttempted;

        public AdminRootRecoveryLease(
            SafeFileHandle handle, string path, FileIdentity identity)
        {
            _handle = handle;
            _path = path;
            _identity = identity;
        }

        public void FreezeOriginal(byte[] bytes)
        {
            _originalSecurity = bytes.ToArray();
            _state = RecoveryLeaseState.Frozen;
        }

        public void MarkMutation()
        {
            _mutationObserved = true;
            _aclMutationOccurred = true;
            _recoveryAction = "recoverEmptyAdminRoot";
            _state = RecoveryLeaseState.Mutated;
        }

        public void RecordCreatedTransaction(
            string path, FileIdentity identity)
        {
            _createdTransactionPath = path;
            _createdTransactionIdentity = identity;
        }

        public void Commit()
        {
            _state = RecoveryLeaseState.Committed;
            _recoveryAction = "recoverEmptyAdminRoot";
        }

        public void Rollback()
        {
            if (_state is RecoveryLeaseState.Unarmed or
                RecoveryLeaseState.Committed || _rollbackAttempted)
                return;
            _rollbackAttempted = true;
            var completed = false;
            try
            {
                if (_originalSecurity is null ||
                    VerifyHandle(_handle, _path, directory: true) != _identity)
                    throw IdentityChanged();

                if (_createdTransactionPath is not null &&
                    _createdTransactionIdentity is not null)
                {
                    TryCleanupCreatedDirectory(
                        _createdTransactionPath,
                        _createdTransactionIdentity.Value);
                    if (Directory.Exists(_createdTransactionPath))
                        throw new InvalidOperationException(
                            "installTransactionRollbackFailed");
                }

                VerifyEmptyAdminRoot(_handle);
                var live = ReadSecurityDescriptor(_handle);
                var changed = !live.SequenceEqual(_originalSecurity);
                if (changed)
                    _state = RecoveryLeaseState.Mutated;
                _mutationObserved |= changed;
                _aclMutationOccurred |= changed;
                if (changed &&
                    !TryRestoreSecurityDescriptor(
                        _handle, _originalSecurity))
                    throw new InvalidOperationException(
                        "installTransactionRollbackFailed");
                if (!ReadSecurityDescriptor(_handle)
                        .SequenceEqual(_originalSecurity) ||
                    VerifyHandle(_handle, _path, directory: true) !=
                        _identity)
                    throw new InvalidOperationException(
                        "installTransactionRollbackFailed");
                VerifyEmptyAdminRoot(_handle);
                completed = true;
            }
            finally
            {
                _aclRollback = completed
                    ? _mutationObserved ? "completed" : "notRequired"
                    : "failed";
                if (!completed)
                    _recoveryAction = "rollbackFailed";
            }
        }

        public void Dispose()
        {
            if ((_state is RecoveryLeaseState.Frozen or
                    RecoveryLeaseState.Mutated) &&
                !_rollbackAttempted)
                Rollback();
            _handle.Dispose();
        }
    }

    private sealed class SecureStore : IDisposable
    {
        private readonly string _root;
        private readonly SafeFileHandle _rootHandle;
        private readonly FileIdentity _rootIdentity;
        private readonly bool _recoveredEmptyAdminRoot;
        private string TransactionPath => Path.Combine(
            _root, "pending-install-transaction.json");
        private string PreflightEvidencePath => Path.Combine(
            _root, "secure-store-preflight-outcome.json");

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
                AdminRootRecoveryLease? recoveryLease = null;
                try
                {
                    var recoveredEmptyAdminRoot = HardenDirectoryChain(
                        root,
                        test ? Path.GetDirectoryName(root)! : programData,
                        ref recoveryLease);
                    InjectPostAclRecoveryFailure("failOpenVerified");
                    var store = OpenVerified(root, recoveredEmptyAdminRoot);
                    recoveryLease?.Commit();
                    return store;
                }
                catch
                {
                    recoveryLease?.Rollback();
                    throw;
                }
                finally
                {
                    recoveryLease?.Dispose();
                }
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
            WriteExactFile(
                bytes, TransactionPath, ".pending-", terminalCommit: false);
        }

#if PREFLIGHT_ONLY
        public void WriteEvidence(byte[] bytes)
        {
            WriteExactFile(
                bytes, PreflightEvidencePath, ".preflight-evidence-",
                terminalCommit: true);
        }

        public void DeleteEvidence()
        {
            DeleteExactFile(PreflightEvidencePath);
        }
#endif

        private void WriteExactFile(
            byte[] bytes,
            string destination,
            string temporaryPrefix,
            bool terminalCommit)
        {
            ValidateRoot();
            var temp = Path.Combine(
                _root, temporaryPrefix + Guid.NewGuid().ToString("N"));
            SafeFileHandle? tempHandle = null;
            var committed = false;
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
                    if (HasAlternateDataStream(verify))
                        throw new InvalidOperationException(
                            "installTransactionInvalid");
                    using var stream = new FileStream(
                        verify, FileAccess.Read, 8192, false);
                    if (!ReadBounded(stream).SequenceEqual(bytes))
                        throw new InvalidOperationException(
                            "installTransactionInvalid");
                }
                ValidateRoot();
                SetStage("atomicReplace");
                if (!MoveFileExW(temp, destination,
                        MoveReplaceExisting | MoveWriteThrough))
                    throw new InvalidOperationException("installTransactionInvalid");
                committed = true;
                if (terminalCommit)
                    return;
                SetStage("finalReadback");
                using var final = OpenPath(destination, false, false);
                AssertAcl(final, false);
                var finalIdentity = VerifyHandle(final, destination, false);
                using (var stream = new FileStream(
                           final, FileAccess.Read, 8192, false))
                {
                    if (!ReadBounded(stream).SequenceEqual(bytes))
                        throw new InvalidOperationException(
                            "installTransactionInvalid");
                }
                using (var reopened = OpenPath(destination, false, false))
                {
                    AssertAcl(reopened, false);
                    if (VerifyHandle(reopened, destination, false) !=
                        finalIdentity)
                        throw new InvalidOperationException(
                            "installTransactionInvalid");
                }
                ValidateRoot();
            }
            finally
            {
                tempHandle?.Dispose();
                if (!committed && File.Exists(temp))
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
            DeleteExactFile(TransactionPath);
        }

        private void DeleteExactFile(string path)
        {
            SetStage("delete");
            ValidateRoot();
            if (!File.Exists(path)) return;
            using var handle = OpenPath(path, false, false);
            AssertAcl(handle, false);
            VerifyHandle(handle, path, false);
            handle.Dispose();
            File.Delete(path);
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

    private static bool HardenDirectoryChain(
        string root,
        string trustedBase,
        ref AdminRootRecoveryLease? recoveryLease)
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
            if (segmentIndex == 2 && recoveryLease is not null)
                InjectPostAclRecoveryFailure("failTransactionsCreate");
            if (!existed)
                CreateDirectoryWithExactAcl(current);
            using var handle = OpenPath(current, true, writeSecurity: false);
            var identity = VerifyHandle(handle, current, true);
            if (!existed && segmentIndex == 2 && recoveryLease is not null)
                recoveryLease.RecordCreatedTransaction(current, identity);
            if (segmentIndex == 2 && recoveryLease is not null)
                InjectPostAclRecoveryFailure("failTransactionsVerify");
            SetStage("canonicalRoot");
            InjectValidationAclFailure(
                "failCanonicalRootInspection",
                "canonicalRootInspectionFailed", 20001);
            var canonicalAdminRoot =
                IsCanonicalAdminRoot(current, trustedBase);
            SetStage("inspectAcl");
            var exactAcl = HasExactAcl(handle, directory: true);
            if (existed && segmentIndex == 1 &&
                canonicalAdminRoot && !exactAcl)
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
                var exclusive = OpenRecoveryDirectory(current);
                recoveryLease = new AdminRootRecoveryLease(
                    exclusive, current, identity);
                {
                    InjectPreArmRecoveryFailure("failPreArmIdentity");
                    if (VerifyHandle(exclusive, current, true) != identity)
                        throw IdentityChanged();
                    VerifyEmptyAdminRoot(exclusive);
                    AttemptValidationResidueInjection(current);
                    VerifyEmptyAdminRoot(exclusive);
                    InjectPreArmRecoveryFailure("failPreArmReadSecurity");
                    originalSecurity = ReadSecurityDescriptor(exclusive);
#if PREFLIGHT_ONLY
                    InjectPreArmRecoveryFailure("failPreArmKnownResidue");
                    AssertKnownPartialAdminRoot(originalSecurity);
#endif
                    recoveryLease.FreezeOriginal(originalSecurity);
                    try
                    {
                        SetStage("applyAcl");
                        ApplyExactAcl(exclusive, true);
                        _aclMutationOccurred =
                            !ReadSecurityDescriptor(exclusive)
                                .SequenceEqual(originalSecurity);
                        recoveryLease.MarkMutation();
                        SetStage("finalReadback");
                        if (VerifyHandle(exclusive, current, true) != identity)
                            throw IdentityChanged();
                        VerifyEmptyAdminRoot(exclusive);
                        AssertAcl(exclusive, directory: true);
                    }
                    catch
                    {
                        recoveryLease.Rollback();
                        throw;
                    }
                }
                recoveredEmptyAdminRoot = true;
                continue;
            }
            SetStage("assertAcl");
            AssertAcl(handle, true);
        }
        return recoveredEmptyAdminRoot;
    }

    private static void InjectPostAclRecoveryFailure(string behavior)
    {
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") == "1" &&
            Environment.GetEnvironmentVariable(
                "LIGASE_TRANSACTION_TEST_BEHAVIOR") == behavior)
            throw new InvalidOperationException(
                "installTransactionUnavailable");
    }

    private static void InjectPreArmRecoveryFailure(string behavior)
    {
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") != "1" ||
            Environment.GetEnvironmentVariable(
                "LIGASE_TRANSACTION_TEST_BEHAVIOR") != behavior)
            return;
        if (behavior == "failPreArmIdentity")
        {
            SetStage("verifyIdentity");
            throw BindingFailure("fileIdentityMismatch");
        }
        if (behavior == "failPreArmReadSecurity")
        {
            SetStage("readSecurityDescriptor");
            throw NativeFailure(
                "installTransactionAclInvalid", ErrorAccessDenied);
        }
        SetStage("knownResidue");
        throw new InvalidOperationException(
            "installTransactionKnownResidueMismatch");
    }

#if PREFLIGHT_ONLY
    private static void AssertKnownPartialAdminRoot(byte[] binary)
    {
        SetStage("knownResidue");
        try
        {
            var descriptor = new RawSecurityDescriptor(binary, 0);
            var requiredFlags =
                ControlFlags.DiscretionaryAclPresent |
                ControlFlags.DiscretionaryAclAutoInherited |
                ControlFlags.DiscretionaryAclProtected;
            var relevantFlags = descriptor.ControlFlags &
                (ControlFlags.DiscretionaryAclPresent |
                 ControlFlags.DiscretionaryAclDefaulted |
                 ControlFlags.DiscretionaryAclAutoInherited |
                 ControlFlags.DiscretionaryAclAutoInheritRequired |
                 ControlFlags.DiscretionaryAclProtected);
            var expected = new[]
            {
                new KnownResidueAce(SystemSid, 0x001F01FF,
                    AceFlags.ObjectInherit | AceFlags.ContainerInherit),
                new KnownResidueAce(AdminSid, 0x001F01FF,
                    AceFlags.ObjectInherit | AceFlags.ContainerInherit),
                new KnownResidueAce(
                    new SecurityIdentifier(
                        WellKnownSidType.CreatorOwnerSid, null),
                    unchecked((int)0x10000000),
                    AceFlags.ObjectInherit | AceFlags.ContainerInherit |
                    AceFlags.InheritOnly),
                new KnownResidueAce(
                    new SecurityIdentifier(
                        WellKnownSidType.BuiltinUsersSid, null),
                    0x001200A9,
                    AceFlags.ObjectInherit | AceFlags.ContainerInherit),
                new KnownResidueAce(
                    new SecurityIdentifier(
                        WellKnownSidType.BuiltinUsersSid, null),
                    0x00000116, AceFlags.ContainerInherit)
            };
            var matched = new bool[expected.Length];
            var dacl = descriptor.DiscretionaryAcl;
            if (descriptor.Owner is null ||
                !AdminSid.Equals(descriptor.Owner) ||
                relevantFlags != requiredFlags ||
                dacl is null || dacl.Count != expected.Length)
                throw KnownResidueMismatch();
            foreach (GenericAce generic in dacl)
            {
                if (generic is not QualifiedAce ace ||
                    generic.AceType != AceType.AccessAllowed)
                    throw KnownResidueMismatch();
                var index = -1;
                for (var candidate = 0;
                     candidate < expected.Length; candidate++)
                {
                    if (!matched[candidate] &&
                        expected[candidate].Matches(ace))
                    {
                        index = candidate;
                        break;
                    }
                }
                if (index < 0) throw KnownResidueMismatch();
                matched[index] = true;
            }
            if (matched.Any(value => !value))
                throw KnownResidueMismatch();
        }
        catch (InvalidOperationException exception)
            when (exception.Message ==
                  "installTransactionKnownResidueMismatch")
        {
            throw;
        }
        catch
        {
            throw KnownResidueMismatch();
        }
    }

    private static InvalidOperationException KnownResidueMismatch()
    {
        _nativeCategory = "managedFailure";
        _nativeCode = KnownResidueMismatchCode;
        return new InvalidOperationException(
            "installTransactionKnownResidueMismatch");
    }

    private readonly record struct KnownResidueAce(
        SecurityIdentifier Sid, int Mask, AceFlags Flags)
    {
        public bool Matches(QualifiedAce ace) =>
            ace.SecurityIdentifier is not null &&
            Sid.Equals(ace.SecurityIdentifier) &&
            ace.AccessMask == Mask &&
            ace.AceFlags == Flags;
    }
#endif

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
        SetStage("inspectEmptyRootOwner");
        _emptyRootInspectionReason = "none";
        if (!HasExpectedOwner(handle))
        {
            _emptyRootInspectionReason = "ownerNotAdministrators";
            throw ManagedEmptyRootFailure(20008);
        }

        SetStage("inspectEmptyRootChildren");
        _emptyRootInspectionReason = "none";
        if (HasDirectoryEntries(handle))
        {
            _emptyRootInspectionReason = "childEntryPresent";
            throw ManagedEmptyRootFailure(20009);
        }

        SetStage("inspectEmptyRootStreams");
        _emptyRootInspectionReason = "none";
        if (HasAlternateDataStream(handle))
        {
            _emptyRootInspectionReason = "namedDataStreamPresent";
            throw ManagedEmptyRootFailure(20010);
        }

        _emptyRootInspectionReason = "empty";
    }

    private static Exception ManagedEmptyRootFailure(int code)
    {
        _nativeCategory = "managedFailure";
        _nativeCode = code;
        return new InvalidOperationException("installTransactionInvalid");
    }

    private static Exception StreamInspectionFailure()
    {
        SetStage("inspectEmptyRootStreamMetadata");
        _emptyRootInspectionReason = "streamMetadataInvalid";
        return ManagedEmptyRootFailure(20011);
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
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") == "1")
        {
            var behavior = Environment.GetEnvironmentVariable(
                "LIGASE_TRANSACTION_TEST_BEHAVIOR");
            if (behavior == "failEmptyRootOwnerMismatch")
                return false;
            if (behavior is "failEmptyRootChildPresent" or
                "failEmptyRootNamedAds" or
                "failEmptyRootStreamMalformed" or
                "failEmptyRootStreamOffsetOverflow" or
                "failEmptyRootStreamNearMax" or
                "failEmptyRootStreamRemainingShort" or
                "failEmptyRootStreamZeroProgress" or
                "inspectFixtureStreams")
                return true;
        }
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
        var validationBehavior = Environment.GetEnvironmentVariable(
            "LIGASE_INSTALL_VALIDATION_HARNESS") == "1"
            ? Environment.GetEnvironmentVariable(
                "LIGASE_TRANSACTION_TEST_BEHAVIOR")
            : null;
        var inspectOnlyValidation = validationBehavior is
            "failEmptyRootOwnerMismatch" or
            "failEmptyRootChildPresent" or
            "failEmptyRootNamedAds" or
            "failEmptyRootStreamMalformed" or
            "failEmptyRootStreamOffsetOverflow" or
            "failEmptyRootStreamNearMax" or
            "failEmptyRootStreamRemainingShort" or
            "failEmptyRootStreamZeroProgress";
        var access = FileListDirectory | FileReadAttributes | ReadControl |
            Synchronize |
            (inspectOnlyValidation ? 0 : WriteDac | WriteOwner);
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
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") == "1" &&
            Environment.GetEnvironmentVariable(
                "LIGASE_TRANSACTION_TEST_BEHAVIOR") ==
            "failEmptyRootChildPresent")
            return true;
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
        var validationBehavior = Environment.GetEnvironmentVariable(
            "LIGASE_INSTALL_VALIDATION_HARNESS") == "1"
            ? Environment.GetEnvironmentVariable(
                "LIGASE_TRANSACTION_TEST_BEHAVIOR")
            : null;
        if (Environment.GetEnvironmentVariable(
                "LIGASE_INSTALL_VALIDATION_HARNESS") == "1")
        {
            if (validationBehavior == "failEmptyRootNamedAds")
                return true;
            if (validationBehavior == "failEmptyRootStreamMalformed")
                throw StreamInspectionFailure();
        }
        var buffer = Marshal.AllocHGlobal(NtQueryBufferBytes);
        try
        {
            if (validationBehavior is
                "failEmptyRootStreamOffsetOverflow" or
                "failEmptyRootStreamNearMax" or
                "failEmptyRootStreamRemainingShort" or
                "failEmptyRootStreamZeroProgress")
            {
                var zero = new byte[NtQueryBufferBytes];
                Marshal.Copy(zero, 0, buffer, zero.Length);
                var firstNext = validationBehavior switch
                {
                    "failEmptyRootStreamOffsetOverflow" => 24,
                    "failEmptyRootStreamNearMax" => int.MaxValue - 7,
                    "failEmptyRootStreamRemainingShort" =>
                        NtQueryBufferBytes - 16,
                    _ => 8
                };
                Marshal.WriteInt32(buffer, 0, firstNext);
                Marshal.WriteInt32(buffer, 4, 0);
                if (validationBehavior ==
                    "failEmptyRootStreamOffsetOverflow")
                {
                    var second = IntPtr.Add(buffer, 24);
                    Marshal.WriteInt32(second, 0, int.MaxValue - 7);
                    const string canonicalData = "::$DATA";
                    var canonicalBytes = Encoding.Unicode.GetBytes(
                        canonicalData);
                    Marshal.WriteInt32(
                        second, 4, canonicalBytes.Length);
                    Marshal.Copy(
                        canonicalBytes, 0, IntPtr.Add(second, 24),
                        canonicalBytes.Length);
                }
            }
            else
            {
                SetStage("queryEmptyRootStreams");
                if (!GetFileInformationByHandleEx(
                        handle, FileStreamInfo, buffer,
                        NtQueryBufferBytes))
                {
                    var queryError = Marshal.GetLastPInvokeError();
                    if (queryError == ErrorHandleEof)
                        return false;
                    if (queryError == 0)
                    {
                        _emptyRootInspectionReason = "streamQueryFailed";
                        _nativeCategory = "managedFailure";
                        _nativeCode = StreamQueryWithoutNativeCode;
                        throw new InvalidOperationException(
                            "installTransactionUnavailable");
                    }
                    throw NativeFailure(
                        "installTransactionUnavailable", queryError);
                }
            }
            SetStage("inspectEmptyRootStreams");
            var offset = 0;
            var canonicalStreams = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (var entry = 0; entry < 64; entry++)
            {
                if (offset < 0 || offset > NtQueryBufferBytes - 24)
                    throw StreamInspectionFailure();
                var current = IntPtr.Add(buffer, offset);
                var next = Marshal.ReadInt32(current, 0);
                var nameLength = Marshal.ReadInt32(current, 4);
                if (nameLength < 0 ||
                    nameLength > NtQueryBufferBytes - offset - 24 ||
                    (nameLength & 1) != 0)
                    throw StreamInspectionFailure();
                var name = Marshal.PtrToStringUni(
                    IntPtr.Add(current, 24), nameLength / 2);
                if (name is null)
                    throw StreamInspectionFailure();
                // Directories may expose their unnamed data stream and their
                // canonical $I30 index-allocation stream. Neither is a
                // caller-created named ADS.
                // Any other stream name is caller-created data and therefore
                // disqualifies automatic recovery of an allegedly empty root.
                if (name.Length != 0 &&
                    !string.Equals(name, "::$DATA",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "::$INDEX_ALLOCATION",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, ":$I30:$INDEX_ALLOCATION",
                        StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!canonicalStreams.Add(name))
                    throw StreamInspectionFailure();
                if (next == 0)
                    return false;
                if (next < 24 || (next & 7) != 0 ||
                    next > NtQueryBufferBytes - offset)
                    throw StreamInspectionFailure();
                offset = checked(offset + next);
            }
            throw StreamInspectionFailure();
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
        _bindingAttempt++;
        _bindingReason = "none";
        _bindingRootKind = "none";
        _bindingSegmentCount = 0;
        _bindingPrefixMatched = false;
        _bindingVolumeMatched = false;
        _bindingFileIdentityMatched = false;
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
        var identity = new FileIdentity(
            info.VolumeSerialNumber,
            ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
        SetStage("resolveFinalPath");
        InjectValidationNativeFailure(
            "failResolveFinalPathInvalidParameter", ErrorInvalidParameter);
        var expectedFull = Path.GetFullPath(expected).TrimEnd('\\');
        var test = Environment.GetEnvironmentVariable(
            "LIGASE_INSTALL_VALIDATION_HARNESS") == "1";
        var trustedBase = test
            ? Path.GetDirectoryName(Path.GetFullPath(
                Environment.GetEnvironmentVariable(
                    "LIGASE_TRANSACTION_TEST_ROOT") ?? expectedFull))!
            : Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
        trustedBase = Path.GetFullPath(trustedBase).TrimEnd('\\');
        var relative = Path.GetRelativePath(trustedBase, expectedFull);
        var segments = relative == "."
            ? Array.Empty<string>()
            : relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment =>
                segment is "." or ".." ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw BindingFailure("segmentMismatch");
        _bindingSegmentCount = segments.Length;

        using var trustedHandle = CreateFileW(
            trustedBase,
            FileListDirectory | FileReadAttributes | ReadControl | Synchronize,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero, OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics,
            IntPtr.Zero);
        if (trustedHandle.IsInvalid)
            throw NativeFailure("installTransactionUnavailable",
                Marshal.GetLastWin32Error());
        if (!GetFileInformationByHandle(trustedHandle, out var trustedInfo))
            throw NativeFailure("installTransactionUnavailable",
                Marshal.GetLastWin32Error());
        if ((trustedInfo.FileAttributes &
                (uint)FileAttributes.ReparsePoint) != 0 ||
            (trustedInfo.FileAttributes &
                (uint)FileAttributes.Directory) == 0)
            throw BindingFailure("trustedRootInvalid");

        var trustedFinal = GetHandleFinalPath(trustedHandle);
        var actualFinal = GetHandleFinalPath(handle);
        _bindingRootKind = ClassifyFinalPath(trustedFinal);
        _bindingVolumeMatched =
            trustedInfo.VolumeSerialNumber == info.VolumeSerialNumber;
        var validationBehavior = Environment.GetEnvironmentVariable(
            "LIGASE_TRANSACTION_TEST_BEHAVIOR");
        if (test && (validationBehavior == "failBindingWrongVolume" ||
            validationBehavior == "failSecondBindingWrongVolume" &&
            _bindingAttempt == 2))
            _bindingVolumeMatched = false;
        if (!_bindingVolumeMatched)
            throw BindingFailure("volumeMismatch");
        var expectedFinal = segments.Aggregate(
            trustedFinal.TrimEnd('\\'), Path.Combine);
        _bindingPrefixMatched = string.Equals(
            actualFinal.TrimEnd('\\'), expectedFinal.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
        if (test && (validationBehavior == "failBindingSegmentMismatch" ||
            validationBehavior == "failSecondBindingSegmentMismatch" &&
            _bindingAttempt == 2))
            _bindingPrefixMatched = false;
        if (!_bindingPrefixMatched)
            throw BindingFailure("segmentMismatch");
        if (!GetFileInformationByHandle(handle, out var finalInfo))
            throw NativeFailure("installTransactionUnavailable",
                Marshal.GetLastWin32Error());
        var finalIdentity = new FileIdentity(
            finalInfo.VolumeSerialNumber,
            ((ulong)finalInfo.FileIndexHigh << 32) | finalInfo.FileIndexLow);
        _bindingFileIdentityMatched = finalIdentity == identity;
        if (test && (validationBehavior == "failBindingIdentitySwap" ||
            validationBehavior == "failSecondBindingIdentitySwap" &&
            _bindingAttempt == 2))
            _bindingFileIdentityMatched = false;
        if (!_bindingFileIdentityMatched)
            throw BindingFailure("fileIdentityMismatch");
        return identity;
    }

    private static string GetHandleFinalPath(SafeFileHandle handle)
    {
        var builder = new StringBuilder(32768);
        if (GetFinalPathNameByHandleW(
                handle, builder, builder.Capacity, VolumeNameNt) == 0)
            throw NativeFailure("installTransactionUnavailable",
                Marshal.GetLastWin32Error());
        return builder.ToString();
    }

    private static string ClassifyFinalPath(string path)
    {
        if (path.StartsWith(@"\\?\Volume{",
                StringComparison.OrdinalIgnoreCase))
            return "volumeGuid";
        if (path.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
            return "device";
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return "unc";
        if (path.Length >= 3 && char.IsLetter(path[0]) &&
            path[1] == ':' && path[2] == '\\')
            return "dosDrive";
        return "unknown";
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
        _aclInspectionReason = "none";
        SetStage("readSecurityDescriptor");
        InjectValidationAclFailure(
            "failReadSecurityDescriptor",
            "securityDescriptorReadFailed", 20002);
        var result = GetSecurityInfo(handle, SeFileObject,
            OwnerSecurityInformation | DaclSecurityInformation,
            out _, out _, out _, out _, out var descriptor);
        if (result != 0 || descriptor == IntPtr.Zero)
        {
            _aclInspectionReason = "securityDescriptorReadFailed";
            throw NativeFailure(
                "installTransactionAclInvalid", checked((int)result));
        }
        try
        {
            SetStage("descriptorLength");
            InjectValidationAclFailure(
                "failDescriptorLength",
                "descriptorLengthInvalid", 20003);
            var length = GetSecurityDescriptorLength(descriptor);
            if (length is 0 or > 1024 * 1024)
                throw AclInspectionFailure(
                    "descriptorLengthInvalid", 20003);

            SetStage("descriptorCopy");
            InjectValidationAclFailure(
                "failDescriptorCopy",
                "descriptorCopyFailed", 20004);
            var binary = new byte[length];
            try
            {
                Marshal.Copy(descriptor, binary, 0, checked((int)length));
            }
            catch
            {
                throw AclInspectionFailure(
                    "descriptorCopyFailed", 20004);
            }

            SetStage("descriptorParse");
            InjectValidationAclFailure(
                "failDescriptorParse",
                "descriptorParseFailed", 20005);
            RawSecurityDescriptor actual;
            try
            {
                actual = new RawSecurityDescriptor(binary, 0);
            }
            catch
            {
                throw AclInspectionFailure(
                    "descriptorParseFailed", 20005);
            }

            SetStage("buildSecurityDescriptor");
            InjectValidationAclFailure(
                "failBuildSecurityDescriptor",
                "expectedDescriptorBuildFailed", 20006);
            CommonSecurityDescriptor expected;
            try
            {
                expected = BuildSecurity(directory);
            }
            catch
            {
                throw AclInspectionFailure(
                    "expectedDescriptorBuildFailed", 20006);
            }

            SetStage("compareSecurityDescriptor");
            InjectValidationAclFailure(
                "failCompareSecurityDescriptor",
                "descriptorCompareFailed", 20007);
            try
            {
                var sections = AccessControlSections.Owner |
                    AccessControlSections.Access;
                var matches = string.Equals(
                    actual.GetSddlForm(sections),
                    expected.GetSddlForm(sections),
                    StringComparison.Ordinal);
                _aclInspectionReason = matches ? "exact" : "notExact";
                return matches;
            }
            catch
            {
                throw AclInspectionFailure(
                    "descriptorCompareFailed", 20007);
            }
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

#if PREFLIGHT_VALIDATION
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Length;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }
#endif

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
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessMitigationPolicy(
        int mitigationPolicy,
        ref uint buffer,
        nuint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMitigationPolicy(
        IntPtr process,
        int mitigationPolicy,
        out uint buffer,
        nuint length);

#if PREFLIGHT_ONLY
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(
        SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle, int tokenInformationClass,
        IntPtr tokenInformation, uint tokenInformationLength,
        out uint returnLength);
#endif

#if PREFLIGHT_VALIDATION
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetSystemDirectoryW(
        StringBuilder buffer,
        int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(
        IntPtr process,
        uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(
        IntPtr handle,
        uint milliseconds);
#endif

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle fileHandle, int fileInformationClass,
        IntPtr fileInformation, int bufferSize);
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
