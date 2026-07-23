using Ligase.Host.Desktop.Pages;
using Ligase.Host.Desktop.Services;
using Ligase.Host.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace Ligase.Host.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly ApolloInstanceManager _core;
    private readonly HostPreferencesService _preferences;
    private readonly WindowsTrayIconService _trayIcon;
    private readonly AppWindow _appWindow;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var services = ((App)Application.Current).Services;
        _core = services.GetRequiredService<ApolloInstanceManager>();
        _preferences = services.GetRequiredService<HostPreferencesService>();
        _trayIcon = services.GetRequiredService<WindowsTrayIconService>();
        var windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Closing += OnWindowClosing;
        _trayIcon.OpenRequested += ShowWindow;
        _trayIcon.ExitRequested += OnExitRequested;
        _trayIcon.CoreStatusChanged += RefreshCoreStatus;
        _trayIcon.Initialize(this);
        RefreshCoreStatus();
        ContentFrame.Navigate(typeof(GameLibraryPage));
    }

    public void HideToTray() => _appWindow.Hide();

    public void ShowWindow()
    {
        _appWindow.Show();
        Activate();
    }

    public void NavigateToAddApplication()
    {
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

    private void OnThemeToggle(object sender, RoutedEventArgs args)
    {
        RootLayout.RequestedTheme = RootLayout.ActualTheme == ElementTheme.Dark
            ? ElementTheme.Light
            : ElementTheme.Dark;
        ThemeIcon.Glyph = RootLayout.RequestedTheme == ElementTheme.Dark
            ? "\uE708"
            : "\uE706";
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        ContentFrame.Navigate(tag switch
        {
            "library" => typeof(GameLibraryPage),
            "add" => typeof(AddApplicationPage),
            "monitor" => typeof(StreamMonitorPage),
            "devices" => typeof(DevicesPage),
            _ => typeof(PlaceholderPage)
        }, tag switch
        {
            "overview" => "概览",
            _ => null
        });
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

    private void RefreshCoreStatus()
    {
        CoreStatusText.Text = _core.IsRunning
            ? $"运行中 · 独立端口 {_core.BasePort}"
            : _core.StartupError ?? $"核心已停止 · 独立端口 {_core.BasePort}";
    }
}
