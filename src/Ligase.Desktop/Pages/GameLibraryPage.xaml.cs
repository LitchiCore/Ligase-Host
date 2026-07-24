using Ligase.Host.Desktop.ViewModels;
using Ligase.Host.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Ligase.Host.Desktop.Pages;

public sealed partial class GameLibraryPage : Page
{
    private bool _isReorderMode;
    private Guid[]? _dragStartOrder;

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

    private void OnReorderModeButtonClick(object sender, RoutedEventArgs e)
    {
        _isReorderMode = !_isReorderMode && ViewModel.CanAcceptManualDrop;
        ApplyReorderMode();
    }

    private void OnSortModeChanged(object sender, SelectionChangedEventArgs e)
    {
        _isReorderMode = false;
        ApplyReorderMode();
    }

    private void OnSearchTextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(sender.Text)) return;
        _isReorderMode = false;
        ApplyReorderMode();
    }

    private void ApplyReorderMode()
    {
        var enabled = _isReorderMode && ViewModel.CanAcceptManualDrop;
        if (!enabled) _isReorderMode = false;
        LibraryList.CanDragItems = enabled;
        LibraryList.CanReorderItems = enabled;
        LibraryList.ReorderMode = enabled
            ? ListViewReorderMode.Enabled
            : ListViewReorderMode.Disabled;
        ReorderModeLabel.Text = enabled ? "完成排序" : "开始排序";
        ReorderModeIcon.Glyph = enabled ? "\uE73E" : "\uE70F";
        AutomationProperties.SetName(
            ReorderModeButton,
            enabled ? "完成游戏库排序" : "开始调整游戏库顺序");
        ReorderModeButton.Background = enabled
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                "LigaseAccentSubtleBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                "LigaseSurfaceBrush"];
        ReorderModeButton.BorderBrush = enabled
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                "LigaseAccentBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                "LigaseCardStrokeBrush"];
        SortDescriptionText.Text = enabled
            ? "排序模式已开启：直接拖动整张卡片；其他卡片会自动让位，松手后立即同步。"
            : ViewModel.SortDescription;
    }

    private void OnLibraryDragItemsStarting(
        object sender,
        DragItemsStartingEventArgs args)
    {
        if (!_isReorderMode ||
            !ViewModel.CanAcceptManualDrop ||
            args.Items.OfType<LibraryItem>().Any(item =>
                !ViewModel.CanBeginManualDrag(item)))
        {
            args.Cancel = true;
            _dragStartOrder = null;
            return;
        }

        _dragStartOrder = GetPublishedOrder();
    }

    private void OnLibraryDragItemsCompleted(
        ListViewBase sender,
        DragItemsCompletedEventArgs args)
    {
        var orderBeforeDrag = _dragStartOrder;
        _dragStartOrder = null;
        if (orderBeforeDrag is null) return;

        DispatcherQueue.TryEnqueue(async () =>
        {
            var orderAfterDrag = GetPublishedOrder();
            if (!orderBeforeDrag.SequenceEqual(orderAfterDrag))
                await ViewModel.CommitManualOrderAsync(orderAfterDrag);
        });
    }

    private Guid[] GetPublishedOrder() =>
        ViewModel.FilteredItems
            .Where(item => item.PublishedToClients)
            .Select(item => item.Id)
            .ToArray();

    private async void OnReloadLibrary(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
        ApplyReorderMode();
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
