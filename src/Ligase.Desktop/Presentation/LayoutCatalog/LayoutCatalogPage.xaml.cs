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
    }
}
