using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;

namespace Ligase.Host.Desktop.Services;

public sealed class PairingNotificationService : IDisposable
{
    private const string Group = "ligase-pairing";
    private readonly IPairingNotificationPlatform _platform;
    private readonly PairingNotificationEvidenceStore _evidenceStore;
    private readonly Dictionary<string, PairingNotificationEvidence> _shown =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, PairingNotificationEvidence> _activations =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _activated = new(StringComparer.Ordinal);
    private readonly HashSet<string> _removed = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private bool _registered;
    private string _registrationState = "uninitialized";
    private string _setting = "Unsupported";

    public PairingNotificationService(
        IPairingNotificationPlatform platform,
        PairingNotificationEvidenceStore evidenceStore)
    {
        _platform = platform;
        _evidenceStore = evidenceStore;
    }

    public event Action<string, string>? Activated;
    public event Action? TestActivated;
    public event Action? StatusChanged;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string StatusText { get; private set; } = "Windows 通知尚未初始化。";

    public void Initialize()
    {
        try
        {
            _platform.Invoked += OnInvoked;
            _setting = _platform.Register();
            _registered = true;
            _registrationState = "registered";
            ApplySetting(_setting);
            Persist(NewEvidence(
                PairingNotificationEvidenceStore.RequestCorrelation(
                    "registration", "startup"),
                NotificationTag("registration|startup")));
        }
        catch (Exception exception)
        {
            _registrationState = "registrationUnavailable";
            MarkUnavailable(
                "Windows 通知注册失败，待批准设备仍会显示在 Ligase Host 的设备页。",
                exception);
            Persist(NewEvidence(
                PairingNotificationEvidenceStore.RequestCorrelation(
                    "registration", "startup"),
                NotificationTag("registration|startup")));
        }
    }

    public void Show(
        string instanceKey,
        string requestId,
        string deviceName,
        string safetyCode) =>
        _ = ShowCoreAsync(instanceKey, requestId, deviceName, safetyCode);

    internal async Task ShowCoreAsync(
        string instanceKey,
        string requestId,
        string deviceName,
        string safetyCode)
    {
        var key = Key(instanceKey, requestId);
        var correlation = PairingNotificationEvidenceStore.RequestCorrelation(
            instanceKey, requestId);
        var tag = NotificationTag(key);
        lock (_sync)
        {
            if (_shown.ContainsKey(key))
            {
                Persist(NewEvidence(correlation, tag) with
                {
                    ShowResult = "duplicate"
                });
                return;
            }
            _shown[key] = NewEvidence(correlation, tag);
        }

        PairingNotificationShowResult result;
        try
        {
            result = await _platform.ShowAsync(new PairingNotificationPayload(
                "pairing",
                instanceKey,
                requestId,
                deviceName,
                safetyCode,
                tag,
                Group));
        }
        catch (Exception exception)
        {
            result = new PairingNotificationShowResult(
                _setting,
                "showFailed",
                Marshal.GetHRForException(exception),
                null,
                false);
        }

        _setting = result.Setting;
        var evidence = NewEvidence(correlation, tag) with
        {
            ShowAttempted = result.Result != "disabled",
            ShowResult = result.Result,
            ShowHResult = result.HResult,
            NotificationId = result.NotificationId,
            EmittedUtc = result.Result is "shownReadback" or "showAcceptedNotReadBack"
                ? DateTimeOffset.UtcNow
                : null
        };
        bool removed;
        lock (_sync)
        {
            removed = _removed.Contains(key);
            if (!removed) _shown[key] = evidence;
        }
        if (removed)
        {
            var withdrawal = await _platform.WithdrawAsync(tag, Group);
            Persist(evidence with
            {
                WithdrawReason = "requestRemoved",
                WithdrawResult = withdrawal.Result,
                WithdrawHResult = withdrawal.HResult,
                WithdrawReadbackAbsent = withdrawal.ReadbackAbsent
            });
            return;
        }
        Persist(evidence);
        ApplyDelivery(result);
    }

    public void Remove(string instanceKey, string requestId)
    {
        var key = Key(instanceKey, requestId);
        PairingNotificationEvidence? evidence;
        lock (_sync)
        {
            _removed.Add(key);
            _shown.Remove(key, out evidence);
        }
        if (evidence is null || !_registered) return;
        _ = RemoveCoreAsync(evidence);
    }

    public void ShowTest()
    {
        if (!IsAvailable)
            throw new InvalidOperationException(
                UnavailableReason ?? "Windows 通知当前不可用。");
        _ = ShowTestCoreAsync();
    }

    private async Task ShowTestCoreAsync()
    {
        var requestId = Guid.NewGuid().ToString("D");
        var result = await _platform.ShowAsync(new PairingNotificationPayload(
            "notification-test",
            "notification-test",
            requestId,
            "Windows 通知测试",
            "无配对权限",
            NotificationTag(requestId),
            "ligase-notification-test"));
        ApplyDelivery(result);
    }

    private void OnInvoked(string arguments)
    {
        IReadOnlyDictionary<string, string> values;
        try { values = ParseArguments(arguments); }
        catch (ArgumentException) { return; }
        if (values.TryGetValue("action", out var action) &&
            action == "notification-test")
        {
            TestActivated?.Invoke();
            return;
        }
        if (!values.TryGetValue("instance", out var instance) ||
            !values.TryGetValue("request", out var request))
            return;

        var key = Key(instance, request);
        PairingNotificationEvidence evidence;
        bool firstActivation;
        lock (_sync)
        {
            var matched = _shown.TryGetValue(key, out var current);
            evidence = (current ?? NewEvidence(
                PairingNotificationEvidenceStore.RequestCorrelation(instance, request),
                NotificationTag(key))) with
            {
                ActivationReceived = true,
                ActivationRequestMatch = matched ? true : null
            };
            if (matched) _shown[key] = evidence;
            _activations[key] = evidence;
            firstActivation = _activated.Add(key);
        }
        Persist(evidence);
        if (firstActivation)
            Activated?.Invoke(instance, request);
    }

    public void RecordActivationMatch(
        string instanceKey,
        string requestId,
        bool matched)
    {
        var key = Key(instanceKey, requestId);
        PairingNotificationEvidence evidence;
        lock (_sync)
        {
            evidence = (_activations.TryGetValue(key, out var current)
                ? current
                : NewEvidence(
                    PairingNotificationEvidenceStore.RequestCorrelation(
                        instanceKey, requestId),
                    NotificationTag(key))) with
            {
                ActivationReceived = true,
                ActivationRequestMatch = matched
            };
            _activations[key] = evidence;
            if (_shown.ContainsKey(key)) _shown[key] = evidence;
        }
        Persist(evidence);
    }

    public bool TryHandleActivation(AppActivationArguments arguments)
    {
        if (arguments.Kind != ExtendedActivationKind.AppNotification ||
            arguments.Data is not Microsoft.Windows.AppNotifications.AppNotificationActivatedEventArgs notification)
            return false;
        OnInvoked(notification.Argument);
        return true;
    }

    internal static IReadOnlyDictionary<string, string> ParseArguments(
        string arguments) =>
        arguments.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => Uri.UnescapeDataString(pair[1]),
                StringComparer.Ordinal);

    private static string Key(string instanceKey, string requestId) =>
        $"{instanceKey}|{requestId}";

    private static string NotificationTag(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private PairingNotificationEvidence NewEvidence(string correlation, string tag) =>
        new(
            1,
            correlation,
            _registrationState,
            _setting,
            false,
            "notAttempted",
            0,
            null,
            tag,
            Group,
            null,
            false,
            null,
            "none",
            "notAttempted",
            0,
            null);

    private async Task RemoveCoreAsync(PairingNotificationEvidence evidence)
    {
        PairingNotificationWithdrawResult result;
        try
        {
            result = await _platform.WithdrawAsync(evidence.Tag, evidence.Group);
        }
        catch (Exception exception)
        {
            result = new PairingNotificationWithdrawResult(
                "withdrawFailed",
                Marshal.GetHRForException(exception),
                false);
        }
        Persist(evidence with
        {
            WithdrawReason = "requestRemoved",
            WithdrawResult = result.Result,
            WithdrawHResult = result.HResult,
            WithdrawReadbackAbsent = result.ReadbackAbsent
        });
        if (!result.ReadbackAbsent)
        {
            UnavailableReason =
                "Windows 通知撤回状态无法确认；设备页中的请求状态仍以 Host 实时列表为准。";
            StatusChanged?.Invoke();
        }
    }

    private void ApplySetting(string setting)
    {
        IsAvailable = string.Equals(setting, "Enabled", StringComparison.Ordinal);
        UnavailableReason = IsAvailable ? null : setting switch
        {
            "DisabledForApplication" => "Windows 设置已关闭 Ligase Host 通知，请在系统通知设置中重新启用。",
            "DisabledForUser" => "当前 Windows 用户已关闭应用通知，请在系统通知设置中重新启用。",
            "DisabledByGroupPolicy" => "Windows 组策略已禁用应用通知；待批准设备仍会显示在设备页。",
            "DisabledByManifest" => "当前应用通知配置不可用；待批准设备仍会显示在设备页。",
            _ => "当前 Windows 版本不支持 Ligase Host 应用通知；请使用设备页处理请求。"
        };
        StatusText = IsAvailable
            ? "Windows 通知已注册。通知会进入通知中心；“勿扰/专注助手”可能隐藏弹出横幅，可按 Win+N 查看。"
            : UnavailableReason!;
        StatusChanged?.Invoke();
    }

    private void ApplyDelivery(PairingNotificationShowResult result)
    {
        if (result.Result == "shownReadback")
        {
            IsAvailable = true;
            UnavailableReason = null;
            StatusText =
                "通知已在 Windows 通知中心读回。若未出现横幅，可能被“勿扰/专注助手”抑制，请按 Win+N 查看。";
        }
        else if (result.Result == "disabled")
        {
            ApplySetting(result.Setting);
            return;
        }
        else if (result.Result == "showAcceptedNotReadBack")
        {
            IsAvailable = false;
            UnavailableReason =
                "Windows 已接受通知请求，但通知中心未读回同一条通知。请打开设备页处理该请求。";
            StatusText = UnavailableReason;
        }
        else
        {
            IsAvailable = false;
            UnavailableReason =
                $"Windows 通知发送失败（HRESULT 0x{result.HResult:X8}），请在设备页处理该请求。";
            StatusText = UnavailableReason;
        }
        StatusChanged?.Invoke();
    }

    private void MarkUnavailable(string message, Exception exception)
    {
        IsAvailable = false;
        UnavailableReason = $"{message}（HRESULT 0x{Marshal.GetHRForException(exception):X8}）";
        StatusText = UnavailableReason;
        StatusChanged?.Invoke();
    }

    private void Persist(PairingNotificationEvidence evidence)
    {
        try { _evidenceStore.Write(evidence); }
        catch (Exception exception)
        {
            IsAvailable = false;
            UnavailableReason =
                $"Windows 通知证据无法保存（{exception.GetType().Name}）；请在设备页处理请求。";
            StatusText = UnavailableReason;
            StatusChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            _platform.Invoked -= OnInvoked;
            _platform.Unregister();
            _registered = false;
        }
        _platform.Dispose();
    }
}
