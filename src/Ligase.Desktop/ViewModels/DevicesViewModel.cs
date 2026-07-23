using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.UI.Xaml;

namespace Ligase.Host.Desktop.ViewModels;

public sealed class DeviceCard(ApolloDevice device)
{
    public string Name { get; } = string.IsNullOrWhiteSpace(device.Name)
        ? "未命名设备"
        : device.Name;
    public string Uuid { get; } = device.Uuid;
    public string Status { get; } = device.Connected ? "在线" : "已配对";
    public string StatusGlyph { get; } = device.Connected ? "\uE73E" : "\uE8FB";
    public string DisplayMode { get; } = device.AlwaysUseVirtualDisplay
        ? "始终使用虚拟显示器"
        : string.IsNullOrWhiteSpace(device.DisplayMode)
            ? "跟随客户端显示设置"
            : $"固定模式 · {device.DisplayMode}";
    public string Permission { get; } =
        $"权限 0x{device.Permissions:X8}" +
        (device.AllowClientCommands ? " · 允许客户端命令" : string.Empty);
}

public partial class DevicesViewModel(ApolloDeviceService devices) : ObservableObject
{
    public ObservableCollection<DeviceCard> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(ContentVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorVisibility))]
    private string? _errorMessage;

    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ContentVisibility => !IsLoading && Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => !IsLoading && Items.Count == 0 && ErrorMessage is null
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility ErrorVisibility => ErrorMessage is null ? Visibility.Collapsed : Visibility.Visible;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var snapshot = await devices.GetDevicesAsync(cancellationToken);
            Items.Clear();
            foreach (var device in snapshot.OrderByDescending(item => item.Connected)
                         .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Items.Add(new DeviceCard(device));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ContentVisibility));
            OnPropertyChanged(nameof(EmptyVisibility));
        }
    }
}
