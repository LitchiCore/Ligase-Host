using Ligase.Host.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Ligase.Host.Desktop.Pages;

public sealed partial class OverviewPage : Page
{
    public OverviewViewModel ViewModel { get; }

    public OverviewPage()
    {
        ViewModel = ((App)Application.Current)
            .Services.GetRequiredService<OverviewViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            await ViewModel.RefreshAsync();
            WelcomeTitleText.Text = ViewModel.WelcomeTitle;
            CoreSummaryText.Text = ViewModel.CoreSummary;
            CoreValueText.Text = ViewModel.CoreValue;
            LibraryValueText.Text = ViewModel.LibraryValue;
            DeviceValueText.Text = ViewModel.DeviceValue;
            VirtualDisplayValueText.Text = ViewModel.VirtualDisplayValue;
            RecentSummaryText.Text = ViewModel.RecentSummary;
            StatusMessageText.Text = ViewModel.StatusMessage;
            StatusMessageText.Visibility = ViewModel.HasStatusMessage
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            // Navigation cancellation is expected when the window is closing.
        }
    }

    private MainWindow Shell => ((App)Application.Current)
        .Services.GetRequiredService<MainWindow>();

    private void OnOpenLibrary(object sender, RoutedEventArgs e) =>
        Shell.NavigateToLibrary();

    private void OnAddGame(object sender, RoutedEventArgs e) =>
        Shell.NavigateToAddApplication();

    private void OnOpenMonitor(object sender, RoutedEventArgs e) =>
        Shell.NavigateToMonitor();

    private void OnOpenDevices(object sender, RoutedEventArgs e) =>
        Shell.NavigateToDevices();
}
