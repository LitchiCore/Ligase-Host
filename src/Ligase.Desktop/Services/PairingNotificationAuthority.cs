using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ligase.Host.Core.Services;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Ligase.Host.Desktop.Services;

public sealed record PairingNotificationPayload(
    string Action,
    string InstanceKey,
    string RequestId,
    string DeviceName,
    string SafetyCode,
    string Tag,
    string Group);

public sealed record PairingNotificationShowResult(
    string Setting,
    string Result,
    int HResult,
    uint? NotificationId,
    bool ReadbackFound);

public sealed record PairingNotificationWithdrawResult(
    string Result,
    int HResult,
    bool ReadbackAbsent);

public interface IPairingNotificationPlatform : IDisposable
{
    event Action<string>? Invoked;
    string Register();
    Task<PairingNotificationShowResult> ShowAsync(
        PairingNotificationPayload payload);
    Task<PairingNotificationWithdrawResult> WithdrawAsync(
        string tag,
        string group);
    void Unregister();
}

public sealed class WindowsPairingNotificationPlatform : IPairingNotificationPlatform
{
    private AppNotificationManager? _manager;

    public event Action<string>? Invoked;

    public string Register()
    {
        var manager = AppNotificationManager.Default;
        manager.NotificationInvoked += OnInvoked;
        try
        {
            manager.Register();
            _manager = manager;
            return manager.Setting.ToString();
        }
        catch
        {
            manager.NotificationInvoked -= OnInvoked;
            throw;
        }
    }

    public async Task<PairingNotificationShowResult> ShowAsync(
        PairingNotificationPayload payload)
    {
        var manager = _manager ?? throw new InvalidOperationException(
            "App notification manager is not registered.");
        var setting = manager.Setting.ToString();
        if (!string.Equals(setting, "Enabled", StringComparison.Ordinal))
        {
            return new PairingNotificationShowResult(
                setting, "disabled", 0, null, false);
        }

        try
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("action", payload.Action);
            if (payload.Action == "pairing")
            {
                builder
                    .AddArgument("instance", payload.InstanceKey)
                    .AddArgument("request", payload.RequestId)
                    .AddText("Ligase Host 有设备等待确认")
                    .AddText($"{payload.DeviceName} · 安全码 {payload.SafetyCode}")
                    .AddText("打开 Ligase Host 确认");
            }
            else
            {
                builder
                    .AddText("Ligase Host Windows 通知测试")
                    .AddText("通知投递正常。点击可返回现有 Ligase Host 窗口。");
            }
            var notification = builder.BuildNotification();
            notification.Group = payload.Group;
            notification.Tag = payload.Tag;
            manager.Show(notification);

            var found = false;
            for (var attempt = 0; attempt < 5 && !found; attempt++)
            {
                var notifications = await manager.GetAllAsync();
                found = notifications.Any(item =>
                    item.Id == notification.Id &&
                    string.Equals(item.Tag, payload.Tag, StringComparison.Ordinal) &&
                    string.Equals(item.Group, payload.Group, StringComparison.Ordinal));
                if (!found && attempt < 4) await Task.Delay(100);
            }
            return new PairingNotificationShowResult(
                setting,
                found ? "shownReadback" : "showAcceptedNotReadBack",
                0,
                notification.Id,
                found);
        }
        catch (Exception exception)
        {
            return new PairingNotificationShowResult(
                setting,
                "showFailed",
                Marshal.GetHRForException(exception),
                null,
                false);
        }
    }

    public async Task<PairingNotificationWithdrawResult> WithdrawAsync(
        string tag,
        string group)
    {
        var manager = _manager ?? throw new InvalidOperationException(
            "App notification manager is not registered.");
        try
        {
            await manager.RemoveByTagAndGroupAsync(tag, group);
            var absent = false;
            for (var attempt = 0; attempt < 5 && !absent; attempt++)
            {
                var notifications = await manager.GetAllAsync();
                absent = !notifications.Any(item =>
                    string.Equals(item.Tag, tag, StringComparison.Ordinal) &&
                    string.Equals(item.Group, group, StringComparison.Ordinal));
                if (!absent && attempt < 4) await Task.Delay(100);
            }
            return new PairingNotificationWithdrawResult(
                absent ? "withdrawnReadback" : "withdrawUnproven",
                0,
                absent);
        }
        catch (Exception exception)
        {
            return new PairingNotificationWithdrawResult(
                "withdrawFailed",
                Marshal.GetHRForException(exception),
                false);
        }
    }

    private void OnInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args) => Invoked?.Invoke(args.Argument);

    public void Unregister()
    {
        if (_manager is null) return;
        _manager.NotificationInvoked -= OnInvoked;
        _manager.Unregister();
        _manager = null;
    }

    public void Dispose() => Unregister();
}

public sealed record PairingNotificationEvidence(
    int SchemaVersion,
    string RequestCorrelationSha256,
    string RegistrationState,
    string AppNotificationSetting,
    bool ShowAttempted,
    string ShowResult,
    int ShowHResult,
    uint? NotificationId,
    string Tag,
    string Group,
    DateTimeOffset? EmittedUtc,
    bool ActivationReceived,
    bool? ActivationRequestMatch,
    string WithdrawReason,
    string WithdrawResult,
    int WithdrawHResult,
    bool? WithdrawReadbackAbsent);

public sealed class PairingNotificationEvidenceStore(LigasePaths paths)
{
    private readonly object _writeSync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string EvidencePath => Path.Combine(
        paths.RootDirectory,
        "diagnostics",
        "pairing-notification-v1.json");

    public void Write(PairingNotificationEvidence evidence)
    {
        lock (_writeSync)
        {
            var directory = Path.GetDirectoryName(EvidencePath)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(
                directory,
                $".pairing-notification-{Guid.NewGuid():N}.tmp");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(evidence, JsonOptions);
            try
            {
                using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(true);
                }
                File.Move(temporary, EvidencePath, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    internal static string RequestCorrelation(string instanceKey, string requestId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"ligase-pairing-notification-v1\n{instanceKey}\n{requestId}")))
            .ToLowerInvariant();
}
