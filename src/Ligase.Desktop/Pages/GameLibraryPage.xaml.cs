using Ligase.Host.Desktop.ViewModels;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
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
            if (item.Kind == LibraryItemKind.VirtualDesktop)
            {
                await ShowVirtualDesktopManagerAsync();
                return;
            }
            await ShowMessageAsync(
                item.Name,
                "这是 Ligase 的系统桌面入口，需要始终保留，不能从游戏库删除。");
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"管理“{item.Name}”",
            Content = item.Kind == LibraryItemKind.Steam
                ? "可以为现有游戏选择与当前 Steam App ID 绑定的本机 Library Capsule，或从游戏库删除项目。"
                : "删除后，这个项目会从 Host 以及所有客户端的游戏库中消失。程序文件本身不会被删除。",
            PrimaryButtonText = item.Kind == LibraryItemKind.Steam ? "选择封面" : "从游戏库删除",
            SecondaryButtonText = item.Kind == LibraryItemKind.Steam ? "从游戏库删除" : null,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        var manageResult = await dialog.ShowAsync();
        if (manageResult == ContentDialogResult.None) return;
        if (item.Kind == LibraryItemKind.Steam &&
            manageResult == ContentDialogResult.Primary)
        {
            await ShowExistingCoverPickerAsync(item);
            return;
        }
        if (item.Kind == LibraryItemKind.Steam &&
            manageResult != ContentDialogResult.Secondary) return;
        if (item.Kind != LibraryItemKind.Steam &&
            manageResult != ContentDialogResult.Primary) return;
        if (!await ViewModel.RemoveAsync(item)) return;

        await ShowMessageAsync(
            "已从游戏库删除",
            $"“{item.Name}”已从 Host 和客户端同步游戏库中移除。");
    }

    private async Task ShowExistingCoverPickerAsync(LibraryItem item)
    {
        var coverViewModel = ((App)Application.Current).Services
            .GetRequiredService<ExistingItemCoverViewModel>();
        try
        {
            var candidates = await coverViewModel.FindVerifiedAsync(item);
            if (candidates.Count == 0)
            {
                await ShowMessageAsync(
                    "没有可验证的本机封面",
                    $"Steam App ID {item.SteamAppId} 当前没有可验证的本机 Library Capsule；游戏库未发生变化。");
                return;
            }

            var candidate = candidates.Single();
            var preview = new Image
            {
                Width = 200,
                Height = 300,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                Source = new BitmapImage(new Uri(candidate.PreviewUrl))
            };
            AutomationProperties.SetName(
                preview,
                $"{item.Name} 的已验证 Steam Library Capsule 预览");
            var content = new StackPanel { Spacing = 12, MaxWidth = 440 };
            content.Children.Add(preview);
            content.Children.Add(new TextBlock
            {
                Text = $"来源：本机 Steam 缓存 · App ID {candidate.SteamAppId}\n确认后将更新同一游戏 UUID，并同步到 Host 与 Android。",
                TextWrapping = TextWrapping.Wrap
            });
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"为“{item.Name}”选择封面",
                Content = content,
                PrimaryButtonText = "使用此封面",
                SecondaryButtonText = "使用默认封面",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };
            var selection = await confirm.ShowAsync();
            if (selection == ContentDialogResult.None) return;

            if (selection == ContentDialogResult.Secondary)
            {
                var reset = await coverViewModel.ResetAsync(
                    item.Id, item.SteamAppId!.Value);
                await ViewModel.RefreshAsync();
                await ShowMessageAsync(
                    reset.Idempotent ? "当前已使用默认封面" : "已恢复默认封面并同步",
                    $"同一游戏 UUID 已完成持久化与核心回读（库修订 {reset.LibraryRevision}）。" +
                    (reset.SupersededCoverCleanupCompleted
                        ? string.Empty
                        : " 旧封面缓存暂未清理，不影响默认封面；Host 会在后续维护中重试。"));
                return;
            }

            var result = await coverViewModel.ApplyAsync(item, candidate);
            await ViewModel.RefreshAsync();
            await ShowMessageAsync(
                result.Idempotent ? "封面已经是最新" : "封面已保存并同步",
                $"同一游戏 UUID 已完成持久化与核心回读（库修订 {result.LibraryRevision}，内容 {result.CoverContentSha256[..12]}…）。" +
                (result.SupersededCoverCleanupCompleted
                    ? string.Empty
                    : " 旧封面缓存暂未清理，不影响当前封面；Host 会在后续维护中重试。"));
        }
        catch (ExistingItemCoverUpdateException exception)
        {
            await ShowMessageAsync("封面未更新", exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowMessageAsync(
                "封面未更新",
                $"封面事务未完成，原游戏库与同步状态已保留。{exception.Message}");
        }
    }

    private async Task ShowVirtualDesktopManagerAsync()
    {
        var control = ((App)Application.Current).Services
            .GetRequiredService<IVirtualDisplayControlService>();
        VirtualDisplayState state;
        try
        {
            state = await control.GetStateAsync();
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("虚拟桌面不可用", exception.Message);
            return;
        }

        var details = new TextBlock
        {
            Text = state.StatusText,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "管理虚拟桌面",
            Content = details,
            PrimaryButtonText = state.IsEnabled ? "停用虚拟桌面" : "创建并启用",
            SecondaryButtonText = "刷新状态",
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return;
        try
        {
            var updated = result == ContentDialogResult.Primary
                ? state.IsEnabled
                    ? await control.DisableAsync()
                    : await control.EnableAsync()
                : await control.GetStateAsync();
            await ShowMessageAsync("虚拟桌面状态", updated.StatusText);
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("虚拟桌面操作失败", exception.Message);
        }
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
