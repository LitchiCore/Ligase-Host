using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.AppLifecycle;

namespace Ligase.Host.Desktop.Services;

public sealed class PairingNotificationService : IDisposable
{
    private const string Group = "ligase-pairing";
    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private bool _registered;

    public event Action<string, string>? Activated;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public void Initialize()
    {
        try
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnInvoked;
            manager.Register();
            _registered = true;
            IsAvailable = true;
        }
        catch (Exception exception)
        {
            MarkUnavailable(exception);
        }
    }

    public void Show(
        string instanceKey,
        string requestId,
        string deviceName,
        string safetyCode)
    {
        var key = $"{instanceKey}|{requestId}";
        lock (_sync)
        {
            if (!IsAvailable || !_shown.Add(key)) return;
        }
        try
        {
            var notification = new AppNotificationBuilder()
                .AddArgument("action", "pairing")
                .AddArgument("instance", instanceKey)
                .AddArgument("request", requestId)
                .AddText("Ligase Host 有设备等待确认")
                .AddText($"{deviceName} · 安全码 {safetyCode}")
                .AddText("打开 Ligase Host 确认")
                .BuildNotification();
            notification.Group = Group;
            notification.Tag = NotificationTag(key);
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception exception)
        {
            lock (_sync) _shown.Remove(key);
            MarkUnavailable(exception);
        }
    }

    public void Remove(string instanceKey, string requestId)
    {
        var key = $"{instanceKey}|{requestId}";
        lock (_sync) _shown.Remove(key);
        if (!IsAvailable) return;
        _ = RemoveCoreAsync(NotificationTag(key));
    }

    private void OnInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        var values = ParseArguments(args.Argument);
        if (values.TryGetValue("instance", out var instance) &&
            values.TryGetValue("request", out var request))
            Activated?.Invoke(instance, request);
    }

    public bool TryHandleActivation(AppActivationArguments arguments)
    {
        if (arguments.Kind != ExtendedActivationKind.AppNotification ||
            arguments.Data is not AppNotificationActivatedEventArgs notification)
            return false;
        OnInvoked(AppNotificationManager.Default, notification);
        return true;
    }

    internal static IReadOnlyDictionary<string, string> ParseArguments(
        string arguments)
    {
        return arguments.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => Uri.UnescapeDataString(pair[1]),
                StringComparer.Ordinal);
    }

    private static string NotificationTag(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private async Task RemoveCoreAsync(string tag)
    {
        try
        {
            await AppNotificationManager.Default.RemoveByTagAndGroupAsync(
                tag,
                Group);
        }
        catch (Exception exception)
        {
            MarkUnavailable(exception);
        }
    }

    private void MarkUnavailable(Exception exception)
    {
        IsAvailable = false;
        UnavailableReason =
            "Windows 通知当前不可用，将继续在 Ligase Host 窗口内提示。" +
            $"({exception.GetType().Name})";
    }

    public void Dispose()
    {
        if (!_registered) return;
        var manager = AppNotificationManager.Default;
        manager.NotificationInvoked -= OnInvoked;
        manager.Unregister();
        _registered = false;
    }
}
