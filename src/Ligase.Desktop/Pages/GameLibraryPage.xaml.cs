using Ligase.Host.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Ligase.Host.Desktop.Pages;

public sealed partial class GameLibraryPage : Page
{
    public GameLibraryViewModel ViewModel { get; }

    public GameLibraryPage()
    {
        ViewModel = ((App)Microsoft.UI.Xaml.Application.Current)
            .Services.GetRequiredService<GameLibraryViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAsync();
    }

    private void OnAddApplication(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).Services.GetRequiredService<MainWindow>()
            .NavigateToAddApplication();
}
