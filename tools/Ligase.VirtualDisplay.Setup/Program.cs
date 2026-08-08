using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

internal static class Program
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;
    private const uint SpdrpHardwareId = 0x00000001;
    private const uint DevpropTypeString = 0x00000012;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorInvalidData = 13;
    private const int ErrorNotFound = 1168;
    private const int ErrorFileExists = 80;
    private const uint SpCopyNoOverwrite = 0x00000008;
    private const string TargetHardwareId = "ROOT\\SUDOMAKER\\SUDOVDA";
    private static readonly Guid DevpkeyDriverInfPath =
        new("A8B865DD-2E3D-4094-AD97-E593A70C75D6");
    private const uint DevpkeyDeviceDriverInfPathPid = 5;

    private sealed record Device(string InstanceId, bool Present, string Status,
        string DriverInf);
    private sealed record SetupTransport(string Mode,
        string OperationDirectoryAcl, bool OperationDirectoryNonReparse,
        bool RequestCreateNew, bool RequestFlushCompleted,
        bool RequestWriteHandlesClosed, bool RequestReadOnlyHandle,
        bool RequestWriteSharing, bool ResultCreateNew,
        bool ResultExclusiveHandle, string RequestFileIdentitySha256,
        string ResultFileIdentitySha256);
    private sealed record SetupRequest(int SchemaVersion, string Operation,
        string OperationIdSha256, DateTimeOffset CreatedUtc, string SourceHead,
        string HelperSha256, string PackageSha256, string HardwareId,
        int HardCapMilliseconds, int SettleMilliseconds, bool TrustSelected,
        bool PackageSelected, bool CreateSelected, string LegacyMarkerPolicy,
        string PackageOwnershipPolicy, SetupTransport Transport);

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 5 &&
                args[0] is "provision" or "uninstall" &&
                args[1] == "--request-handle" &&
                args[3] == "--result-handle")
#if SETUP_VALIDATION
            {
                var callerFixture = Environment.GetEnvironmentVariable(
                    "LIGASE_VDISPLAY_CALLER_FIXTURE");
                if (!string.IsNullOrEmpty(callerFixture))
                    return RunCallerFixture(args[4], callerFixture);
#endif
                return ExecuteFromHandles(args[0], args[2], args[4]);
#if SETUP_VALIDATION
            }
#endif
#if SETUP_VALIDATION
            if (args.Length == 2 && args[0] == "--validate-contract-fixture")
                return RunContractFixture(args[1]);
#endif
            return 10;
        }
        catch (Win32Exception)
        {
            return 11;
        }
        catch
        {
            return 12;
        }
    }

#if SETUP_VALIDATION
    private static int RunCallerFixture(string resultValue, string mode)
    {
        if (mode == "overflow")
        {
            Console.Out.Write(new string('x', 5000));
            Console.Out.Flush();
            Thread.Sleep(5000);
            return 0;
        }
        if (mode is "timeout" or "dualPipePending")
        {
            if (mode == "dualPipePending")
            {
                Console.Out.Write("pending"); Console.Out.Flush();
                Console.Error.Write("pending"); Console.Error.Flush();
            }
            Thread.Sleep(5000);
            return 0;
        }
        if (mode == "schemaInvalidSuccess")
        {
            if (!long.TryParse(resultValue, out var raw) || raw <= 0) return 40;
            using var handle = new SafeFileHandle(new IntPtr(raw), false);
            using var stream = new FileStream(handle, FileAccess.ReadWrite, 4096, false);
            var bytes = Encoding.UTF8.GetBytes(
                "{\"schemaVersion\":1,\"operation\":\"provision\"," +
                "\"operationIdsSha256\":[\"" + new string('0', 64) + "\"]," +
                "\"sourceHead\":\"" + new string('0', 40) + "\"," +
                "\"helperSha256\":\"" + new string('0', 64) + "\"," +
                "\"packageSha256\":\"" + new string('0', 64) + "\"," +
                "\"state\":\"completed\",\"code\":\"installed\"}");
            stream.Write(bytes); stream.Flush(true); return 0;
        }
        return 41;
    }
#endif

    private static int ExecuteFromHandles(string operation, string requestValue,
        string resultValue)
    {
        if (!long.TryParse(requestValue, out var requestRaw) || requestRaw <= 0 ||
            !long.TryParse(resultValue, out var resultRaw) || resultRaw <= 0 ||
            requestRaw == resultRaw)
            return 30;
        using var requestHandle = new SafeFileHandle(new IntPtr(requestRaw), false);
        using var resultHandle = new SafeFileHandle(new IntPtr(resultRaw), false);
        try
        {
            var requestIdentity = FileIdentitySha256(requestHandle);
            var resultIdentity = FileIdentitySha256(resultHandle);
            using var requestStream = new FileStream(requestHandle,
                FileAccess.Read, 4096, false);
            if (requestStream.Length is < 2 or > 16384)
                return 31;
            var requestBytes = new byte[requestStream.Length];
            requestStream.ReadExactly(requestBytes);
            if (requestStream.ReadByte() != -1)
                return 31;
            var request = ParseSetupRequest(requestBytes);
            if (!StringComparer.Ordinal.Equals(operation, request.Operation))
                return 31;
            if (!StringComparer.Ordinal.Equals(
                    request.Transport.RequestFileIdentitySha256,
                    requestIdentity) ||
                !StringComparer.Ordinal.Equals(
                    request.Transport.ResultFileIdentitySha256,
                    resultIdentity))
                return 31;
            var clock = Stopwatch.StartNew();
            var document = operation == "provision" ?
                RunProvision(request, clock) : RunUninstall(request, clock);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(document,
                JsonOptions());
            if (bytes.Length > 65536 || resultHandle.IsInvalid)
                return 32;
            using var resultStream = new FileStream(resultHandle,
                FileAccess.ReadWrite, 4096, false);
            if (resultStream.Length != 0)
                return 32;
            resultStream.Write(bytes);
            resultStream.Flush(true);
            resultStream.Position = 0;
            var readback = new byte[bytes.Length];
            resultStream.ReadExactly(readback);
            if (resultStream.ReadByte() != -1 ||
                !CryptographicOperations.FixedTimeEquals(bytes, readback) ||
                !StringComparer.Ordinal.Equals(
                    resultIdentity, FileIdentitySha256(resultHandle)))
                return 32;
            return 0;
        }
        catch
        {
            return 33;
        }
    }

    private static SetupRequest ParseSetupRequest(byte[] bytes)
    {
        var raw = new UTF8Encoding(false, true).GetString(bytes);
        using var document = JsonDocument.Parse(raw, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });
        var expected = new[] { "schemaVersion", "operation",
            "operationIdSha256", "createdUtc", "sourceHead", "helperSha256",
            "packageSha256", "hardwareId", "hardCapMilliseconds",
            "settleMilliseconds", "trustSelected", "packageSelected",
            "createSelected", "legacyMarkerPolicy",
            "packageOwnershipPolicy", "transport" };
        AssertExactProperties(document.RootElement, expected);
        var request = JsonSerializer.Deserialize<SetupRequest>(raw,
            JsonOptions()) ?? throw new InvalidDataException();
        if (request.SchemaVersion != 1 ||
            request.Operation is not ("provision" or "uninstall") ||
            !IsLowerHex(request.OperationIdSha256, 64) ||
            !IsLowerHex(request.SourceHead, 40) ||
            !IsLowerHex(request.HelperSha256, 64) ||
            !IsLowerHex(request.PackageSha256, 64) ||
            request.HardwareId != TargetHardwareId ||
            request.HardCapMilliseconds != 120000 ||
            request.SettleMilliseconds is < 500 or > 60000 ||
            request.LegacyMarkerPolicy != "v1CertificateOnly" ||
            request.PackageOwnershipPolicy !=
                "provisionOperationProvenanceOnly" ||
            request.Transport is null ||
            (request.Operation == "provision" &&
                (!request.TrustSelected || !request.PackageSelected ||
                 !request.CreateSelected)) ||
            (request.Operation == "uninstall" &&
                (request.TrustSelected || request.PackageSelected ||
                 request.CreateSelected)))
            throw new InvalidDataException();
        AssertExactProperties(document.RootElement.GetProperty("transport"),
            new[] { "mode", "operationDirectoryAcl",
                "operationDirectoryNonReparse", "requestCreateNew",
                "requestFlushCompleted", "requestWriteHandlesClosed",
                "requestReadOnlyHandle", "requestWriteSharing",
                "resultCreateNew", "resultExclusiveHandle",
                "requestFileIdentitySha256", "resultFileIdentitySha256" });
        var transport = request.Transport;
        if (transport.Mode != "inheritedHandles" ||
            transport.OperationDirectoryAcl !=
                "systemAdministratorsCallerOnly" ||
            !transport.OperationDirectoryNonReparse ||
            !transport.RequestCreateNew || !transport.RequestFlushCompleted ||
            !transport.RequestWriteHandlesClosed ||
            !transport.RequestReadOnlyHandle || transport.RequestWriteSharing ||
            !transport.ResultCreateNew || !transport.ResultExclusiveHandle ||
            !IsLowerHex(transport.RequestFileIdentitySha256, 64) ||
            !IsLowerHex(transport.ResultFileIdentitySha256, 64))
            throw new InvalidDataException();
        return request;
    }

    private static Dictionary<string, object?> RunProvision(SetupRequest request,
        Stopwatch clock)
    {
        var removed = new List<object>();
        var compensation = Compensation("notRequired", "none");
        var trust = Component("notAttempted", "none");
        var package = Component("notAttempted", "none");
        var create = Component("notAttempted", "none");
        var marker = Component("notAttempted", "none");
        object certificateStores = CertificateStoresNotAttempted();
        var zero = ZeroProof("notAttempted", 0, 0, false);
        List<Device> devices;
        try { devices = EnumerateMatches(EnumerateInstanceIds(
            DigcfAllClasses | DigcfPresent)); }
        catch { return SetupResultDocument(request, clock, "failed", "inventoryUnavailable",
            "inventoryBefore", true, UnknownInventory(),
            Removal("notRequired", removed, "none", -1, false), zero,
            trust, package, create, marker, compensation); }
        OwnershipMarker? priorMarker;
        try { priorMarker = ReadOwnershipMarker(
            GetMarkerPath(), request.PackageSha256); }
        catch (OwnershipReadException failure)
        {
            var markerReason = failure.Reason == "conflict" ?
                "conflict" : "readbackFailure";
            return SetupResultDocument(request, clock, "failed",
                "ownershipReadFailed", "readOwnership", true,
                Inventory(devices),
                Removal("notRequired", removed, "none", -1, false), zero,
                trust, package, create,
                UninstallComponent("failed", markerReason, "unknown"),
                compensation,
                Migration("failed", failure.MarkerSource,
                    failure.MarkerSource == "unknown" ? "unknown" :
                    failure.MarkerSource == "v1" ? "legacyUnknown" : "notOwned",
                    "unknown"), OwnershipAcquisitionNone(), null,
                OwnershipReadFailure(failure.Reason, failure.MarkerSource));
        }
        var healthyMarker = IsExpectedFinal(devices) && priorMarker is not null &&
            AreMarkerCertificatesPresent(priorMarker) ? priorMarker : null;
        if (healthyMarker is not null)
            return SetupResultDocument(request, clock, "completed", "alreadyInstalled",
                "completed", false, Inventory(devices),
                Removal("notRequired", removed, "none", -1, false), zero,
                Component("verified", "alreadyOwned"),
                Component("verified", "alreadyOwned"),
                Component("notRequired", "none"),
                Component("verified", "alreadyOwned"), compensation,
                Migration(healthyMarker.SchemaVersion == 1 ? "v1Read" : "v2Read",
                    healthyMarker.SchemaVersion == 1 ? "v1" : "v2",
                    healthyMarker.PackageOwnership,
                    AggregateCertificateOwnership(
                        healthyMarker.CertificateStores)),
                healthyMarker.PackageOwnership == "addedByLigase" ?
                    OwnershipAcquisitionHistorical() :
                    OwnershipAcquisitionNone(),
                healthyMarker.PackageOwnership == "addedByLigase" ?
                    healthyMarker.AcquisitionOperationIdSha256 : null,
                certificateStores: CertificateStores(
                    healthyMarker.CertificateStores));
        while (devices.Count != 0)
        {
            EnsureBudget(clock, request.HardCapMilliseconds);
            var selected = devices[0];
            bool reboot;
            int native;
            try { native = RemoveDevice(selected.InstanceId, out reboot); }
            catch { native = Marshal.GetLastWin32Error(); reboot = false; }
            if (native != 0)
                return SetupResultDocument(request, clock, "failed", "removeNativeFailed",
                    "remove", true, Inventory(devices),
                    Removal("failed", removed, "nativeFailure",
                        native < 1 ? 1 : native, false), zero, trust, package,
                    create, marker, compensation);
            if (reboot)
                return SetupResultDocument(request, clock, "failed", "removeRebootRequired",
                    "remove", true, Inventory(devices),
                    Removal("failed", removed, "rebootRequired", 0, true),
                    zero, trust, package, create, marker, compensation);
            List<Device> after;
            try { after = EnumerateMatches(EnumerateInstanceIds(
                DigcfAllClasses | DigcfPresent)); }
            catch { return SetupResultDocument(request, clock, "failed",
                "removeReadbackFailed", "inventoryAfterRemove", true,
                UnknownInventory(), Removal("failed", removed,
                    "readbackFailure", 0, false), zero, trust, package,
                create, marker, compensation); }
            if (after.Count >= devices.Count)
                return SetupResultDocument(request, clock, "failed", "removeNoProgress",
                    "inventoryAfterRemove", true, Inventory(after),
                    Removal("failed", removed, "noProgress", 0, false), zero,
                    trust, package, create, marker, compensation);
            removed.Add(new Dictionary<string, object> {
                ["nativeCode"] = 0, ["rebootRequired"] = false,
                ["strictDecrease"] = true });
            devices = after;
        }
        var samples = 0;
        var zeroStart = clock.ElapsedMilliseconds;
        string? epoch = null;
        while (samples < 3)
        {
            EnsureBudget(clock, request.HardCapMilliseconds);
            Thread.Sleep(Math.Min(request.SettleMilliseconds, 500));
            var sample = EnumerateMatches(EnumerateInstanceIds(
                DigcfAllClasses | DigcfPresent));
            var currentEpoch = InventoryEpoch(sample);
            if (sample.Count != 0 || (epoch is not null && epoch != currentEpoch))
                return SetupResultDocument(request, clock, "failed", "zeroProofFailed",
                    "stableZero", true, Inventory(sample),
                    Removal(removed.Count == 0 ? "notRequired" : "completed",
                        removed, removed.Count == 0 ? "none" : "completed",
                        removed.Count == 0 ? -1 : 0, false),
                    ZeroProof("failed", samples, checked((int)(
                        clock.ElapsedMilliseconds - zeroStart)), false),
                    trust, package, create, marker, compensation);
            epoch ??= currentEpoch;
            samples++;
        }
        zero = ZeroProof("completed", samples,
            Math.Max(1, checked((int)(clock.ElapsedMilliseconds - zeroStart))),
            true);
        var successfulRemoval = Removal(removed.Count == 0 ? "notRequired" :
            "completed", removed, removed.Count == 0 ? "none" : "completed",
            removed.Count == 0 ? -1 : 0, false);
        var driverRoot = Path.Combine(AppContext.BaseDirectory,
            "Drivers", "sudovda");
        try { var trustResult = EnsureTrust(driverRoot, priorMarker);
            trust = trustResult.Component;
            certificateStores = CertificateStores(
                trustResult.CertificateStores); }
        catch { trust = Component("failed", "nativeFailure"); return SetupResultDocument(
            request, clock, "failed", "trustFailed", "trustPackage", true,
            Inventory(Array.Empty<Device>()), successfulRemoval, zero, trust,
            package, create, marker, Compensation("failed", "trust"),
            certificateStores: certificateStores); }
        try { package = EnsurePackage(driverRoot, request.PackageSha256); }
        catch { package = Component("failed", "nativeFailure"); return SetupResultDocument(
            request, clock, "failed", "packageFailed", "trustPackage", true,
            Inventory(Array.Empty<Device>()), successfulRemoval, zero, trust,
            package, create, marker, Compensation("failed", "multiple"),
            certificateStores: certificateStores); }
        var pendingAuthority = GetProvisionAuthority(priorMarker, package,
            "notAttempted", false,
            ReadCertificateOwnership(certificateStores));
        try { RunPinnedTool(Path.Combine(driverRoot, "nefconc.exe"), new[] {
            "--create-device-node", "--class-name", "Display", "--class-guid",
            "4D36E968-E325-11CE-BFC1-08002BE10318", "--hardware-id",
            TargetHardwareId.ToLowerInvariant() }, request.HardCapMilliseconds -
            checked((int)clock.ElapsedMilliseconds));
            create = Component("completed", "created"); }
        catch { create = Component("failed", "nativeFailure"); return SetupResultDocument(
            request, clock, "failed", "createFailed", "create", true,
            Inventory(Array.Empty<Device>()), successfulRemoval, zero, trust,
            package, create, marker, Compensation("failed", "multiple"),
            pendingAuthority.Migration, pendingAuthority.Acquisition,
            pendingAuthority.HistoricalOperationIdSha256,
            certificateStores: certificateStores); }
        List<Device> final;
        try { final = EnumerateMatches(EnumerateInstanceIds(
            DigcfAllClasses | DigcfPresent)); }
        catch { return SetupResultDocument(request, clock, "failed", "finalReadbackFailed",
            "readbackAfterCreate", true, UnknownInventory(), successfulRemoval,
            zero, trust, package, create, marker,
            Compensation("failed", "multiple"), pendingAuthority.Migration,
            pendingAuthority.Acquisition,
            pendingAuthority.HistoricalOperationIdSha256,
            certificateStores: certificateStores); }
        if (!IsExpectedFinal(final))
            return SetupResultDocument(request, clock, "failed", "finalReadbackFailed",
                "readbackAfterCreate", true, Inventory(final), successfulRemoval,
                zero, trust, package, create, marker,
                Compensation("failed", "multiple"), pendingAuthority.Migration,
                pendingAuthority.Acquisition,
                pendingAuthority.HistoricalOperationIdSha256,
                certificateStores: certificateStores);
        try { WriteMarker(request, final[0], package, priorMarker,
                certificateStores); marker = Component(
            "completed", "committed"); }
        catch { marker = Component("failed", "nativeFailure");
            var failedAuthority = GetProvisionAuthority(priorMarker, package,
                "failed", true, ReadCertificateOwnership(certificateStores));
            return SetupResultDocument(
            request, clock, "failed", "markerCommitFailed", "commitMarker",
            true, Inventory(final), successfulRemoval, zero, trust, package,
            create, marker, Compensation("failed", "multiple"),
            failedAuthority.Migration, failedAuthority.Acquisition,
            failedAuthority.HistoricalOperationIdSha256,
            certificateStores: certificateStores); }
        var committedAuthority = GetProvisionAuthority(priorMarker, package,
            "completed", false, ReadCertificateOwnership(certificateStores));
        return SetupResultDocument(request, clock, "completed", "installed", "completed",
            false, Inventory(final), successfulRemoval, zero, trust, package,
            create, marker, compensation, committedAuthority.Migration,
            committedAuthority.Acquisition,
            committedAuthority.HistoricalOperationIdSha256,
            certificateStores: certificateStores);
    }

    private sealed record OwnershipMarker(int SchemaVersion,
        string PackageSha256, string PackageOwnership,
        string? AcquisitionOperationIdSha256, string PublishedInf,
        string CertificateThumbprint,
        CertificateStoreAuthority[] CertificateStores);
    private sealed record CertificateStoreAuthority(string Store,
        string Ownership, string AuthoritySource, string PreState,
        string Mutation, string Readback, string CleanupState);
    private sealed record TrustResult(Dictionary<string, object> Component,
        CertificateStoreAuthority[] CertificateStores);
    private sealed record TrustCleanupResult(
        Dictionary<string, object> Component,
        CertificateStoreAuthority[] CertificateStores);
    private sealed class OwnershipReadException : Exception
    {
        public string Reason { get; }
        public string MarkerSource { get; }
        public OwnershipReadException(string reason, string source = "unknown")
            : base("ownershipReadFailed")
        { Reason = reason; MarkerSource = source; }
    }
    private sealed record ProvisionAuthority(object Migration,
        object Acquisition, string? HistoricalOperationIdSha256);

    private static ProvisionAuthority GetProvisionAuthority(
        OwnershipMarker? prior, object package, string markerState,
        bool markerFailed, string certificateOwnership)
    {
        var installed = ReadString(package, "code") == "installed";
        if (installed)
        {
            var fromV1 = prior?.SchemaVersion == 1;
            return new ProvisionAuthority(Migration(markerFailed ?
                    (fromV1 ? "v1UpgradeFailed" : "v2CommitFailed") :
                    markerState == "completed" ?
                        (fromV1 ? "v1Upgraded" : "v2Committed") :
                        (fromV1 ? "v1UpgradePending" : "v2Pending"),
                    fromV1 ? "v1" : "none", "addedByLigase",
                    certificateOwnership),
                OwnershipAcquisitionCurrent(), null);
        }
        if (prior?.SchemaVersion == 2)
            return new ProvisionAuthority(Migration("v2Read", "v2",
                    prior.PackageOwnership, certificateOwnership),
                prior.PackageOwnership == "addedByLigase" ?
                    OwnershipAcquisitionHistorical() :
                    OwnershipAcquisitionNone(),
                prior.PackageOwnership == "addedByLigase" ?
                    prior.AcquisitionOperationIdSha256 : null);
        var fromLegacy = prior?.SchemaVersion == 1;
        return new ProvisionAuthority(Migration(markerFailed ?
                (fromLegacy ? "v1RetainedUpgradeFailed" :
                    "v2RetainedCommitFailed") :
                markerState == "completed" ?
                    (fromLegacy ? "v1Upgraded" : "v2RetainedCommitted") :
                    (fromLegacy ? "v1Read" : "notRequired"),
                fromLegacy ? "v1" : "none",
                fromLegacy ? "legacyUnknown" : "notOwned",
                certificateOwnership),
            OwnershipAcquisitionNone(), null);
    }

    private static Dictionary<string, object?> RunUninstall(SetupRequest request,
        Stopwatch clock)
    {
        var removed = new List<object>();
        var zero = ZeroProof("notAttempted", 0, 0, false);
        var notAttempted = Component("notAttempted", "none");
        List<Device> devices;
        try { devices = EnumerateMatches(EnumerateInstanceIds(
            DigcfAllClasses | DigcfPresent)); }
        catch { return SetupResultDocument(request, clock, "failed",
            "inventoryUnavailable", "inventoryBefore", true,
            UnknownInventory(), Removal("notRequired", removed, "none", -1,
            false), zero, notAttempted, notAttempted, notAttempted,
            notAttempted, Compensation("notRequired", "none")); }
        while (devices.Count != 0)
        {
            EnsureBudget(clock, request.HardCapMilliseconds);
            var selected = devices[0];
            int native; bool reboot;
            try { native = RemoveDevice(selected.InstanceId, out reboot); }
            catch { native = 1; reboot = false; }
            if (native != 0)
                return SetupResultDocument(request, clock, "failed",
                    "removeNativeFailed", "remove", true, Inventory(devices),
                    Removal("failed", removed, "nativeFailure", native, false),
                    zero, notAttempted, notAttempted, notAttempted,
                    notAttempted, Compensation("notRequired", "none"));
            if (reboot)
                return SetupResultDocument(request, clock, "failed",
                    "removeRebootRequired", "remove", true,
                    Inventory(devices), Removal("failed", removed,
                    "rebootRequired", 0, true), zero, notAttempted,
                    notAttempted, notAttempted, notAttempted,
                    Compensation("notRequired", "none"));
            List<Device> after;
            try { after = EnumerateMatches(EnumerateInstanceIds(
                DigcfAllClasses | DigcfPresent)); }
            catch { return SetupResultDocument(request, clock, "failed",
                "removeReadbackFailed", "inventoryAfterRemove", true,
                UnknownInventory(), Removal("failed", removed,
                "readbackFailure", 0, false), zero, notAttempted,
                notAttempted, notAttempted, notAttempted,
                Compensation("notRequired", "none")); }
            if (after.Count >= devices.Count)
                return SetupResultDocument(request, clock, "failed",
                    "removeNoProgress", "inventoryAfterRemove", true,
                    Inventory(after), Removal("failed", removed, "noProgress",
                    0, false), zero, notAttempted, notAttempted, notAttempted,
                    notAttempted, Compensation("notRequired", "none"));
            removed.Add(new Dictionary<string, object> {
                ["nativeCode"] = 0, ["rebootRequired"] = false,
                ["strictDecrease"] = true });
            devices = after;
        }
        var zeroStart = clock.ElapsedMilliseconds;
        string? epoch = null; var samples = 0;
        while (samples < 3)
        {
            EnsureBudget(clock, request.HardCapMilliseconds);
            Thread.Sleep(Math.Min(request.SettleMilliseconds, 500));
            var sample = EnumerateMatches(EnumerateInstanceIds(
                DigcfAllClasses | DigcfPresent));
            var currentEpoch = InventoryEpoch(sample);
            if (sample.Count != 0 || epoch is not null && epoch != currentEpoch)
                return SetupResultDocument(request, clock, "failed",
                    "zeroProofFailed", "stableZero", true, Inventory(sample),
                    Removal(removed.Count == 0 ? "notRequired" : "completed",
                        removed, removed.Count == 0 ? "none" : "completed",
                        removed.Count == 0 ? -1 : 0, false),
                    ZeroProof("failed", samples, checked((int)(
                        clock.ElapsedMilliseconds - zeroStart)), false),
                    notAttempted, notAttempted, Component("notRequired", "none"),
                    notAttempted, Compensation("notRequired", "none"));
            epoch ??= currentEpoch; samples++;
        }
        zero = ZeroProof("completed", samples, Math.Max(1, checked((int)(
            clock.ElapsedMilliseconds - zeroStart))), true);
        var removal = Removal(removed.Count == 0 ? "notRequired" : "completed",
            removed, removed.Count == 0 ? "none" : "completed",
            removed.Count == 0 ? -1 : 0, false);
        var markerPath = GetMarkerPath();
        OwnershipMarker? ownership;
        try { ownership = ReadOwnershipMarker(markerPath, request.PackageSha256); }
        catch (OwnershipReadException failure) { return SetupResultDocument(request, clock, "failed",
            "ownershipReadFailed", "cleanupOwnership", true,
            Inventory(Array.Empty<Device>()), removal, zero,
            Component("notAttempted", "none"),
            Component("notAttempted", "none"),
            Component("notRequired", "none"),
            UninstallComponent("failed", failure.Reason == "conflict" ?
                "conflict" : "readbackFailure", "unknown"),
            Compensation("notRequired", "none"),
            Migration("failed", failure.MarkerSource,
                failure.MarkerSource == "unknown" ? "unknown" :
                failure.MarkerSource == "v1" ? "legacyUnknown" : "notOwned",
                "unknown"),
            OwnershipAcquisitionNone(), null,
            OwnershipReadFailure(failure.Reason, failure.MarkerSource)); }
        if (ownership is null)
            return SetupResultDocument(request, clock, "completed",
                "alreadyAbsent", "completed", false,
                Inventory(Array.Empty<Device>()), removal, zero,
                UninstallComponent("verified", "absentNotOwned", "notOwned"),
                UninstallComponent("verified", "absentNotOwned", "notOwned"),
                Component("notRequired", "none"),
                UninstallComponent("verified", "absentNotOwned", "notOwned"),
                Compensation("notRequired", "none"));
        var migration = Migration(ownership.SchemaVersion == 1 ?
                "v1Read" : "v2Read",
            ownership.SchemaVersion == 1 ? "v1" : "v2",
            ownership.PackageOwnership,
            AggregateCertificateOwnership(ownership.CertificateStores));
        object certificateStores = CertificateStores(
            ownership.CertificateStores);
        var acquisition = ownership.PackageOwnership == "addedByLigase" ?
            OwnershipAcquisitionHistorical() : OwnershipAcquisitionNone();
        var historicalId = ownership.PackageOwnership == "addedByLigase" ?
            ownership.AcquisitionOperationIdSha256 : null;
        var package = CleanupPackage(ownership);
        if ((string)package["state"] == "failed")
            return SetupResultDocument(request, clock, "failed",
                "packageCleanupFailed", "cleanupOwnership", true,
                Inventory(Array.Empty<Device>()), removal, zero,
                Component("notAttempted", "none"), package,
                Component("notRequired", "none"),
                UninstallComponent("verified", "owned", "owned"),
                Compensation("notRequired", "none"), migration, acquisition,
                historicalId, certificateStores: certificateStores);
        var trustResult = CleanupTrust(ownership);
        var trust = trustResult.Component;
        certificateStores = CertificateStores(trustResult.CertificateStores);
        if ((string)trust["state"] == "failed")
            return SetupResultDocument(request, clock, "failed",
                "trustCleanupFailed", "cleanupOwnership", true,
                Inventory(Array.Empty<Device>()), removal, zero, trust, package,
                Component("notRequired", "none"),
                UninstallComponent("verified", "owned", "owned"),
                Compensation("notRequired", "none"), migration, acquisition,
                historicalId, certificateStores: certificateStores);
        Dictionary<string, object> marker;
        try { File.Delete(markerPath); marker = UninstallComponent(
            "completed", "removed", "owned"); }
        catch { marker = UninstallComponent("failed", "nativeFailure", "owned");
            return SetupResultDocument(request, clock, "failed",
                "markerCleanupFailed", "cleanupOwnership", true,
                Inventory(Array.Empty<Device>()), removal, zero, trust, package,
                Component("notRequired", "none"), marker,
                Compensation("notRequired", "none"), migration, acquisition,
                historicalId, certificateStores: certificateStores); }
        var successCode = ownership.PackageOwnership switch {
            "addedByLigase" => "uninstalled",
            "legacyUnknown" => "uninstalledLegacyPackageRetained",
            _ => "uninstalledSharedPackageRetained"
        };
        return SetupResultDocument(request, clock, "completed", successCode,
            "completed", false, Inventory(Array.Empty<Device>()), removal,
            zero, trust, package, Component("notRequired", "none"), marker,
            Compensation("notRequired", "none"), migration, acquisition,
            historicalId, certificateStores: certificateStores);
    }

    private static Dictionary<string, object?> SetupResultDocument(SetupRequest request,
        Stopwatch clock, string state, string code, string stage,
        bool firstFailureFrozen, object inventory, object removal,
        object zeroProof, object trust, object package, object create,
        object marker, object compensation,
        object? migration = null, object? ownershipAcquisition = null,
        string? historicalOperationIdSha256 = null,
        object? ownershipReadFailure = null,
        object? certificateStores = null)
    {
        var packageState = ReadString(package, "state");
        var packageCode = ReadString(package, "code");
        var markerState = ReadString(marker, "state");
        var currentAcquisition = request.Operation == "provision" &&
            packageState == "completed" && packageCode == "installed";
        var retainedCommit = request.Operation == "provision" &&
            packageState == "verified" && packageCode == "alreadyOwned" &&
            markerState is "completed" or "failed";
        migration ??= currentAcquisition ?
            Migration(markerState == "completed" ? "v2Committed" :
                code == "markerCommitFailed" ? "v2CommitFailed" :
                "v2Pending", "none", "addedByLigase", "owned") :
            retainedCommit ? Migration(markerState == "completed" ?
                "v2RetainedCommitted" : "v2RetainedCommitFailed", "none",
                "notOwned", "owned") :
            Migration("notRequired", "none", "notOwned", "notOwned");
        ownershipAcquisition ??= currentAcquisition ?
            OwnershipAcquisitionCurrent() : OwnershipAcquisitionNone();
        certificateStores ??= ReadString(migration, "certificateOwnership") ==
            "unknown" ? CertificateStoresUnavailable() :
            CertificateStoresNotAttempted();
        var operationIds = historicalOperationIdSha256 is null ?
            new[] { request.OperationIdSha256 } :
            new[] { request.OperationIdSha256, historicalOperationIdSha256 };
        var result = new Dictionary<string, object?>
        {
        ["schemaVersion"] = 1,
        ["operation"] = request.Operation,
        ["operationIdsSha256"] = operationIds,
        ["sourceHead"] = request.SourceHead,
        ["helperSha256"] = request.HelperSha256,
        ["packageSha256"] = request.PackageSha256,
        ["state"] = state,
        ["code"] = code,
        ["stage"] = stage,
        ["firstFailureFrozen"] = firstFailureFrozen,
        ["writtenUtc"] = DateTimeOffset.UtcNow.ToString("O"),
        ["migration"] = migration,
        ["ownershipAcquisition"] = ownershipAcquisition,
        ["certificateStores"] = certificateStores,
        ["inventory"] = inventory,
        ["removal"] = removal,
        ["zeroProof"] = zeroProof,
        ["trust"] = trust,
        ["package"] = package,
        ["create"] = create,
        ["marker"] = marker,
        ["compensation"] = compensation,
        ["execution"] = new Dictionary<string, object>
        {
            ["elapsedMilliseconds"] = Math.Min(request.HardCapMilliseconds,
                checked((int)clock.ElapsedMilliseconds)),
            ["hardCapMilliseconds"] = request.HardCapMilliseconds,
            ["resultFileState"] = "verified"
        }
        };
        if (ownershipReadFailure is not null)
            result["ownershipReadFailure"] = ownershipReadFailure;
        return result;
    }

    private static string ReadString(object value, string property)
    {
        if (value is Dictionary<string, object> dictionary &&
            dictionary.TryGetValue(property, out var found))
            return found?.ToString() ?? string.Empty;
        return string.Empty;
    }

    private static string ReadCertificateOwnership(object value)
    {
        if (value is not Dictionary<string, object> dictionary ||
            !dictionary.TryGetValue("ownedCount", out var countValue) ||
            countValue is not int count || count is < 0 or > 2)
            throw new InvalidDataException();
        return count == 0 ? "notOwned" : "owned";
    }

    private static CertificateStoreAuthority[] ReadCertificateStoreAuthorities(
        object value)
    {
        if (value is not Dictionary<string, object> dictionary ||
            !dictionary.TryGetValue("entries", out var entriesValue) ||
            entriesValue is not IEnumerable<Dictionary<string, object>> entries)
            throw new InvalidDataException();
        return entries.Select(entry => new CertificateStoreAuthority(
            ReadString(entry, "store"), ReadString(entry, "ownership"),
            ReadString(entry, "authoritySource"),
            ReadString(entry, "preState"), ReadString(entry, "mutation"),
            ReadString(entry, "readback"),
            ReadString(entry, "cleanupState"))).ToArray();
    }

    private static Dictionary<string, object> Migration(string state,
        string source, string packageOwnership, string certificateOwnership) =>
        new() { ["state"] = state, ["source"] = source,
            ["packageOwnership"] = packageOwnership,
            ["certificateOwnership"] = certificateOwnership };

    private static object CertificateStoresNotAttempted() =>
        new Dictionary<string, object> {
            ["state"] = "notAttempted", ["entries"] = Array.Empty<object>(),
            ["ownedCount"] = 0,
            ["ownershipSetSha256"] = Sha256(string.Empty) };

    private static object CertificateStoresUnavailable() =>
        new Dictionary<string, object> {
            ["state"] = "unavailable", ["entries"] = Array.Empty<object>(),
            ["ownedCount"] = -1,
            ["ownershipSetSha256"] = Sha256(string.Empty) };

    private static object CertificateStores(
        IEnumerable<CertificateStoreAuthority> source)
    {
        var entries = source.OrderBy(value => value.Store switch {
            "LocalMachine\\Root" => 0,
            "LocalMachine\\TrustedPublisher" => 1,
            _ => throw new InvalidDataException() }).ToArray();
        if (entries.Length != 2 || entries[0].Store != "LocalMachine\\Root" ||
            entries[1].Store != "LocalMachine\\TrustedPublisher")
            throw new InvalidDataException();
        var owned = entries.Count(value => value.Ownership != "notOwned");
        var canonical = string.Join("\n", entries.Select(value =>
            value.Store + "=" + value.Ownership));
        return new Dictionary<string, object> {
            ["state"] = "verified",
            ["entries"] = entries.Select(value =>
                new Dictionary<string, object> {
                    ["store"] = value.Store,
                    ["ownership"] = value.Ownership,
                    ["authoritySource"] = value.AuthoritySource,
                    ["preState"] = value.PreState,
                    ["mutation"] = value.Mutation,
                    ["readback"] = value.Readback,
                    ["cleanupState"] = value.CleanupState }).ToArray(),
            ["ownedCount"] = owned,
            ["ownershipSetSha256"] = Sha256(canonical) };
    }

    private static string AggregateCertificateOwnership(
        IEnumerable<CertificateStoreAuthority> stores) =>
        stores.Any(value => value.Ownership != "notOwned") ?
            "owned" : "notOwned";

    private static Dictionary<string, object> OwnershipAcquisitionNone() =>
        new() { ["state"] = "none", ["source"] = "none" };

    private static Dictionary<string, object> OwnershipAcquisitionCurrent() =>
        new() { ["state"] = "proven", ["source"] = "currentOperation",
            ["acquisitionOperationIndex"] = 0, ["preState"] = "absent",
            ["mutation"] = "publishedByLigaseProvision",
            ["readback"] = "presentPinned" };

    private static Dictionary<string, object> OwnershipAcquisitionHistorical() =>
        new() { ["state"] = "proven", ["source"] = "historicalOperation",
            ["acquisitionOperationIndex"] = 1,
            ["markerIdentityReadback"] = "exact", ["preState"] = "absent",
            ["mutation"] = "publishedByLigaseProvision",
            ["readback"] = "presentPinned" };

    private static Dictionary<string, object> OwnershipReadFailure(
        string reason, string source) => new() {
            ["state"] = "failed", ["reason"] = reason, ["count"] = 1,
            ["source"] = source, ["resourceMutationCount"] = 0 };

    private static object Inventory(IEnumerable<Device> source)
    {
        var values = source.ToArray();
        var nodes = values.Select(device => new Dictionary<string, object>
        {
            ["present"] = device.Present,
            ["bound"] = !string.IsNullOrWhiteSpace(device.DriverInf)
        }).ToArray();
        var residual = values.Length switch
        {
            0 => "zero",
            1 when nodes[0]["bound"] is true => "exactOneBound",
            1 => "exactOneUnbound",
            _ => "multiple"
        };
        var hash = values.Length == 0 ? Sha256(string.Empty) : Sha256(
            JsonSerializer.Serialize(values.Select(value =>
                value.InstanceId.ToUpperInvariant()).OrderBy(value => value,
                    StringComparer.Ordinal)));
        return new Dictionary<string, object>
        {
            ["state"] = "proven", ["nodes"] = nodes,
            ["uniqueSetSha256"] = hash, ["residual"] = residual
        };
    }

    private static object UnknownInventory() => new Dictionary<string, object>
    {
        ["state"] = "unknown", ["nodes"] = Array.Empty<object>(),
        ["uniqueSetSha256"] = Sha256(string.Empty), ["residual"] = "unknown"
    };

    private static object Removal(string state, List<object> removed,
        string terminal, int nativeCode, bool rebootRequired) =>
        new Dictionary<string, object>
        {
            ["state"] = state, ["removed"] = removed.ToArray(),
            ["terminal"] = terminal, ["nativeCode"] = nativeCode,
            ["rebootRequired"] = rebootRequired
        };

    private static object ZeroProof(string state, int samples,
        int windowMilliseconds, bool epochStable) =>
        new Dictionary<string, object>
        {
            ["state"] = state,
            ["samples"] = Enumerable.Range(0, samples).Select(_ =>
                new Dictionary<string, object> { ["zero"] = true }).ToArray(),
            ["windowMilliseconds"] = windowMilliseconds,
            ["epochStable"] = epochStable
        };

    private static Dictionary<string, object> Component(string state,
        string code) => new() { ["state"] = state, ["code"] = code };
    private static Dictionary<string, object> UninstallComponent(string state,
        string code, string ownership) => new() {
            ["state"] = state, ["code"] = code, ["ownership"] = ownership };
    private static Dictionary<string, object> Compensation(string state,
        string code) => new() { ["state"] = state, ["code"] = code };

    private static void EnsureBudget(Stopwatch clock, int hardCap)
    {
        if (clock.ElapsedMilliseconds >= hardCap)
            throw new TimeoutException();
    }

    private static bool IsExpectedFinal(IReadOnlyList<Device> devices) =>
        devices.Count == 1 && devices[0].Present &&
        !string.IsNullOrWhiteSpace(devices[0].DriverInf);

    private static int RemoveDevice(string instanceId, out bool rebootRequired)
    {
        using var set = SafeDeviceInfoSet.Open(DigcfAllClasses);
        foreach (var infoValue in set.Enumerate())
        {
            var info = infoValue;
            if (!StringComparer.Ordinal.Equals(
                    ReadInstanceId(set.Handle, ref info), instanceId))
                continue;
            var ids = ReadMultiString(set.Handle, ref info, SpdrpHardwareId);
            if (!ids.Any(value => StringComparer.OrdinalIgnoreCase.Equals(
                    value, TargetHardwareId)))
                throw new InvalidDataException();
            if (!DiUninstallDevice(IntPtr.Zero, set.Handle, ref info, 0,
                    out rebootRequired))
                return Marshal.GetLastWin32Error();
            return 0;
        }
        throw new InvalidDataException();
    }

    private static TrustResult EnsureTrust(string root,
        OwnershipMarker? priorMarker)
    {
        var path = Path.Combine(root, "sudovda.cer");
        using var certificate = new X509Certificate2(path);
        var added = false;
        var authorities = new List<CertificateStoreAuthority>();
        foreach (var name in new[] { StoreName.Root, StoreName.TrustedPublisher })
        {
            using var store = new X509Store(name, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            var existing = store.Certificates.Find(
                X509FindType.FindByThumbprint, certificate.Thumbprint, false);
            var wasPresent = existing.Count != 0;
            if (!wasPresent) { store.Add(certificate); added = true; }
            if (store.Certificates.Find(X509FindType.FindByThumbprint,
                    certificate.Thumbprint, false).Count == 0)
                throw new InvalidDataException();
            var storePath = "LocalMachine\\" + name;
            var prior = priorMarker?.CertificateStores.SingleOrDefault(
                value => value.Store == storePath);
            if (prior is not null && prior.Ownership != "notOwned")
                authorities.Add(prior with { CleanupState = "notAttempted" });
            else if (!wasPresent)
                authorities.Add(new CertificateStoreAuthority(storePath,
                    "addedByLigase", "currentOperation", "absent",
                    "addedByThisOperation", "present", "notAttempted"));
            else
                authorities.Add(new CertificateStoreAuthority(storePath,
                    "notOwned", "currentObservation", "present", "none",
                    "present", "notAttempted"));
        }
        return new TrustResult(Component(added ? "completed" : "verified",
            added ? "added" : "alreadyOwned"), authorities.ToArray());
    }

    private static Dictionary<string, object> EnsurePackage(string root,
        string packageSha256)
    {
        var inf = Path.Combine(root, "SudoVDA.inf");
        var packageBytes = new[] { "SudoVDA.dll", "SudoVDA.inf",
                "sudovda.cat", "sudovda.cer" }
            .OrderBy(name => name, StringComparer.Ordinal)
            .SelectMany(name => File.ReadAllBytes(Path.Combine(root, name)))
            .ToArray();
        if (!StringComparer.Ordinal.Equals(Convert.ToHexString(
                SHA256.HashData(packageBytes)).ToLowerInvariant(), packageSha256))
            throw new InvalidDataException();
        var destination = new StringBuilder(260);
        if (SetupCopyOEMInfW(inf, null, 1, SpCopyNoOverwrite,
                destination, destination.Capacity, out _, IntPtr.Zero))
        {
            if (destination.Length == 0 ||
                !File.Exists(Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.Windows), "INF",
                    Path.GetFileName(destination.ToString()))))
                throw new InvalidDataException();
            return Component("completed", "installed");
        }
        if (Marshal.GetLastWin32Error() == ErrorFileExists &&
            destination.Length != 0 &&
            File.Exists(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.Windows), "INF",
                Path.GetFileName(destination.ToString()))))
            return Component("verified", "alreadyOwned");
        throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static bool AreMarkerCertificatesPresent(OwnershipMarker marker)
    {
        foreach (var authority in marker.CertificateStores)
        {
            if (authority.Ownership == "notOwned")
                continue;
            var name = authority.Store switch {
                "LocalMachine\\Root" => StoreName.Root,
                "LocalMachine\\TrustedPublisher" => StoreName.TrustedPublisher,
                _ => throw new InvalidDataException() };
            using var store = new X509Store(name, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            if (store.Certificates.Find(X509FindType.FindByThumbprint,
                    marker.CertificateThumbprint, false).Count == 0)
                return false;
        }
        return true;
    }

    private static void RunPinnedTool(string path, IEnumerable<string> args,
        int timeoutMilliseconds)
    {
        if (timeoutMilliseconds < 1 || !File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException();
        using var process = new Process { StartInfo = new ProcessStartInfo
        {
            FileName = path, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = false, RedirectStandardError = false
        } };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        if (!process.Start() || !process.WaitForExit(timeoutMilliseconds) ||
            process.ExitCode != 0)
        {
            try { process.Kill(true); process.WaitForExit(1000); } catch { }
            throw new InvalidOperationException();
        }
    }

    private static string GetMarkerPath() => Path.Combine(
        AppContext.BaseDirectory, "Drivers", "sudovda",
        ".ligase-driver-ownership.json");

    private static OwnershipMarker? ReadOwnershipMarker(string path,
        string packageSha256)
    {
        if (!File.Exists(path)) return null;
        byte[] bytes;
        string identity;
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new OwnershipReadException("identityChanged");
            using var handle = File.OpenHandle(path, FileMode.Open,
                FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
            identity = FileIdentitySha256(handle);
            using var stream = new FileStream(handle, FileAccess.Read, 4096,
                false);
            if (stream.Length is < 2 or > 16384)
                throw new OwnershipReadException("unreadable");
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1 ||
                !StringComparer.Ordinal.Equals(identity,
                    FileIdentitySha256(handle)))
                throw new OwnershipReadException("identityChanged");
        }
        catch (OwnershipReadException) { throw; }
        catch (IOException) { throw new OwnershipReadException("io"); }
        catch (UnauthorizedAccessException) {
            throw new OwnershipReadException("io"); }
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes, new JsonDocumentOptions {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8 }); }
        catch (JsonException) { throw new OwnershipReadException("unreadable"); }
        using (document)
        {
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new OwnershipReadException("type");
        var properties = document.RootElement.EnumerateObject().ToArray();
        if (properties.Select(item => item.Name).Distinct(StringComparer.Ordinal)
                .Count() != properties.Length)
            throw new OwnershipReadException("duplicateProperty");
        if (!document.RootElement.TryGetProperty("schemaVersion", out var version) ||
            version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var schemaVersion))
            throw new OwnershipReadException("type");
        if (schemaVersion == 1)
        {
            try { AssertExactProperties(document.RootElement, new[] {
                    "schemaVersion", "certificateThumbprint",
                    "certificateStores" }); }
            catch { throw new OwnershipReadException("schema"); }
            if (document.RootElement.GetProperty("certificateThumbprint").ValueKind !=
                    JsonValueKind.String ||
                document.RootElement.GetProperty("certificateStores").ValueKind !=
                    JsonValueKind.Array)
                throw new OwnershipReadException("type");
            var legacyThumb = document.RootElement.GetProperty(
                "certificateThumbprint").GetString();
            var legacyStores = document.RootElement.GetProperty(
                "certificateStores").EnumerateArray().Select(value =>
                    value.ValueKind == JsonValueKind.String ?
                        value.GetString() ?? "" : "").ToArray();
            if (legacyThumb?.Length != 40 || legacyStores.Length is < 1 or > 2 ||
                legacyStores.Any(value => value is not (
                    "LocalMachine\\Root" or
                    "LocalMachine\\TrustedPublisher")))
                throw new OwnershipReadException("conflict", "v1");
            var legacyAuthorities = new[] { "Root", "TrustedPublisher" }
                .Select(name => {
                    var storePath = "LocalMachine\\" + name;
                    return legacyStores.Contains(storePath,
                        StringComparer.Ordinal) ?
                        new CertificateStoreAuthority(storePath,
                            "legacyOwned", "historicalMarker",
                            "historicalMarker", "none", "markerExact",
                            "notAttempted") :
                        new CertificateStoreAuthority(storePath, "notOwned",
                            "currentObservation", "present", "none",
                            "present", "notAttempted");
                }).ToArray();
            return new OwnershipMarker(1, packageSha256, "legacyUnknown", null,
                string.Empty, legacyThumb, legacyAuthorities);
        }
        if (schemaVersion != 2)
            throw new OwnershipReadException("version");
        try { AssertExactProperties(document.RootElement, new[] { "schemaVersion",
            "packageSha256", "packageOwnership",
            "acquisitionOperationIdSha256", "publishedInf",
            "certificateThumbprint", "certificateStores",
            "certificateOwnedCount", "certificateOwnershipSetSha256" }); }
        catch { throw new OwnershipReadException("schema"); }
        if (document.RootElement.GetProperty("packageSha256").ValueKind !=
                JsonValueKind.String ||
            document.RootElement.GetProperty("packageOwnership").ValueKind !=
                JsonValueKind.String ||
            document.RootElement.GetProperty(
                "acquisitionOperationIdSha256").ValueKind is not (
                    JsonValueKind.String or JsonValueKind.Null) ||
            document.RootElement.GetProperty("publishedInf").ValueKind !=
                JsonValueKind.String ||
            document.RootElement.GetProperty("certificateThumbprint").ValueKind !=
                JsonValueKind.String ||
            document.RootElement.GetProperty("certificateStores").ValueKind !=
                JsonValueKind.Array ||
            document.RootElement.GetProperty("certificateOwnedCount").ValueKind !=
                JsonValueKind.Number ||
            document.RootElement.GetProperty(
                "certificateOwnershipSetSha256").ValueKind !=
                JsonValueKind.String)
            throw new OwnershipReadException("type");
        if (
            document.RootElement.GetProperty("packageSha256").GetString() !=
                packageSha256)
            throw new OwnershipReadException("conflict", "v2");
        var ownership = document.RootElement.GetProperty(
            "packageOwnership").GetString();
        var acquisitionElement = document.RootElement.GetProperty(
            "acquisitionOperationIdSha256");
        var acquisition = acquisitionElement.ValueKind == JsonValueKind.Null ?
            null : acquisitionElement.GetString();
        var inf = document.RootElement.GetProperty("publishedInf").GetString();
        var thumb = document.RootElement.GetProperty(
            "certificateThumbprint").GetString();
        var stores = ParseMarkerCertificateStores(document.RootElement.GetProperty(
            "certificateStores"));
        var ownedCount = document.RootElement.GetProperty(
            "certificateOwnedCount").GetInt32();
        var ownershipHash = document.RootElement.GetProperty(
            "certificateOwnershipSetSha256").GetString();
        if (ownership is not ("addedByLigase" or "notOwned" or
                "legacyUnknown") ||
            (ownership == "addedByLigase" ?
                acquisition is null || !IsLowerHex(acquisition, 64) :
                acquisition is not null) ||
            string.IsNullOrWhiteSpace(inf) || Path.GetFileName(inf) != inf ||
            !inf.StartsWith("oem", StringComparison.OrdinalIgnoreCase) ||
            !inf.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) ||
            thumb?.Length != 40 || stores.Length != 2 ||
            ownedCount != stores.Count(value => value.Ownership != "notOwned") ||
            ownershipHash != Sha256(string.Join("\n", stores.Select(value =>
                value.Store + "=" + value.Ownership))))
            throw new OwnershipReadException("conflict", "v2");
        return new OwnershipMarker(schemaVersion, packageSha256, ownership,
            acquisition, inf, thumb, stores);
        }
    }

    private static Dictionary<string, object> CleanupPackage(
        OwnershipMarker marker)
    {
        var infPath = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.Windows), "INF", marker.PublishedInf);
        if (marker.PackageOwnership != "addedByLigase")
        {
            if (marker.SchemaVersion == 1 ||
                marker.PackageOwnership == "legacyUnknown")
                return UninstallComponent("verified",
                    "retainedLegacyUnknown", "legacyUnknown");
            return File.Exists(infPath) ?
                UninstallComponent("verified", "retainedNotOwned", "notOwned") :
                UninstallComponent("verified", "absentNotOwned", "notOwned");
        }
        if (!File.Exists(infPath))
            return UninstallComponent("verified", "absentOwned", "owned");
        try
        {
            RunPinnedTool(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.System), "pnputil.exe"),
                new[] { "/delete-driver", marker.PublishedInf, "/uninstall" },
                30000);
            return !File.Exists(infPath) ?
                UninstallComponent("completed", "removed", "owned") :
                UninstallComponent("failed", "readbackFailure", "owned");
        }
        catch { return UninstallComponent("failed", "nativeFailure", "owned"); }
    }

    private static TrustCleanupResult CleanupTrust(OwnershipMarker marker)
    {
        var output = new List<CertificateStoreAuthority>();
        var retainedNotOwnedPresent = false;
        try
        {
            foreach (var authority in marker.CertificateStores)
            {
                if (authority.Ownership == "notOwned")
                {
                    var retainedStoreName = authority.Store switch {
                        "LocalMachine\\Root" => StoreName.Root,
                        "LocalMachine\\TrustedPublisher" =>
                            StoreName.TrustedPublisher,
                        _ => throw new InvalidDataException() };
                    using var retainedStore = new X509Store(retainedStoreName,
                        StoreLocation.LocalMachine);
                    retainedStore.Open(OpenFlags.ReadOnly);
                    retainedNotOwnedPresent |= retainedStore.Certificates.Find(
                        X509FindType.FindByThumbprint,
                        marker.CertificateThumbprint, false).Count != 0;
                    output.Add(authority with { CleanupState = "retained" });
                    continue;
                }
                var storeName = authority.Store switch {
                    "LocalMachine\\Root" => StoreName.Root,
                    "LocalMachine\\TrustedPublisher" =>
                        StoreName.TrustedPublisher,
                    _ => throw new InvalidDataException() };
                using var store = new X509Store(storeName, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                var matches = store.Certificates.Find(
                    X509FindType.FindByThumbprint,
                    marker.CertificateThumbprint, false);
                var removed = matches.Count != 0;
                foreach (var certificate in matches) {
                    store.Remove(certificate); }
                if (store.Certificates.Find(X509FindType.FindByThumbprint,
                        marker.CertificateThumbprint, false).Count != 0)
                {
                    output.Add(authority with { CleanupState = "failed" });
                    return new TrustCleanupResult(UninstallComponent("failed",
                        "readbackFailure", "owned"), output.Concat(
                        marker.CertificateStores.Skip(output.Count)).ToArray());
                }
                output.Add(authority with { CleanupState = removed ?
                    "removed" : "absentOwned" });
            }
            var owned = output.Where(value => value.Ownership != "notOwned")
                .ToArray();
            Dictionary<string, object> component = owned.Length == 0 ?
                UninstallComponent("verified",
                    retainedNotOwnedPresent ?
                        "retainedNotOwned" : "absentNotOwned", "notOwned") :
                UninstallComponent(owned.Any(value =>
                        value.CleanupState == "removed") ? "completed" :
                        "verified",
                    owned.Any(value => value.CleanupState == "removed") ?
                        "removed" : "absentOwned", "owned");
            return new TrustCleanupResult(component, output.ToArray());
        }
        catch
        {
            var remaining = marker.CertificateStores.Skip(output.Count).ToArray();
            if (remaining.Length != 0)
                output.Add(remaining[0] with { CleanupState = "failed" });
            output.AddRange(remaining.Skip(1));
            return new TrustCleanupResult(UninstallComponent("failed",
                "nativeFailure", "owned"), output.ToArray());
        }
    }

    private static void WriteMarker(SetupRequest request, Device device,
        object package, OwnershipMarker? prior, object certificateStores)
    {
        var path = GetMarkerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var packageAddedByCurrentOperation =
            ReadString(package, "code") == "installed";
        var packageOwnership = packageAddedByCurrentOperation ?
            "addedByLigase" : prior?.PackageOwnership ?? "notOwned";
        var acquisitionOperationId = packageAddedByCurrentOperation ?
            request.OperationIdSha256 :
            packageOwnership == "addedByLigase" ?
                prior?.AcquisitionOperationIdSha256 : null;
        if (packageOwnership == "addedByLigase" &&
            (acquisitionOperationId is null ||
             !IsLowerHex(acquisitionOperationId, 64)))
            throw new InvalidDataException();
        var markerStores = ReadCertificateStoreAuthorities(certificateStores)
            .Select(value => new CertificateStoreAuthority(value.Store,
                value.Ownership, "historicalMarker", "historicalMarker",
                "none", "markerExact", "notAttempted")).ToArray();
        var markerOwnershipHash = Sha256(string.Join("\n", markerStores.Select(
            value => value.Store + "=" + value.Ownership)));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, object?> {
                ["schemaVersion"] = 2,
                ["packageSha256"] = request.PackageSha256,
                ["packageOwnership"] = packageOwnership,
                ["acquisitionOperationIdSha256"] = acquisitionOperationId,
                ["publishedInf"] = Path.GetFileName(device.DriverInf),
                ["certificateThumbprint"] = new X509Certificate2(Path.Combine(
                    AppContext.BaseDirectory, "Drivers", "sudovda",
                    "sudovda.cer")).Thumbprint,
                ["certificateStores"] = markerStores.Select(value =>
                    new Dictionary<string, object> {
                        ["store"] = value.Store,
                        ["ownership"] = value.Ownership,
                        ["authoritySource"] = value.AuthoritySource,
                        ["preState"] = value.PreState,
                        ["mutation"] = value.Mutation,
                        ["readback"] = value.Readback,
                        ["cleanupState"] = value.CleanupState }).ToArray(),
                ["certificateOwnedCount"] = markerStores.Count(value =>
                    value.Ownership != "notOwned"),
                ["certificateOwnershipSetSha256"] = markerOwnershipHash
            }, JsonOptions());
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096,
                FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null, true);
            else File.Move(temporary, path);
            if (!File.ReadAllBytes(path).SequenceEqual(bytes))
                throw new IOException();
        }
        finally { try { File.Delete(temporary); } catch { } }
    }

    private static void AssertExactProperties(JsonElement element,
        IEnumerable<string> expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException();
        var properties = element.EnumerateObject().ToArray();
        var names = expected.ToArray();
        if (properties.Length != names.Length ||
            properties.Select(item => item.Name).Distinct(StringComparer.Ordinal)
                .Count() != names.Length ||
            names.Any(name => properties.All(item => item.Name != name)))
            throw new InvalidDataException();
    }

    private static CertificateStoreAuthority[] ParseMarkerCertificateStores(
        JsonElement element)
    {
        var values = element.EnumerateArray().ToArray();
        if (values.Length != 2)
            throw new OwnershipReadException("conflict", "v2");
        var expected = new[] { "LocalMachine\\Root",
            "LocalMachine\\TrustedPublisher" };
        var result = new CertificateStoreAuthority[2];
        for (var index = 0; index < values.Length; index++)
        {
            try { AssertExactProperties(values[index], new[] { "store",
                "ownership", "authoritySource", "preState", "mutation",
                "readback", "cleanupState" }); }
            catch { throw new OwnershipReadException("schema", "v2"); }
            if (values[index].EnumerateObject().Any(property =>
                    property.Value.ValueKind != JsonValueKind.String))
                throw new OwnershipReadException("type", "v2");
            var value = new CertificateStoreAuthority(
                values[index].GetProperty("store").GetString() ?? "",
                values[index].GetProperty("ownership").GetString() ?? "",
                values[index].GetProperty("authoritySource").GetString() ?? "",
                values[index].GetProperty("preState").GetString() ?? "",
                values[index].GetProperty("mutation").GetString() ?? "",
                values[index].GetProperty("readback").GetString() ?? "",
                values[index].GetProperty("cleanupState").GetString() ?? "");
            if (value.Store != expected[index] ||
                value.Ownership is not ("addedByLigase" or "notOwned" or
                    "legacyOwned") ||
                value.AuthoritySource != "historicalMarker" ||
                value.PreState != "historicalMarker" ||
                value.Mutation != "none" || value.Readback != "markerExact" ||
                value.CleanupState != "notAttempted")
                throw new OwnershipReadException("conflict", "v2");
            result[index] = value;
        }
        return result;
    }

    private static string FileIdentitySha256(SafeFileHandle handle)
    {
        if (handle.IsInvalid || !GetFileInformationByHandle(handle,
                out var info) || (info.FileAttributes & 0x10) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var payload = string.Join("\n", info.VolumeSerialNumber,
            info.FileIndexHigh, info.FileIndexLow);
        return Sha256(payload);
    }

#if SETUP_VALIDATION
    private static int RunContractFixture(string mode)
    {
        var result = mode switch
        {
            "inventoryZero" => new { state = "available", count = 0,
                present = 0, bound = 0, strictDecrease = false },
            "inventoryOne" => new { state = "available", count = 1,
                present = 1, bound = 1, strictDecrease = false },
            "inventoryPhantom" => new { state = "available", count = 1,
                present = 0, bound = 0, strictDecrease = false },
            "inventoryTwo" => new { state = "available", count = 2,
                present = 1, bound = 1, strictDecrease = false },
            "removalLegal" => new { state = "removed", count = 1,
                present = 0, bound = 0, strictDecrease = true },
            "authorityDrift" => new { state = "rejected", count = 1,
                present = 1, bound = 0, strictDecrease = false },
            _ => throw new InvalidDataException()
        };
        Console.Out.WriteLine(JsonSerializer.Serialize(result));
        return mode == "authorityDrift" ? 20 : 0;
    }
#endif

    private static string InventoryEpoch(List<Device> source)
    {
        var ordered = source.OrderBy(item => item.InstanceId,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.InstanceId, StringComparer.Ordinal);
        var payload = JsonSerializer.Serialize(ordered.Select(item => new
        {
            instanceId = item.InstanceId.ToUpperInvariant(), item.Present,
            item.Status, item.DriverInf
        }));
        return Sha256(payload);
    }

    private static string Sha256(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool IsLowerHex(string value, int length) =>
        value.Length == length && value.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static List<Device> EnumerateMatches(HashSet<string> present)
    {
        var result = new List<Device>();
        using var set = SafeDeviceInfoSet.Open(DigcfAllClasses);
        foreach (var infoValue in set.Enumerate())
        {
            var info = infoValue;
            var hardwareIds = ReadMultiString(set.Handle, ref info, SpdrpHardwareId);
            if (!hardwareIds.Any(value =>
                    StringComparer.OrdinalIgnoreCase.Equals(value, TargetHardwareId)))
                continue;
            var instanceId = ReadInstanceId(set.Handle, ref info);
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new InvalidDataException();
            var isPresent = present.Contains(instanceId);
            var status = ReadStatus(info.DevInst, isPresent);
            var driverInf = ReadDriverInf(set.Handle, ref info);
            result.Add(new Device(instanceId, isPresent, status, driverInf));
        }
        return result
            .OrderBy(item => item.InstanceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.InstanceId, StringComparer.Ordinal)
            .ToList();
    }

    private static HashSet<string> EnumerateInstanceIds(uint flags)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var set = SafeDeviceInfoSet.Open(flags);
        foreach (var infoValue in set.Enumerate())
        {
            var info = infoValue;
            if (!result.Add(ReadInstanceId(set.Handle, ref info)))
                throw new InvalidDataException();
        }
        return result;
    }

    private static string ReadInstanceId(IntPtr set, ref SpDevinfoData info)
    {
        SetupDiGetDeviceInstanceIdW(set, ref info, null, 0, out var required);
        if (required < 2 || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var value = new StringBuilder(checked((int)required));
        if (!SetupDiGetDeviceInstanceIdW(set, ref info, value,
                required, out var actual) || actual != required)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return value.ToString();
    }

    private static string[] ReadMultiString(IntPtr set, ref SpDevinfoData info,
        uint property)
    {
        SetupDiGetDeviceRegistryPropertyW(set, ref info, property,
            out _, null, 0, out var required);
        var error = Marshal.GetLastWin32Error();
        if (error is ErrorInvalidData or ErrorNotFound)
            return Array.Empty<string>();
        if (error != ErrorInsufficientBuffer || required < 2 || required > 65536)
            throw new Win32Exception(error);
        var buffer = new byte[required];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref info, property,
                out _, buffer, required, out var actual) || actual != required)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return Encoding.Unicode.GetString(buffer)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string ReadDriverInf(IntPtr set, ref SpDevinfoData info)
    {
        // devpkey.h: DEVPKEY_Device_DriverInfPath =
        // {A8B865DD-2E3D-4094-AD97-E593A70C75D6}, 5.
        var key = new Devpropkey
        {
            Fmtid = DevpkeyDriverInfPath,
            Pid = DevpkeyDeviceDriverInfPathPid
        };
        SetupDiGetDevicePropertyW(set, ref info, ref key, out var type,
            null, 0, out var required, 0);
        var error = Marshal.GetLastWin32Error();
        if (error is ErrorInvalidData or ErrorNotFound)
            return string.Empty;
        if (error != ErrorInsufficientBuffer || type != DevpropTypeString ||
            required < 2 || required > 32768)
            throw new Win32Exception(error);
        var buffer = new byte[required];
        if (!SetupDiGetDevicePropertyW(set, ref info, ref key, out type,
                buffer, required, out var actual, 0) || actual != required ||
            type != DevpropTypeString)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    private static string ReadStatus(uint devInst, bool present)
    {
        if (!present)
            return "Unknown";
        var result = CM_Get_DevNode_Status(out _, out var problem, devInst, 0);
        if (result != 0)
            throw new Win32Exception(checked((int)result));
        return problem == 0 ? "OK" : "Problem";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Devpropkey
    {
        public Guid Fmtid;
        public uint Pid;
    }

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

    private sealed class SafeDeviceInfoSet : IDisposable
    {
        public IntPtr Handle { get; }
        private SafeDeviceInfoSet(IntPtr handle) => Handle = handle;
        public static SafeDeviceInfoSet Open(uint flags)
        {
            var handle = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, flags);
            if (handle == new IntPtr(-1))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return new SafeDeviceInfoSet(handle);
        }
        public IEnumerable<SpDevinfoData> Enumerate()
        {
            for (uint index = 0; ; index++)
            {
                var info = new SpDevinfoData
                {
                    CbSize = checked((uint)Marshal.SizeOf<SpDevinfoData>())
                };
                if (SetupDiEnumDeviceInfo(Handle, index, ref info))
                {
                    yield return info;
                    continue;
                }
                const int noMoreItems = 259;
                var error = Marshal.GetLastWin32Error();
                if (error == noMoreItems)
                    yield break;
                throw new Win32Exception(error);
            }
        }
        public void Dispose() => SetupDiDestroyDeviceInfoList(Handle);
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid,
        string? enumerator, IntPtr hwndParent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index,
        ref SpDevinfoData info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set,
        ref SpDevinfoData info, StringBuilder? instanceId, uint size,
        out uint required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set,
        ref SpDevinfoData info, uint property, out uint type, byte[]? buffer,
        uint size, out uint required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr set,
        ref SpDevinfoData info, ref Devpropkey key, out uint type,
        byte[]? buffer, uint size, out uint required, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupCopyOEMInfW(string sourceInfFileName,
        string? oemSourceMediaLocation, uint oemSourceMediaType,
        uint copyStyle, StringBuilder destinationInfFileName,
        int destinationInfFileNameSize, out int requiredSize,
        IntPtr destinationInfFileNameComponent);
    [DllImport("newdev.dll", SetLastError = true)]
    private static extern bool DiUninstallDevice(IntPtr hwndParent,
        IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, uint flags,
        [MarshalAs(UnmanagedType.Bool)] out bool needReboot);
    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(out uint status,
        out uint problemNumber, uint devInst, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle, out ByHandleFileInformation information);
}
