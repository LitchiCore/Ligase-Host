using Ligase.Host.Desktop.Pages;
using Ligase.Host.Desktop.Services;
using Ligase.Host.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace Ligase.Host.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly ApolloInstanceManager _core;
    private readonly ApolloCoreLocator _coreLocator;
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
        _coreLocator = services.GetRequiredService<ApolloCoreLocator>();
        _preferences = services.GetRequiredService<HostPreferencesService>();
        _trayIcon = services.GetRequiredService<WindowsTrayIconService>();
        var windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        PlaceOnSecondaryDisplay();
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

    private void PlaceOnSecondaryDisplay()
    {
        var displays = EnumerateDisplays();
        var target = displays.FirstOrDefault(display => !display.IsPrimary)
            ?? displays.FirstOrDefault(display => display.IsPrimary);

        if (target is null || target.WorkArea.Width <= 0 || target.WorkArea.Height <= 0) return;

        const int horizontalInset = 80;
        const int verticalInset = 56;
        const int preferredWidth = 1560;
        const int preferredHeight = 920;
        var width = Math.Min(preferredWidth, Math.Max(640, target.WorkArea.Width - horizontalInset * 2));
        var height = Math.Min(preferredHeight, Math.Max(480, target.WorkArea.Height - verticalInset * 2));
        _appWindow.MoveAndResize(new RectInt32(
            target.WorkArea.X + horizontalInset,
            target.WorkArea.Y + verticalInset,
            width,
            height));
    }

    private static IReadOnlyList<DisplayWorkArea> EnumerateDisplays()
    {
        var displays = new List<DisplayWorkArea>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                displays.Add(new DisplayWorkArea(
                    new RectInt32(
                        info.WorkArea.Left,
                        info.WorkArea.Top,
                        info.WorkArea.Right - info.WorkArea.Left,
                        info.WorkArea.Bottom - info.WorkArea.Top),
                    (info.Flags & MonitorInfoPrimary) != 0));
            }

            return true;
        }, IntPtr.Zero);
        return displays;
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

    public async void RefreshCoreStatus()
    {
        try
        {
            var endpoint = await _coreLocator.ResolveAsync();
            CoreStatusText.Text = _core.IsRunning
                ? $"运行中 · 独立端口 {endpoint.BasePort}"
                : $"已连接 {endpoint.HostName} · 端口 {endpoint.BasePort}";
        }
        catch (ApolloCoreUnavailableException)
        {
            CoreStatusText.Text = _core.StartupError ?? "核心未运行 · 可在概览页启动";
        }
    }

    private const uint MonitorInfoPrimary = 1;

    private sealed record DisplayWorkArea(RectInt32 WorkArea, bool IsPrimary);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    private delegate bool MonitorEnumProcedure(
        IntPtr monitor,
        IntPtr deviceContext,
        IntPtr monitorRect,
        IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRect,
        MonitorEnumProcedure callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
}
