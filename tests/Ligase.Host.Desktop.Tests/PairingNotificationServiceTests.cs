using Ligase.Host.Desktop.Services;
using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class PairingNotificationServiceTests
{
    private readonly List<string> _roots = [];

    [TestMethod]
    public void ActivationArgumentsRoundTripEncodedInstanceAndRequest()
    {
        var values = PairingNotificationService.ParseArguments(
            "action=pairing&instance=start%7C48989&" +
            "request=9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4");

        Assert.AreEqual("pairing", values["action"]);
        Assert.AreEqual("start|48989", values["instance"]);
        Assert.AreEqual(
            "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
            values["request"]);
    }

    [TestMethod]
    public void DuplicateActivationArgumentIsRejected()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            PairingNotificationService.ParseArguments(
                "request=a&request=b"));
    }

    [TestMethod]
    public void NotificationTestActivationHasNoPairingRequestAuthority()
    {
        var values = PairingNotificationService.ParseArguments(
            "action=notification-test");

        Assert.AreEqual("notification-test", values["action"]);
        Assert.IsFalse(values.ContainsKey("instance"));
        Assert.IsFalse(values.ContainsKey("request"));
    }

    [TestMethod]
    public void SyntheticToastCheckIsOneShotAndDoesNotPersistCredentials()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "scripts", "ligase", "test-attended-pairing-synthetic.py"));

        StringAssert.Contains(source, "\"ui-toast-reject\"");
        StringAssert.Contains(source, "\"WAITING_FOR_TOAST_REJECT\"");
        StringAssert.Contains(source, "\"safetyCode\": enrollment.safety_code");
        StringAssert.Contains(source, "token=enrollment.token");
        StringAssert.Contains(source, "\"terminalState\": \"rejected\"");
        StringAssert.Contains(source, "\"liveRequestRemoved\": True");
        StringAssert.Contains(source, "\"NO_REQUEST_CHECK_PASS\"");
        StringAssert.Contains(source, "\"requestCalls\": 0");
        Assert.IsFalse(source.Contains("--output", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("write_text(", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("write_bytes(", StringComparison.Ordinal));
        StringAssert.Contains(source, "\"result\": \"FAIL\"");
        StringAssert.Contains(source, "\"terminalState\": \"cancelled\"");
        StringAssert.Contains(source, "\"liveRequestRemoved\": True");
        StringAssert.Contains(source, "\"reasonCode\": \"toastDecisionTimeout\"");
    }

    [TestMethod]
    public async Task EnabledShowRequiresNotificationCenterReadbackAndPersistsSafeEvidence()
    {
        var platform = new FakePlatform
        {
            ShowResult = new("Enabled", "shownReadback", 0, 42, true)
        };
        var (service, store) = CreateService(platform);
        service.Initialize();

        await service.ShowCoreAsync(
            "nonce|host|48989",
            "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
            "Android Device",
            "ABCD-EFGH");

        Assert.IsTrue(service.IsAvailable);
        StringAssert.Contains(service.StatusText, "通知中心读回");
        using var document = JsonDocument.Parse(File.ReadAllBytes(store.EvidencePath));
        var root = document.RootElement;
        Assert.AreEqual("shownReadback", root.GetProperty("showResult").GetString());
        Assert.AreEqual(42u, root.GetProperty("notificationId").GetUInt32());
        Assert.AreEqual(64, root.GetProperty("requestCorrelationSha256").GetString()!.Length);
        var raw = File.ReadAllText(store.EvidencePath);
        Assert.IsFalse(raw.Contains("9dbbb480", StringComparison.Ordinal));
        Assert.IsFalse(raw.Contains("ABCD-EFGH", StringComparison.Ordinal));
        Assert.IsFalse(raw.Contains("Android Device", StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow("DisabledForApplication", "Windows 设置已关闭")]
    [DataRow("DisabledForUser", "当前 Windows 用户")]
    [DataRow("DisabledByGroupPolicy", "组策略")]
    [DataRow("Unsupported", "不支持")]
    public async Task DisabledSettingsAreTypedAndHumanReadable(
        string setting,
        string expectedText)
    {
        var platform = new FakePlatform
        {
            RegistrationSetting = setting,
            ShowResult = new(setting, "disabled", 0, null, false)
        };
        var (service, store) = CreateService(platform);
        service.Initialize();
        await service.ShowCoreAsync("instance", Guid.NewGuid().ToString("D"), "device", "CODE-CODE");

        Assert.IsFalse(service.IsAvailable);
        StringAssert.Contains(service.StatusText, expectedText);
        using var document = JsonDocument.Parse(File.ReadAllBytes(store.EvidencePath));
        Assert.AreEqual("disabled", document.RootElement.GetProperty("showResult").GetString());
        Assert.IsFalse(document.RootElement.GetProperty("showAttempted").GetBoolean());
    }

    [TestMethod]
    public async Task AcceptedButMissingReadbackIsNotReportedAsDelivered()
    {
        var platform = new FakePlatform
        {
            ShowResult = new("Enabled", "showAcceptedNotReadBack", 0, 8, false)
        };
        var (service, _) = CreateService(platform);
        service.Initialize();
        await service.ShowCoreAsync("instance", Guid.NewGuid().ToString("D"), "device", "CODE-CODE");

        Assert.IsFalse(service.IsAvailable);
        StringAssert.Contains(service.StatusText, "未读回同一条通知");
    }

    [TestMethod]
    public async Task ShowFailureAndDuplicateAreClosedWithoutSecondEmission()
    {
        var platform = new FakePlatform
        {
            ShowResult = new("Enabled", "showFailed", unchecked((int)0x80004005), null, false)
        };
        var (service, store) = CreateService(platform);
        service.Initialize();
        const string request = "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4";
        await service.ShowCoreAsync("instance", request, "device", "CODE-CODE");
        await service.ShowCoreAsync("instance", request, "device", "CODE-CODE");

        Assert.AreEqual(1, platform.ShowCalls);
        using var document = JsonDocument.Parse(File.ReadAllBytes(store.EvidencePath));
        Assert.AreEqual("duplicate", document.RootElement.GetProperty("showResult").GetString());
    }

    [TestMethod]
    public void StaleActivationIsPendingUntilLiveProjectionRejectsIt()
    {
        var platform = new FakePlatform();
        var (service, store) = CreateService(platform);
        var activationCalls = 0;
        service.Activated += (_, _) => activationCalls++;
        service.Initialize();

        platform.Emit("action=pairing&instance=old&request=00000000-0000-0000-0000-000000000001");
        service.RecordActivationMatch(
            "old", "00000000-0000-0000-0000-000000000001", false);

        Assert.AreEqual(1, activationCalls);
        using var document = JsonDocument.Parse(File.ReadAllBytes(store.EvidencePath));
        Assert.IsTrue(document.RootElement.GetProperty("activationReceived").GetBoolean());
        Assert.IsFalse(document.RootElement.GetProperty("activationRequestMatch").GetBoolean());
    }

    [TestMethod]
    public void DuplicateActivationRaisesOnlyOnce()
    {
        var platform = new FakePlatform();
        var (service, _) = CreateService(platform);
        var activationCalls = 0;
        service.Activated += (_, _) => activationCalls++;
        service.Initialize();
        const string arguments =
            "action=pairing&instance=instance&request=00000000-0000-0000-0000-000000000003";

        platform.Emit(arguments);
        platform.Emit(arguments);

        Assert.AreEqual(1, activationCalls);
    }

    [TestMethod]
    public async Task WithdrawalPersistsReadbackAndReason()
    {
        var platform = new FakePlatform();
        var (service, store) = CreateService(platform);
        service.Initialize();
        const string request = "00000000-0000-0000-0000-000000000002";
        await service.ShowCoreAsync("instance", request, "device", "CODE-CODE");
        service.Remove("instance", request);

        await WaitForEvidenceAsync(store.EvidencePath, "withdrawnReadback");
        using var document = JsonDocument.Parse(File.ReadAllBytes(store.EvidencePath));
        Assert.AreEqual("requestRemoved", document.RootElement.GetProperty("withdrawReason").GetString());
        Assert.IsTrue(document.RootElement.GetProperty("withdrawReadbackAbsent").GetBoolean());
    }

    [TestMethod]
    public void UnpackagedRegistrationContractMatchesMicrosoftModel()
    {
        var project = File.ReadAllText(FindRepositoryFile(
            "src", "Ligase.Desktop", "Ligase.Host.Desktop.csproj"));
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "Ligase.Desktop", "Services", "PairingNotificationAuthority.cs"));

        StringAssert.Contains(project, "<WindowsPackageType>None</WindowsPackageType>");
        Assert.IsTrue(source.IndexOf("NotificationInvoked += OnInvoked", StringComparison.Ordinal) <
                      source.IndexOf("manager.Register()", StringComparison.Ordinal));
        StringAssert.Contains(source, "manager.Setting");
        StringAssert.Contains(source, "manager.GetAllAsync()");
        Assert.IsFalse(source.Contains("AUMID", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void EvidenceSchemaIsClosedAndExcludesRawPairingAuthority()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(
            FindRepositoryFile(
                "docs", "ligase-host",
                "pairing-notification-evidence-v1.schema.json")));
        var root = document.RootElement;
        Assert.IsFalse(root.GetProperty("additionalProperties").GetBoolean());
        var required = root.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToHashSet(StringComparer.Ordinal);
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "requestCorrelationSha256", "registrationState",
                "appNotificationSetting", "showAttempted", "showResult",
                "showHResult", "notificationId", "tag", "group",
                "emittedUtc", "activationReceived", "activationRequestMatch",
                "withdrawReason", "withdrawResult", "withdrawHResult",
                "withdrawReadbackAbsent"
            },
            required.ToArray());
        var raw = File.ReadAllText(FindRepositoryFile(
            "docs", "ligase-host",
            "pairing-notification-evidence-v1.schema.json"));
        foreach (var forbidden in new[]
                 {
                     "requestId", "safetyCode", "deviceName", "requestToken",
                     "certificate", "sourceAddress"
                 })
            Assert.IsFalse(raw.Contains($"\"{forbidden}\"", StringComparison.Ordinal));
    }

    private (PairingNotificationService Service, PairingNotificationEvidenceStore Store)
        CreateService(FakePlatform platform)
    {
        var root = Path.Combine(
            @"D:\LitchiCore\Build",
            $"ligase-notification-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        _roots.Add(root);
        var store = new PairingNotificationEvidenceStore(new LigasePaths(root));
        return (new PairingNotificationService(platform, store), store);
    }

    private static async Task WaitForEvidenceAsync(string path, string result)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                if (document.RootElement.GetProperty("withdrawResult").GetString() == result)
                    return;
            }
            await Task.Delay(20);
        }
        Assert.Fail($"Evidence did not reach {result}.");
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var root in _roots)
            if (Directory.Exists(root)) Directory.Delete(root, true);
        _roots.Clear();
    }

    private sealed class FakePlatform : IPairingNotificationPlatform
    {
        public event Action<string>? Invoked;
        public string RegistrationSetting { get; set; } = "Enabled";
        public PairingNotificationShowResult ShowResult { get; set; } =
            new("Enabled", "shownReadback", 0, 1, true);
        public PairingNotificationWithdrawResult WithdrawResult { get; set; } =
            new("withdrawnReadback", 0, true);
        public int ShowCalls { get; private set; }

        public string Register() => RegistrationSetting;
        public Task<PairingNotificationShowResult> ShowAsync(PairingNotificationPayload payload)
        {
            ShowCalls++;
            return Task.FromResult(ShowResult);
        }
        public Task<PairingNotificationWithdrawResult> WithdrawAsync(string tag, string group) =>
            Task.FromResult(WithdrawResult);
        public void Emit(string arguments) => Invoked?.Invoke(arguments);
        public void Unregister() { }
        public void Dispose() { }
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }
}
