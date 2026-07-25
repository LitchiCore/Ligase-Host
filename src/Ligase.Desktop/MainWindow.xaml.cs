using Ligase.Host.Desktop.Pages;
using Ligase.Host.Desktop.Presentation.LayoutCatalog;
using Ligase.Host.Desktop.Presentation.Onboarding;
using Ligase.Host.Desktop.Services;
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
    private string? _pendingPairingNavigationRequestId;
    private bool _isExiting;

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

    internal void AllowApplicationExit() => _isExiting = true;

    public void ShowWindow()
    {
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
        await ((App)Application.Current).ExitAsync();
    }

    public void RefreshCoreStatus() => _ = RefreshCoreStatusAsync();

    public async Task RefreshCoreStatusAsync()
    {
        try
        {
            var endpoint = await _coreLocator.ResolveAsync();
            CoreStatusText.Text = _core.IsRunning
                ? $"运行中 · 独立端口 {endpoint.BasePort}"
                : $"已连接 {endpoint.HostName} · 端口 {endpoint.BasePort}";
            CoreRetryButton.Visibility = _core.IsRunning
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        catch (ApolloCoreUnavailableException)
        {
            CoreStatusText.Text = _core.StartupError ?? "核心未运行 · 可在概览页启动";
            CoreRetryButton.Visibility = Visibility.Visible;
        }
        await RefreshLibraryAuthorityAsync();
        if (ContentFrame.Content is GameLibraryPage libraryPage)
            await libraryPage.ViewModel.RefreshAsync();
        else if (ContentFrame.Content is HostSetupPage setupPage)
            await setupPage.RefreshAsync();
    }

    private void OnManagedCoreStatusChanged()
    {
        DispatcherQueue.TryEnqueue(RefreshCoreStatus);
    }

    private Task RefreshPairingPageAsync() =>
        ContentFrame.Content is DevicesPage devicesPage
            ? devicesPage.ViewModel.RefreshAsync()
            : Task.CompletedTask;

    private async void OnRetryCore(object sender, RoutedEventArgs args)
    {
        CoreRetryButton.IsEnabled = false;
        CoreStatusText.Text = "正在启动串流核心…";
        try
        {
            var existing = await _coreLocator.DiscoverAsync();
            if (existing.Count > 0)
            {
                CoreStatusText.Text =
                    "检测到其他 Ligase 核心。请关闭其他实例，再点击“重新启动”。";
                return;
            }

            await _core.StartAsync();
            await Task.Delay(1500);
        }
        finally
        {
            CoreRetryButton.IsEnabled = true;
            await RefreshCoreStatusAsync();
        }
    }

    private async Task<bool> RefreshLibraryAuthorityAsync()
    {
        var authority = await _libraryAuthority.GetStateAsync();
        AddApplicationNavigationItem.IsEnabled = authority.CanWrite;
        ToolTipService.SetToolTip(
            AddApplicationNavigationItem,
            authority.CanWrite ? null : authority.Message);
        return authority.CanWrite;
    }

}
