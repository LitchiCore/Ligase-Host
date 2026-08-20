using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Ligase.Host.Desktop.Services;
using Microsoft.UI.Xaml;

namespace Ligase.Host.Desktop.ViewModels;

public enum DevicePresenceState { Unknown, Offline, Online }
public enum DeviceSessionState { None, Streaming, Observing }

public sealed class DeviceCard : ObservableObject
{
    private string _name = string.Empty;
    private string _status = string.Empty;
    private string _sessionStatus = string.Empty;
    private string _statusGlyph = string.Empty;
    private string _displayMode = string.Empty;
    private string _permission = string.Empty;
    private string _accessibleName = string.Empty;
    private string _accessibleHelpText = string.Empty;
    private bool _isOperate;
    private DevicePresenceState _presence;
    private DeviceSessionState _session;

    public DeviceCard(ApolloDevice device) => Update(device);

    public string Name { get => _name; private set => SetProperty(ref _name, value); }
    public string Uuid { get; private set; } = string.Empty;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string SessionStatus { get => _sessionStatus; private set => SetProperty(ref _sessionStatus, value); }
    public string StatusGlyph { get => _statusGlyph; private set => SetProperty(ref _statusGlyph, value); }
    public string DisplayMode { get => _displayMode; private set => SetProperty(ref _displayMode, value); }
    public string Permission { get => _permission; private set => SetProperty(ref _permission, value); }
    public bool IsOperate { get => _isOperate; private set => SetProperty(ref _isOperate, value); }
    public DevicePresenceState Presence { get => _presence; private set => SetProperty(ref _presence, value); }
    public DeviceSessionState Session { get => _session; private set => SetProperty(ref _session, value); }
    public string AccessibleName { get => _accessibleName; private set => SetProperty(ref _accessibleName, value); }
    public string AccessibleHelpText { get => _accessibleHelpText; private set => SetProperty(ref _accessibleHelpText, value); }
    public Visibility OnlineVisibility => Presence == DevicePresenceState.Online
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NotOnlineVisibility => Presence == DevicePresenceState.Online
        ? Visibility.Collapsed : Visibility.Visible;

    public void Update(ApolloDevice device)
    {
        Name = string.IsNullOrWhiteSpace(device.Name) ? "未命名设备" : device.Name;
        Uuid = device.Uuid;
        Presence = device.PresenceState switch
        {
            "online" => DevicePresenceState.Online,
            "offline" => DevicePresenceState.Offline,
            _ => DevicePresenceState.Unknown
        };
        Session = device.SessionState switch
        {
            "streaming" => DeviceSessionState.Streaming,
            "observing" => DeviceSessionState.Observing,
            _ => DeviceSessionState.None
        };
        SessionStatus = Session switch
        {
            DeviceSessionState.Streaming => "正在串流",
            DeviceSessionState.Observing => "正在观察",
            _ => "无活动会话"
        };
        Status = Presence switch
        {
            DevicePresenceState.Online => $"在线 · {SessionStatus}",
            DevicePresenceState.Offline => "离线",
            _ => "状态未知"
        };
        StatusGlyph = Presence switch
        {
            DevicePresenceState.Online => "\uE73E",
            DevicePresenceState.Offline => "\uE8FB",
            _ => "\uE9CE"
        };
        DisplayMode = device.AlwaysUseVirtualDisplay
            ? "始终使用虚拟显示器"
            : string.IsNullOrWhiteSpace(device.DisplayMode)
                ? "跟随客户端显示设置"
                : $"固定模式 · {device.DisplayMode}";
        IsOperate = device.AccessMode == "operate";
        Permission = IsOperate
            ? "可操作 · 可启动、控制和结束串流"
            : "仅观察 · 不允许控制或修改 Host";
        AccessibleName = $"{Name}，{Status}";
        AccessibleHelpText = $"当前状态：{Status}。{Permission}。{DisplayMode}。";
        OnPropertyChanged(nameof(OnlineVisibility));
        OnPropertyChanged(nameof(NotOnlineVisibility));
    }
}

public partial class DevicesViewModel(
    IApolloDeviceService devices,
    DevicePresenceCoordinator presence) : ObservableObject
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
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility => ErrorMessage is null ? Visibility.Collapsed : Visibility.Visible;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            await presence.RefreshNowAsync(cancellationToken);
            if (presence.Current is not null) Apply(presence.Current);
        }
        finally
        {
            IsLoading = false;
            RaiseCollectionVisibility();
        }
    }

    public void Apply(DevicePresenceProjection projection)
    {
        ErrorMessage = projection.IsAuthoritative ? null : projection.Message;
        var incoming = projection.Devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Uuid))
            .GroupBy(device => device.Uuid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(device => device.Uuid, StringComparer.OrdinalIgnoreCase);

        foreach (var existing in Items.ToArray())
        {
            if (incoming.Remove(existing.Uuid, out var update)) existing.Update(update);
            else if (projection.IsAuthoritative) Items.Remove(existing);
        }
        foreach (var device in incoming.Values) Items.Add(new DeviceCard(device));

        var ordered = Items
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Uuid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var target = 0; target < ordered.Length; target++)
        {
            var current = Items.IndexOf(ordered[target]);
            if (current != target) Items.Move(current, target);
        }
        RaiseCollectionVisibility();
    }

    private void RaiseCollectionVisibility()
    {
        OnPropertyChanged(nameof(ContentVisibility));
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    public async Task SetAccessModeAsync(string uuid, bool operate, CancellationToken cancellationToken = default)
    {
        await devices.SetAccessModeAsync(uuid, operate ? "operate" : "observe", cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async Task DeleteAsync(string uuid, bool endActiveSession, CancellationToken cancellationToken = default)
    {
        await devices.DeleteAsync(uuid, endActiveSession, cancellationToken);
        await RefreshAsync(cancellationToken);
    }
}
