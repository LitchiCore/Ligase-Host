using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Ligase.Host.Desktop.Services;
using Microsoft.UI.Xaml;

namespace Ligase.Host.Desktop.ViewModels;

public sealed class PendingPairingCard(
    PendingPairingRequest request,
    bool selected = false)
{
    public string RequestId { get; } = request.RequestId;
    public string DeviceName { get; } = request.Device.Name;
    public string Platform { get; } = "Android";
    public string SafetyCode { get; } = request.SafetyCode;
    public bool ReadyForApproval { get; } = request.ReadyForApproval;
    public bool AllowEnabled => ReadyForApproval;
    public bool ObserveOnly { get; set; }
    public Visibility SelectionVisibility =>
        selected ? Visibility.Visible : Visibility.Collapsed;
    public string StateText => ReadyForApproval
        ? "安全连接已建立，请核对安全码"
        : "正在建立安全连接";
    public string SourceText { get; } = DescribeSource(request.SourceAddress);
    public string RemainingText
    {
        get
        {
            var seconds = Math.Max(
                0,
                (int)Math.Ceiling(
                    (request.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds));
            return $"剩余 {seconds} 秒";
        }
    }

    internal static string DescribeSource(PairingSourceAddress source)
    {
        if (!IPAddress.TryParse(source.Address, out var address))
            return "其他网络来源";
        if (IPAddress.IsLoopback(address)) return "本机";
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            var privateAddress =
                bytes[0] == 10 ||
                bytes[0] == 127 ||
                (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168);
            return privateAddress ? "局域网 IPv4" : "其他 IPv4 网络";
        }
        if (address.IsIPv6LinkLocal)
            return source.ScopeId is > 0
                ? "局域网 IPv6（链路本地）"
                : "IPv6 链路本地";
        return "局域网 IPv6";
    }
}

public partial class AttendedPairingViewModel(
    AttendedPairingCoordinator coordinator,
    PairingNotificationService notifications) : ObservableObject
{
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);

    public ObservableCollection<PendingPairingCard> Items { get; } = [];
    public string? SelectedRequestId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorVisibility))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FeedbackVisibility))]
    private string? _feedbackMessage;

    public Visibility PendingVisibility =>
        Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility =>
        ErrorMessage is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FeedbackVisibility =>
        FeedbackMessage is null ? Visibility.Collapsed : Visibility.Visible;
    public string? NotificationWarning => notifications.UnavailableReason;
    public Visibility NotificationWarningVisibility =>
        NotificationWarning is null ? Visibility.Collapsed : Visibility.Visible;

    public void RefreshNotificationStatus()
    {
        OnPropertyChanged(nameof(NotificationWarning));
        OnPropertyChanged(nameof(NotificationWarningVisibility));
    }

    public void Apply(AttendedPairingProjection projection)
    {
        var accessSelections = Items.ToDictionary(
            item => item.RequestId,
            item => item.ObserveOnly,
            StringComparer.Ordinal);
        Items.Clear();
        foreach (var item in projection.Requests)
        {
            var card = new PendingPairingCard(
                item,
                item.RequestId == SelectedRequestId);
            if (accessSelections.TryGetValue(item.RequestId, out var observeOnly))
                card.ObserveOnly = observeOnly;
            Items.Add(card);
        }
        ErrorMessage = null;
        OnPropertyChanged(nameof(PendingVisibility));
    }

    public void SetUnavailable(string message)
    {
        ErrorMessage = string.IsNullOrWhiteSpace(message) ? null : message;
        if (ErrorMessage is not null) Items.Clear();
        OnPropertyChanged(nameof(PendingVisibility));
    }

    public async Task AllowAsync(string requestId, bool observeOnly)
    {
        if (!_busy.Add(requestId)) return;
        try
        {
            FeedbackMessage = "正在允许设备…";
            var status = await coordinator.AllowAsync(requestId, observeOnly);
            FeedbackMessage = status.State == "approved"
                ? "已允许，正在完成证书配对…"
                : "设备已经完成配对。";
        }
        catch (AttendedPairingUnavailableException exception)
        {
            FeedbackMessage = exception.Message;
        }
        catch
        {
            FeedbackMessage =
                "允许设备失败。请确认串流核心仍在运行，然后让客户端重新发起。";
        }
        finally
        {
            _busy.Remove(requestId);
        }
    }

    public async Task RejectAsync(string requestId)
    {
        if (!_busy.Add(requestId)) return;
        try
        {
            FeedbackMessage = "正在拒绝设备…";
            await coordinator.RejectAsync(requestId);
            FeedbackMessage = "已拒绝该设备。";
        }
        catch (AttendedPairingUnavailableException exception)
        {
            FeedbackMessage = exception.Message;
        }
        catch
        {
            FeedbackMessage =
                "拒绝设备失败。请确认串流核心仍在运行，然后让客户端重新发起。";
        }
        finally
        {
            _busy.Remove(requestId);
        }
    }
}
