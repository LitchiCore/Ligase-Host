using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Ligase.Host.Desktop.Presentation.Onboarding;

public sealed partial class HostSetupPage : Page
{
    private bool _initialRead = true;

    public HostSetupViewModel ViewModel { get; }

    public HostSetupPage()
    {
        ViewModel = ((App)Application.Current).Services
            .GetRequiredService<HostSetupViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
        if (_initialRead && !ViewModel.RequiresSetup)
            ((App)Application.Current).Services
                .GetRequiredService<MainWindow>()
                .NavigateToLibrary();
        _initialRead = false;
    }

    internal Task RefreshAsync() => ViewModel.LoadAsync();

    private async void OnRefresh(object sender, RoutedEventArgs e) =>
        await ViewModel.LoadAsync();

    private async void OnOpenInstaller(object sender, RoutedEventArgs e) =>
        await ViewModel.OpenInstallerAsync();

    private void OnOpenFirewallSettings(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).Services
            .GetRequiredService<MainWindow>()
            .NavigateToSettings();

    private void OnFinish(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).Services
            .GetRequiredService<MainWindow>()
            .NavigateToLibrary();
}
