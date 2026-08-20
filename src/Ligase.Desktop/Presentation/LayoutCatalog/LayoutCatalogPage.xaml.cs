using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Ligase.Host.Desktop.Presentation.LayoutCatalog;

public sealed partial class LayoutCatalogPage : Page
{
    public LayoutCatalogViewModel ViewModel { get; }

    public LayoutCatalogPage()
    {
        ViewModel = ((App)Application.Current)
            .Services.GetRequiredService<LayoutCatalogViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadAsync();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) =>
        await LoadAsync();

    private async Task LoadAsync()
    {
        DescriptorList.IsEnabled = false;
        try
        {
            await ViewModel.LoadAsync();
            DescriptorList.SelectedItem = ViewModel.SelectedItem;
            BindingAlertBar.Message = string.Join(
                Environment.NewLine,
                ViewModel.BindingAlerts.Select(alert => alert.Summary));
            BindingAlertBar.IsOpen = ViewModel.BindingAlerts.Count > 0;
            UpdateState();
            ShowDetails(ViewModel.SelectedItem);
        }
        finally
        {
            DescriptorList.IsEnabled = true;
        }
    }

    private void OnDescriptorSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        ViewModel.SelectedItem =
            DescriptorList.SelectedItem as LayoutCatalogDescriptorCard;
        ShowDetails(ViewModel.SelectedItem);
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateState();

    private async void OnManageBinding(object sender, RoutedEventArgs e)
    {
        var descriptor = ViewModel.SelectedItem;
        if (descriptor is null) return;
        if (!ViewModel.CanEditBindings)
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "当前不能修改布局绑定",
                Content = ViewModel.BindingAuthorityMessage ??
                          "没有可绑定的游戏，或当前 Host 核心不是唯一权威实例。",
                CloseButtonText = "知道了"
            }.ShowAsync();
            return;
        }

        var selector = new ComboBox
        {
            Header = "目标游戏",
            ItemsSource = ViewModel.BindableGames,
            DisplayMemberPath = nameof(LayoutBindingGameOption.Label),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = $"布局 {descriptor.LayoutId} · 修订 {descriptor.Revision}",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(selector);
        content.Children.Add(new TextBlock
        {
            Text = "绑定按 canonical 游戏 UUID 保存。清除只移除此游戏的显式绑定；不会删除游戏或布局描述。",
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "管理游戏布局绑定",
            Content = content,
            PrimaryButtonText = "保存此绑定",
            SecondaryButtonText = "清除所选游戏绑定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None ||
            selector.SelectedItem is not LayoutBindingGameOption game)
            return;

        try
        {
            await ViewModel.SetBindingAsync(
                descriptor,
                game,
                clear: result == ContentDialogResult.Secondary);
            DescriptorList.SelectedItem = ViewModel.Items.FirstOrDefault(item =>
                item.LayoutId == descriptor.LayoutId &&
                item.Revision == descriptor.Revision);
            ShowDetails(DescriptorList.SelectedItem as LayoutCatalogDescriptorCard);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "布局绑定未保存",
                Content = exception.Message,
                CloseButtonText = "知道了"
            }.ShowAsync();
        }
    }

    private void UpdateState()
    {
        DescriptorList.Visibility = ViewModel.HasItems
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyState.Visibility = ViewModel.IsEmpty
            ? Visibility.Visible
            : Visibility.Collapsed;
        ErrorMessageText.Text = ViewModel.ErrorMessage;
        ErrorState.Visibility = ViewModel.HasError
            ? Visibility.Visible
            : Visibility.Collapsed;
        var showDetails = ViewModel.HasItems && ActualWidth >= 900;
        DetailPanel.Visibility = showDetails
            ? Visibility.Visible
            : Visibility.Collapsed;
        DetailColumn.Width = new GridLength(showDetails ? 360 : 0);
    }

    private void ShowDetails(LayoutCatalogDescriptorCard? item)
    {
        DetailLayoutId.Text = item?.LayoutId ?? "—";
        DetailRevisionStatus.Text = item is null
            ? "—"
            : $"{item.RevisionLabel} · {item.PublicationStatusLabel}";
        DetailCompatibility.Text = item?.CompatibilitySummary ?? "—";
        DetailVariants.Text = item?.VariantDetails ?? "—";
        DetailIdentities.Text = item?.PortableIdentitySummary ?? "—";
        DetailBindings.Text = item?.BindingSummary ?? "—";
        BindSelectedLayoutButton.IsEnabled =
            item is not null &&
            item.PublicationStatus != "retired" &&
            ViewModel.CanEditBindings;
    }
}
