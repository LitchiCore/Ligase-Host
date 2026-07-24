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
            var observeOnly = PairingViewModel.Items
                .FirstOrDefault(item => item.RequestId == requestId)?
                .ObserveOnly == true;
            await PairingViewModel.AllowAsync(requestId, observeOnly);
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

    private async void OnManageDevice(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string uuid) return;
        var device = ViewModel.Items.FirstOrDefault(item => item.Uuid == uuid);
        if (device is null) return;
        var operate = new ToggleSwitch
        {
            Header = "允许操作",
            OffContent = "仅观察",
            OnContent = "可操作",
            IsOn = device.IsOperate
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = device.Name,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = "仅观察设备不能控制电脑、启动或结束游戏，也不能修改 Host 设置。",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(operate);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "管理设备",
            Content = content,
            PrimaryButtonText = "保存权限",
            SecondaryButtonText = "删除设备",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                await ViewModel.SetAccessModeAsync(uuid, operate.IsOn);
            }
            catch (Exception exception)
            {
                await ShowMessageAsync("权限修改失败", exception.Message);
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await ConfirmDeleteAsync(device);
        }
    }

    private async Task ConfirmDeleteAsync(DeviceCard device)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"删除“{device.Name}”？",
            Content = "删除后，该设备的证书和权限会立即撤销；若要再次使用，必须重新配对。",
            PrimaryButtonText = "删除设备",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await ViewModel.DeleteAsync(device.Uuid, endActiveSession: false);
        }
        catch (ApolloDeviceActiveException)
        {
            var active = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "设备正在串流",
                Content = "可先结束该设备的活动串流，再撤销证书并删除设备。",
                PrimaryButtonText = "结束串流并删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };
            if (await active.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    await ViewModel.DeleteAsync(
                        device.Uuid,
                        endActiveSession: true);
                }
                catch (Exception exception)
                {
                    await ShowMessageAsync("删除失败", exception.Message);
                }
            }
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("删除失败", exception.Message);
        }
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
