using Ligase.Host.Desktop.ViewModels;
using Ligase.Host.Core.Models;
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

    private async void OnAddApplication(object sender, RoutedEventArgs e)
    {
        if (!await ViewModel.EnsureWritableAsync()) return;
        ((App)Application.Current).Services.GetRequiredService<MainWindow>()
            .NavigateToAddApplication();
    }

    private async void OnManageApplication(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LibraryItem item }) return;

        if (item.IsSystemEntry)
        {
            await ShowMessageAsync(
                item.Name,
                "这是 Ligase 的系统桌面入口，需要始终保留，不能从游戏库删除。");
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"管理“{item.Name}”",
            Content = "删除后，这个项目会从 Host 以及所有客户端的游戏库中消失。程序文件本身不会被删除。",
            PrimaryButtonText = "从游戏库删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (!await ViewModel.RemoveAsync(item)) return;

        await ShowMessageAsync(
            "已从游戏库删除",
            $"“{item.Name}”已从 Host 和客户端同步游戏库中移除。");
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "知道了"
        };
        await dialog.ShowAsync();
    }
}
