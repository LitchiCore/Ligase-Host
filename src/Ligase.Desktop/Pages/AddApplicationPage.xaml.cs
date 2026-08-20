using Ligase.Host.Core.Models;
using Ligase.Host.Desktop.Controls;
using Ligase.Host.Desktop.Services;
using Ligase.Host.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Ligase.Host.Desktop.Pages;

public sealed partial class AddApplicationPage : Page
{
    private readonly AddApplicationChromeState _chrome = new();
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

    private void OnSteamGameSearchLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not AutoSuggestBox searchBox) return;
        var editor = FindDescendantTextBox(searchBox);
        if (editor is null) return;

        // AutoSuggestBox delegates keyboard focus to its template TextBox.
        // Put the accessible identity on that real focus owner as well as the
        // public control so UI Automation never reports the unrelated cover
        // search field when the Steam editor has focus.
        AutomationProperties.SetName(editor, "搜索 Steam 游戏");
        AutomationProperties.SetHelpText(
            editor,
            "按 Steam 游戏名称、App ID 或安装目录筛选结果");
        AutomationProperties.SetLabeledBy(editor, SteamGameSearchLabel);
        editor.IsTabStop = true;
        editor.TabIndex = 0;
    }

    private static TextBox? FindDescendantTextBox(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBox editor) return editor;
            var nested = FindDescendantTextBox(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void OnBack(object sender, RoutedEventArgs e) =>
        MainWindow.NavigateBackToLibrary();

    private void OnSteamResultsViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer &&
            _chrome.Update(scrollViewer.VerticalOffset))
            ApplyChromeState();
    }

    private void OnExpandChrome(object sender, RoutedEventArgs e)
    {
        _chrome.Expand();
        SteamResultsScrollViewer.ChangeView(null, 0, null, true);
        ApplyChromeState();
    }

    private void OnFocusSteamSearch(object sender, RoutedEventArgs e)
    {
        _ = SteamGameSearchBox.Focus(FocusState.Keyboard);
    }

    private void ApplyChromeState()
    {
        var compact = _chrome.IsCompact;
        HeroHeader.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CoverSearchPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactHeader.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }

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

    private void OnShortcutDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        e.DragUIOverride.Caption = "预览快捷方式（不会自动添加）";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void OnShortcutDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            await ViewModel.PreviewShortcutPathsAsync([]);
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        await ViewModel.PreviewShortcutPathsAsync(
            items.OfType<StorageFile>().Select(file => file.Path).ToArray());
    }

    private async void OnBrowseShortcut(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".lnk");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is not null) await ViewModel.PreviewShortcutPathsAsync([file.Path]);
    }

    private async void OnConfirmShortcut(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.ConfirmShortcutAsync();
        }
        catch (Exception exception)
        {
            ViewModel.Message = exception.Message;
        }
    }

    private void OnCancelShortcut(object sender, RoutedEventArgs e) =>
        ViewModel.CancelShortcutPreview();

    private void OnShortcutFallbackConfirmationChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            ViewModel.SetShortcutFallbackConfirmation(checkBox.IsChecked == true);
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

    private async void OnSaveCoverSelection(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.SaveCoverSelectionAsync();
        }
        catch (Exception exception)
        {
            ViewModel.Message = $"无法保存封面：{exception.Message}";
        }
    }

    private void OnPreviewDefaultCover(object sender, RoutedEventArgs e) =>
        ViewModel.PreviewDefaultCover();

    private void OnCancelCoverSelection(object sender, RoutedEventArgs e) =>
        ViewModel.CancelCoverSelection();

    private async void OnFindSteamCover(object sender, RoutedEventArgs e)
    {
        if (sender is SteamGameResultCard { Result: { } result })
            await ViewModel.FindSteamCoverAsync(result);
    }
}
