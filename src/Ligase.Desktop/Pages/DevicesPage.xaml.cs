using Ligase.Host.Desktop.ViewModels;
using Ligase.Host.Desktop.Services;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Ligase.Host.Desktop.Pages;

public sealed partial class DevicesPage : Page
{
    public DevicesViewModel ViewModel { get; }
    public AttendedPairingViewModel PairingViewModel { get; }
    private readonly AttendedPairingCoordinator _pairing;

    public DevicesPage()
    {
        var services = ((App)Application.Current).Services;
        ViewModel = services.GetRequiredService<DevicesViewModel>();
        PairingViewModel = services.GetRequiredService<AttendedPairingViewModel>();
        _pairing = services.GetRequiredService<AttendedPairingCoordinator>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        PairingViewModel.SelectedRequestId = e.Parameter as string;
        await ViewModel.RefreshAsync();
        if (_pairing.Current is not null)
            PairingViewModel.Apply(_pairing.Current);
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.WhenAll(
                ViewModel.RefreshAsync(),
                _pairing.RefreshNowAsync());
        }
        catch (AttendedPairingUnavailableException exception)
        {
            PairingViewModel.SetUnavailable(exception.Message);
        }
        catch
        {
            PairingViewModel.SetUnavailable(
                "待批准设备暂时无法读取。请确认串流核心仍在运行。");
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _pairing.ProjectionChanged += OnProjectionChanged;
        _pairing.AvailabilityChanged += OnAvailabilityChanged;
        if (_pairing.Current is not null)
            PairingViewModel.Apply(_pairing.Current);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _pairing.ProjectionChanged -= OnProjectionChanged;
        _pairing.AvailabilityChanged -= OnAvailabilityChanged;
    }

    private void OnProjectionChanged(AttendedPairingProjection projection) =>
        DispatcherQueue.TryEnqueue(() => PairingViewModel.Apply(projection));

    private void OnAvailabilityChanged(string message) =>
        DispatcherQueue.TryEnqueue(() => PairingViewModel.SetUnavailable(message));

    private async void OnAllow(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string requestId)
        {
            await PairingViewModel.AllowAsync(requestId);
            if (_pairing.Current?.Requests.All(
                    item => item.RequestId != requestId) == true)
                await ViewModel.RefreshAsync();
        }
    }

    private async void OnReject(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string requestId)
            await PairingViewModel.RejectAsync(requestId);
    }
}
