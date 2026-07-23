using Ligase.Host.Desktop.Pages;
using Ligase.Host.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ligase.Host.Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var core = ((App)Application.Current).Services.GetRequiredService<ApolloInstanceManager>();
        CoreStatusText.Text = core.IsRunning
            ? $"运行中 · 独立端口 {core.BasePort}"
            : core.StartupError ?? $"核心待部署 · 独立端口 {core.BasePort}";
        Closed += async (_, _) => await core.StopAsync();
        ContentFrame.Navigate(typeof(GameLibraryPage));
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
            ContentFrame.Navigate(typeof(PlaceholderPage), "设置");
            return;
        }

        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        ContentFrame.Navigate(tag switch
        {
            "library" => typeof(GameLibraryPage),
            "add" => typeof(AddApplicationPage),
            "monitor" => typeof(StreamMonitorPage),
            _ => typeof(PlaceholderPage)
        }, tag switch
        {
            "overview" => "概览",
            "devices" => "设备",
            _ => null
        });
    }
}
