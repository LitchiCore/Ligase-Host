using Ligase.Host.Core.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;

namespace Ligase.Host.Desktop.Services;

public sealed class AttendedPairingUiCoordinator : IDisposable
{
    private readonly AttendedPairingCoordinator _pairing;
    private readonly PairingNotificationService _notifications;
    private readonly SingleInstanceService _singleInstance;
    private readonly Queue<AttendedPairingReadyEvent> _prompts = new();
    private Window? _window;
    private AppWindow? _appWindow;
    private FrameworkElement? _root;
    private Action<string?>? _navigate;
    private Action? _showWindow;
    private Func<bool>? _isPairingPage;
    private Func<Task>? _refreshPairingPage;
    private bool _isActivated;
    private bool _promptOpen;
    private ContentDialog? _activeDialog;
    private string? _activeRequestId;
    private (string InstanceKey, string RequestId)? _pendingActivation;

    public AttendedPairingUiCoordinator(
        AttendedPairingCoordinator pairing,
        PairingNotificationService notifications,
        SingleInstanceService singleInstance)
    {
        _pairing = pairing;
        _notifications = notifications;
        _singleInstance = singleInstance;
    }

    public void Attach(
        Window window,
        AppWindow appWindow,
        FrameworkElement root,
        Action<string?> navigate,
        Action showWindow,
        Func<bool> isPairingPage,
        Func<Task> refreshPairingPage)
    {
        _window = window;
        _appWindow = appWindow;
        _root = root;
        _navigate = navigate;
        _showWindow = showWindow;
        _isPairingPage = isPairingPage;
        _refreshPairingPage = refreshPairingPage;
        window.Activated += OnWindowActivated;
        _pairing.ReadyForApproval += OnPairingReady;
        _pairing.RequestRemoved += OnPairingRemoved;
        _pairing.ProjectionChanged += OnPairingProjectionChanged;
        _notifications.Activated += OnNotificationActivated;
        _notifications.TestActivated += OnTestNotificationActivated;
        _singleInstance.RedirectedActivation += OnRedirectedActivation;
    }

    public void HandleInitialActivation(AppActivationArguments arguments) =>
        _notifications.TryHandleActivation(arguments);

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args) =>
        _isActivated =
            args.WindowActivationState != WindowActivationState.Deactivated;

    private void OnPairingReady(AttendedPairingReadyEvent pairingEvent) =>
        Enqueue(() =>
        {
            var minimized =
                _appWindow?.Presenter is OverlappedPresenter presenter &&
                presenter.State == OverlappedPresenterState.Minimized;
            if (!_isActivated || _appWindow?.IsVisible != true || minimized)
            {
                _notifications.Show(
                    pairingEvent.InstanceKey,
                    pairingEvent.Request.RequestId,
                    pairingEvent.Request.Device.Name,
                    pairingEvent.Request.SafetyCode);
                return;
            }
            if (_isPairingPage?.Invoke() == true) return;
            _prompts.Enqueue(pairingEvent);
            _ = ShowNextPromptAsync();
        });

    private void OnPairingRemoved(AttendedPairingRemovedEvent pairingEvent)
    {
        _notifications.Remove(pairingEvent.InstanceKey, pairingEvent.RequestId);
        Enqueue(async () =>
        {
            if (string.Equals(
                    _activeRequestId,
                    pairingEvent.RequestId,
                    StringComparison.Ordinal))
                _activeDialog?.Hide();
            RemoveQueued(pairingEvent.RequestId);
            if (_refreshPairingPage is not null)
                await _refreshPairingPage();
        });
    }

    private void OnPairingProjectionChanged(
        AttendedPairingProjection projection) =>
        Enqueue(() =>
        {
            var live = projection.Requests
                .Select(item => item.RequestId)
                .ToHashSet(StringComparer.Ordinal);
            if (_activeRequestId is { } active && !live.Contains(active))
                _activeDialog?.Hide();
            if (_prompts.Count > 0)
            {
                var retained = _prompts
                    .Where(item => live.Contains(item.Request.RequestId))
                    .ToArray();
                _prompts.Clear();
                foreach (var item in retained) _prompts.Enqueue(item);
            }
            if (_pendingActivation is not { } pending) return;
            _pendingActivation = null;
            if (string.Equals(
                    projection.Core.InstanceKey,
                    pending.InstanceKey,
                    StringComparison.Ordinal) &&
                live.Contains(pending.RequestId))
            {
                _notifications.RecordActivationMatch(
                    pending.InstanceKey, pending.RequestId, true);
                _navigate?.Invoke(pending.RequestId);
            }
            else
            {
                _notifications.RecordActivationMatch(
                    pending.InstanceKey, pending.RequestId, false);
                _ = ShowUnavailableAsync();
            }
        });

    private void OnNotificationActivated(string instanceKey, string requestId) =>
        Enqueue(() =>
        {
            var current = _pairing.Current;
            if (current is null)
            {
                _pendingActivation = (instanceKey, requestId);
                _showWindow?.Invoke();
                return;
            }
            if (string.Equals(
                    current.Core.InstanceKey,
                    instanceKey,
                    StringComparison.Ordinal) &&
                current.Requests.Any(item => item.RequestId == requestId))
            {
                _notifications.RecordActivationMatch(
                    instanceKey, requestId, true);
                _navigate?.Invoke(requestId);
            }
            else
            {
                _notifications.RecordActivationMatch(
                    instanceKey, requestId, false);
                _ = ShowUnavailableAsync();
            }
        });

    private void OnTestNotificationActivated() =>
        Enqueue(() => _showWindow?.Invoke());

    private void OnRedirectedActivation(AppActivationArguments arguments) =>
        Enqueue(() =>
        {
            if (!_notifications.TryHandleActivation(arguments))
                _showWindow?.Invoke();
        });

    private async Task ShowNextPromptAsync()
    {
        if (_promptOpen || _prompts.Count == 0 || _root is null) return;
        _promptOpen = true;
        var pairingEvent = _prompts.Dequeue();
        try
        {
            if (_pairing.Current?.Requests.Any(
                    item => item.RequestId ==
                            pairingEvent.Request.RequestId) != true)
                return;
            var dialog = new ContentDialog
            {
                XamlRoot = _root.XamlRoot,
                Title = "有设备等待配对",
                Content =
                    $"{pairingEvent.Request.Device.Name}\n" +
                    $"安全码 {pairingEvent.Request.SafetyCode}\n\n" +
                    "请在 Android 设备上核对相同安全码，" +
                    "然后前往设备页允许或拒绝。",
                PrimaryButtonText = "打开设备页",
                CloseButtonText = "稍后处理",
                DefaultButton = ContentDialogButton.Primary
            };
            _activeDialog = dialog;
            _activeRequestId = pairingEvent.Request.RequestId;
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                _navigate?.Invoke(pairingEvent.Request.RequestId);
        }
        finally
        {
            _activeDialog = null;
            _activeRequestId = null;
            _promptOpen = false;
            if (_prompts.Count > 0) await ShowNextPromptAsync();
        }
    }

    private async Task ShowUnavailableAsync()
    {
        _showWindow?.Invoke();
        if (_root is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = "配对请求已失效",
            Content =
                "该请求已经完成、超时或属于先前的核心实例。" +
                "请在 Android 客户端重新发起。",
            CloseButtonText = "知道了"
        };
        await dialog.ShowAsync();
    }

    private void RemoveQueued(string requestId)
    {
        if (_prompts.Count == 0) return;
        var retained = _prompts
            .Where(item => item.Request.RequestId != requestId)
            .ToArray();
        _prompts.Clear();
        foreach (var item in retained) _prompts.Enqueue(item);
    }

    private void Enqueue(Action action) =>
        _window?.DispatcherQueue.TryEnqueue(() => action());

    private void Enqueue(Func<Task> action) =>
        _window?.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Ligase pairing UI action failed: {exception.Message}");
            }
        });

    public void Dispose()
    {
        if (_window is not null) _window.Activated -= OnWindowActivated;
        _pairing.ReadyForApproval -= OnPairingReady;
        _pairing.RequestRemoved -= OnPairingRemoved;
        _pairing.ProjectionChanged -= OnPairingProjectionChanged;
        _notifications.Activated -= OnNotificationActivated;
        _notifications.TestActivated -= OnTestNotificationActivated;
        _singleInstance.RedirectedActivation -= OnRedirectedActivation;
    }
}
