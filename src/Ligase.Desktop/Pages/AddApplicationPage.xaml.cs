using Ligase.Host.Core.Models;
using Ligase.Host.Desktop.Controls;
using Ligase.Host.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Ligase.Host.Desktop.Pages;

public sealed partial class AddApplicationPage : Page
{
    public AddApplicationViewModel ViewModel { get; }

    public AddApplicationPage()
    {
        ViewModel = ((App)Application.Current).Services
            .GetRequiredService<AddApplicationViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAuthorityAsync();
        await ViewModel.ScanSteamAsync();
    }

    private MainWindow MainWindow => ((App)Application.Current).Services
        .GetRequiredService<MainWindow>();

    private void OnBack(object sender, RoutedEventArgs e) =>
        MainWindow.NavigateBackToLibrary();

    private async void OnScanSteam(object sender, RoutedEventArgs e) =>
        await ViewModel.ScanSteamAsync();

    private async void OnAddSteam(object sender, RoutedEventArgs e)
    {
        if (sender is SteamGameResultCard { Result: { } result })
            await ViewModel.AddSteamAsync(result);
    }

    private async void OnRemoveSteam(object sender, RoutedEventArgs e)
    {
        if (sender is not SteamGameResultCard { Result: { } result }) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = $"移除“{result.Name}”？",
            Content = "删除后，这个项目会从 Host 以及所有客户端的游戏库中消失。Steam 游戏程序和存档文件不会被删除。",
            PrimaryButtonText = "从游戏库移除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            PrimaryButtonStyle = (Style)Application.Current.Resources[
                "LigaseDangerButtonStyle"]
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RemoveSteamAsync(result);
    }

    private async void OnBrowseExecutable(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is not null) ViewModel.SetExecutablePath(file.Path);
    }

    private async void OnAddExecutable(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.AddExecutableAsync();
        }
        catch (Exception exception)
        {
            ViewModel.Message = exception.Message;
        }
    }

    private async void OnSearchCovers(object sender, RoutedEventArgs e) =>
        await ViewModel.SearchCoversAsync();

    private async void OnSelectCover(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CoverCandidate candidate) return;
        try
        {
            await ViewModel.SelectCoverAsync(candidate);
        }
        catch (Exception exception)
        {
            ViewModel.Message = $"无法保存所选封面：{exception.Message}";
        }
    }

    private async void OnFindSteamCover(object sender, RoutedEventArgs e)
    {
        if (sender is SteamGameResultCard { Result: { } result })
            await ViewModel.SearchCoversAsync(result.Name);
    }
}
