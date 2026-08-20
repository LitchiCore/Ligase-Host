using Ligase.Host.Desktop.Pages;
using Ligase.Host.Desktop.Presentation.LayoutCatalog;
using Ligase.Host.Desktop.Presentation.Onboarding;
using Ligase.Host.Desktop.Services;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using WinRT.Interop;

namespace Ligase.Host.Desktop;

public sealed partial class MainWindow : Window
{
    private const double MinimumNavigationPaneWidth = 220;
    private const double MaximumNavigationPaneWidth = 420;

    private readonly ApolloInstanceManager _core;
    private readonly ApolloCoreLocator _coreLocator;
    private readonly ILibraryAuthorityService _libraryAuthority;
    private readonly HostPreferencesService _preferences;
    private readonly WindowsTrayIconService _trayIcon;
    private readonly AttendedPairingUiCoordinator _pairingUi;
    private readonly AppWindow _appWindow;
    private readonly CancellationTokenSource _windowLifetime = new();
    private readonly CoreReadinessLoop _coreReadiness;
    private string? _pendingPairingNavigationRequestId;
    private bool _isExiting;
    private bool _installerShutdownFrozen;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var services = ((App)Application.Current).Services;
        _core = services.GetRequiredService<ApolloInstanceManager>();
        _coreLocator = services.GetRequiredService<ApolloCoreLocator>();
        _libraryAuthority = services.GetRequiredService<ILibraryAuthorityService>();
        _preferences = services.GetRequiredService<HostPreferencesService>();
        _trayIcon = services.GetRequiredService<WindowsTrayIconService>();
        _pairingUi =
            services.GetRequiredService<AttendedPairingUiCoordinator>();
        var windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _coreReadiness = new CoreReadinessLoop(
            RefreshCoreReadinessAttemptAsync,
            PublishCoreReadinessTimeoutAsync,
            DispatchCoreReadinessAsync);
        _appWindow.Closing += OnWindowClosing;
        _trayIcon.OpenRequested += ShowWindow;
        _trayIcon.ExitRequested += OnExitRequested;
        _trayIcon.CoreStatusChanged += RefreshCoreStatus;
        _core.StatusChanged += OnManagedCoreStatusChanged;
        _pairingUi.Attach(
            this,
            _appWindow,
            RootLayout,
            NavigateToPairing,
            ShowWindow,
            () => ContentFrame.Content is DevicesPage,
            RefreshPairingPageAsync);
        _trayIcon.Initialize(this);
        RefreshCoreStatus();
        ContentFrame.Navigate(typeof(HostSetupPage));
    }

    public void HideToTray() => _appWindow.Hide();

    internal void AllowApplicationExit()
    {
        _isExiting = true;
        FreezeForInstallerShutdown();
    }

    internal void FreezeForInstallerShutdown()
    {
        _installerShutdownFrozen = true;
        _coreReadiness.Cancel();
    }

    internal void StartCoreReadiness() =>
        _ = _coreReadiness.StartAsync(_windowLifetime.Token);

    internal Task<InstallerShutdownOutcome> InvokeInstallerShutdownAsync(
        Func<Task<InstallerShutdownOutcome>> shutdown)
    {
        if (DispatcherQueue.HasThreadAccess) return shutdown();
        var completion = new TaskCompletionSource<InstallerShutdownOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try { completion.TrySetResult(await shutdown()); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }))
            completion.TrySetResult(new InstallerShutdownOutcome(
                false, "dispatcherUnavailable", "notStarted",
                "notObserved", true));
        return completion.Task;
    }

    internal void ExitAfterInstallerShutdown()
    {
        _ = DispatcherQueue.TryEnqueue(
            () => ((App)Application.Current).CompleteInstallerShutdown());
    }

    public void ShowWindow()
    {
        if (_isExiting || _installerShutdownFrozen) return;
        _appWindow.Show();
        Activate();
    }

    public void NavigateToPairing(string? requestId = null)
    {
        ShowWindow();
        _pendingPairingNavigationRequestId = requestId;
        if (ReferenceEquals(
                RootNavigation.SelectedItem,
                DevicesNavigationItem))
        {
            ContentFrame.Navigate(typeof(DevicesPage), requestId);
            _pendingPairingNavigationRequestId = null;
        }
        else
        {
            RootNavigation.SelectedItem = DevicesNavigationItem;
        }
    }

    public async void NavigateToAddApplication()
    {
        if (!await RefreshLibraryAuthorityAsync()) return;
        if (ReferenceEquals(RootNavigation.SelectedItem, AddApplicationNavigationItem))
        {
            ContentFrame.Navigate(typeof(AddApplicationPage));
            return;
        }

        RootNavigation.SelectedItem = AddApplicationNavigationItem;
    }

    public void NavigateBackToLibrary()
    {
        RootNavigation.SelectedItem = LibraryNavigationItem;
    }

    public void NavigateToLibrary()
    {
        if (ReferenceEquals(
                RootNavigation.SelectedItem,
                LibraryNavigationItem))
        {
            ContentFrame.Navigate(typeof(GameLibraryPage));
            return;
        }

        RootNavigation.SelectedItem = LibraryNavigationItem;
    }

    public void NavigateToMonitor() =>
        RootNavigation.SelectedItem = RootNavigation.MenuItems
            .OfType<NavigationViewItem>()
            .First(item => Equals(item.Tag, "monitor"));

    public void NavigateToDevices() =>
        RootNavigation.SelectedItem = DevicesNavigationItem;

    public void NavigateToSettings()
    {
        RootNavigation.SelectedItem = RootNavigation.SettingsItem;
        ContentFrame.Navigate(typeof(SettingsPage));
    }

    private void OnThemeToggle(object sender, RoutedEventArgs args)
    {
        RootLayout.RequestedTheme = RootLayout.ActualTheme == ElementTheme.Dark
            ? ElementTheme.Light
            : ElementTheme.Dark;
        ThemeIcon.Glyph = RootLayout.RequestedTheme == ElementTheme.Dark
            ? "\uE708"
            : "\uE706";
    }

    private async void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        if (tag == "add" && !await RefreshLibraryAuthorityAsync())
        {
            RootNavigation.SelectedItem = LibraryNavigationItem;
            return;
        }
        var page = tag switch
        {
            "library" => typeof(GameLibraryPage),
            "add" => typeof(AddApplicationPage),
            "layouts" => typeof(LayoutCatalogPage),
            "monitor" => typeof(StreamMonitorPage),
            "devices" => typeof(DevicesPage),
            _ => typeof(OverviewPage)
        };
        var parameter = tag switch
        {
            "devices" => _pendingPairingNavigationRequestId,
            "overview" => "概览",
            _ => null
        };
        _pendingPairingNavigationRequestId = null;
        ContentFrame.Navigate(page, parameter);
    }

    private void OnNavigationDisplayModeChanged(
        NavigationView sender,
        NavigationViewDisplayModeChangedEventArgs args) =>
        UpdatePaneResizeThumb();

    private void OnNavigationPaneChanged(NavigationView sender, object args) =>
        UpdatePaneResizeThumb();

    private void OnPaneResizeDragDelta(object sender, DragDeltaEventArgs args)
    {
        if (RootNavigation.DisplayMode != NavigationViewDisplayMode.Expanded ||
            !RootNavigation.IsPaneOpen)
            return;

        RootNavigation.OpenPaneLength = Math.Clamp(
            RootNavigation.OpenPaneLength + args.HorizontalChange,
            MinimumNavigationPaneWidth,
            MaximumNavigationPaneWidth);
        UpdatePaneResizeThumb();
    }

    private void UpdatePaneResizeThumb()
    {
        var canResize =
            RootNavigation.DisplayMode == NavigationViewDisplayMode.Expanded &&
            RootNavigation.IsPaneOpen;
        PaneResizeThumb.Visibility = canResize
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (canResize)
        {
            PaneResizeThumb.Margin = new Thickness(
                RootNavigation.OpenPaneLength - PaneResizeThumb.Width / 2,
                0,
                0,
                0);
        }
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExiting) return;
        args.Cancel = true;
        if (_preferences.Current.CloseToTray)
        {
            HideToTray();
            return;
        }

        OnExitRequested();
    }

    private async void OnExitRequested()
    {
        if (_isExiting) return;
        _isExiting = true;
        _windowLifetime.Cancel();
        _coreReadiness.Cancel();
        await ((App)Application.Current).ExitAsync();
    }

    public void RefreshCoreStatus() => _ = RefreshCoreStatusAsync();

    public async Task RefreshCoreStatusAsync()
    {
        var result = await DispatchCoreReadinessAsync(
            RefreshCoreReadinessAttemptAsync,
            _windowLifetime.Token);
        if (result == CoreReadinessAttempt.Waiting)
            StartCoreReadiness();
    }

    private void OnManagedCoreStatusChanged()
    {
        DispatcherQueue.TryEnqueue(
            _core.IsRunning ? StartCoreReadiness : RefreshCoreStatus);
    }

    private Task RefreshPairingPageAsync() =>
        ContentFrame.Content is DevicesPage devicesPage
            ? devicesPage.ViewModel.RefreshAsync()
            : Task.CompletedTask;

    private async void OnRetryCore(object sender, RoutedEventArgs args)
    {
        CoreRetryButton.IsEnabled = false;
        CoreStatusText.Text = _core.IsRunning
            ? "正在刷新设备接口状态…"
            : "正在启动串流核心…";
        try
        {
            if (_core.IsRunning)
            {
                await RefreshCoreStatusAsync();
                return;
            }

            var existing = await _coreLocator.DiscoverAsync();
            if (existing.Count > 0)
            {
                CoreStatusText.Text =
                    "检测到其他 Ligase 核心。请关闭其他实例，再点击“重新启动”。";
                return;
            }

            await _core.StartAsync();
        }
        finally
        {
            CoreRetryButton.IsEnabled = true;
            StartCoreReadiness();
        }
    }

    private async Task<CoreReadinessAttempt> RefreshCoreReadinessAttemptAsync(
        CancellationToken cancellationToken)
    {
        var authority = await _libraryAuthority.GetStateAsync(cancellationToken);
        CoreReadinessAttempt result;
        if (authority.Kind == LibraryAuthorityKind.ManagedAuthoritative &&
            authority.Core is { } core)
        {
            CoreStatusText.Text = $"运行中 · 独立端口 {core.BasePort}";
            CoreRetryButton.Visibility = Visibility.Collapsed;
            result = CoreReadinessAttempt.Ready;
        }
        else if (_core.IsRunning && authority.Code == "coreStarting")
        {
            CoreStatusText.Text = "核心进程运行中 · 正在等待设备接口";
            CoreRetryButton.Visibility = Visibility.Collapsed;
            result = CoreReadinessAttempt.Waiting;
        }
        else
        {
            CoreStatusText.Text = authority.Message;
            CoreRetryButton.Content = _core.IsRunning ? "刷新状态" : "重新启动";
            CoreRetryButton.Visibility = Visibility.Visible;
            result = CoreReadinessAttempt.Terminal;
        }
        AddApplicationNavigationItem.IsEnabled = authority.CanWrite;
        ToolTipService.SetToolTip(
            AddApplicationNavigationItem,
            authority.CanWrite ? null : authority.Message);
        if (result != CoreReadinessAttempt.Waiting)
            await RefreshVisibleAuthorityPageAsync(cancellationToken);
        return result;
    }

    private async Task<bool> RefreshLibraryAuthorityAsync()
    {
        var result = await DispatchCoreReadinessAsync(
            RefreshCoreReadinessAttemptAsync,
            _windowLifetime.Token);
        if (result == CoreReadinessAttempt.Waiting)
            StartCoreReadiness();
        return result == CoreReadinessAttempt.Ready;
    }

    private async Task<CoreReadinessAttempt> PublishCoreReadinessTimeoutAsync(
        CancellationToken cancellationToken)
    {
        const string message =
            "核心设备接口启动超时，游戏库仍为只读。请点击“刷新状态”重试。";
        CoreStatusText.Text = "设备接口启动超时 · 可刷新状态";
        CoreRetryButton.Content = "刷新状态";
        CoreRetryButton.Visibility = Visibility.Visible;
        AddApplicationNavigationItem.IsEnabled = false;
        ToolTipService.SetToolTip(AddApplicationNavigationItem, message);
        await RefreshVisibleAuthorityPageAsync(cancellationToken);
        return CoreReadinessAttempt.Terminal;
    }

    private async Task RefreshVisibleAuthorityPageAsync(
        CancellationToken cancellationToken)
    {
        if (ContentFrame.Content is GameLibraryPage libraryPage)
            await libraryPage.ViewModel.RefreshAsync(cancellationToken);
        else if (ContentFrame.Content is HostSetupPage setupPage)
            await setupPage.RefreshAsync();
    }

    private Task<CoreReadinessAttempt> DispatchCoreReadinessAsync(
        Func<CancellationToken, Task<CoreReadinessAttempt>> action,
        CancellationToken cancellationToken)
    {
        if (DispatcherQueue.HasThreadAccess) return action(cancellationToken);

        var completion = new TaskCompletionSource<CoreReadinessAttempt>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(
                () => CompleteDispatchedReadinessAsync(
                    action,
                    cancellationToken,
                    completion)))
        {
            completion.TrySetCanceled(cancellationToken);
        }
        return completion.Task.WaitAsync(cancellationToken);
    }

    private static async void CompleteDispatchedReadinessAsync(
        Func<CancellationToken, Task<CoreReadinessAttempt>> action,
        CancellationToken cancellationToken,
        TaskCompletionSource<CoreReadinessAttempt> completion)
    {
        try
        {
            completion.TrySetResult(await action(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

}
