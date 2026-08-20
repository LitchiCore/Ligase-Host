using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;

namespace Ligase.Host.Desktop.ViewModels;

public sealed record OverviewMetric(
    string Glyph,
    string Label,
    string Value,
    string Description);

public sealed record OverviewRecentItem(
    string Glyph,
    string Name,
    string Description);

public partial class OverviewViewModel(
    ApolloInstanceManager core,
    IApplicationLibrary library,
    ApolloDeviceService devices,
    IVirtualDisplayControlService virtualDisplay) : ObservableObject
{
    public ObservableCollection<OverviewMetric> Metrics { get; } = [];
    public ObservableCollection<OverviewRecentItem> RecentItems { get; } = [];

    [ObservableProperty]
    private string _welcomeTitle = "欢迎回来";

    [ObservableProperty]
    private string _coreSummary = "正在读取 Host 状态";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    [ObservableProperty]
    private string _coreValue = "读取中";

    [ObservableProperty]
    private string _libraryValue = "—";

    [ObservableProperty]
    private string _deviceValue = "—";

    [ObservableProperty]
    private string _virtualDisplayValue = "读取中";

    [ObservableProperty]
    private string _recentSummary = "正在读取最近项目";

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        StatusMessage = null;
        LibraryState? state = null;
        try
        {
            state = await library.LoadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppendStatusMessage(
                $"暂时无法读取游戏库：{exception.Message}。请稍后刷新，现有游戏不会受到影响。");
        }

        var pairedCount = "—";
        var deviceDescription = "设备状态暂时不可用";
        try
        {
            var snapshot = await devices.GetDevicesAsync(cancellationToken);
            pairedCount = snapshot.Count.ToString();
            var online = snapshot.Count(device => device.PresenceState == "online");
            var unknown = snapshot.Count(device => device.PresenceState == "unknown");
            deviceDescription = online > 0
                ? $"{online} 台在线，可立即连接"
                : unknown > 0
                    ? $"{unknown} 台设备状态未知，请稍后刷新"
                : snapshot.Count > 0
                    ? "均已配对，当前没有在线设备"
                    : "还没有配对设备";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppendStatusMessage(
                $"暂时无法读取设备状态：{exception.Message}。核心恢复后会自动显示最新状态。");
        }

        try
        {
            var display = await virtualDisplay.GetStateAsync(cancellationToken);
            VirtualDisplayValue = display.IsEnabled ? "已启用" :
                display.DriverReady ? "可用" : "不可用";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            VirtualDisplayValue = "不可用";
            AppendStatusMessage($"暂时无法读取虚拟桌面状态：{exception.Message}");
        }

        CoreSummary = core.IsRunning
            ? $"Ligase 核心运行中 · 独立端口 {core.BasePort}"
            : core.StartupError ?? "Ligase 核心未运行";
        WelcomeTitle = DateTime.Now.Hour switch
        {
            < 6 => "夜深了，Host 仍在待命",
            < 12 => "早上好",
            < 18 => "下午好",
            _ => "晚上好"
        };

        Metrics.Clear();
        CoreValue = core.IsRunning ? "运行中" : "未运行";
        Metrics.Add(new OverviewMetric(
            "\uE9D9",
            "串流核心",
            CoreValue,
            core.IsRunning ? $"端口 {core.BasePort} · 等待设备连接" : "前往串流监控查看详情"));
        LibraryValue = state?.Items.Count.ToString() ?? "—";
        Metrics.Add(new OverviewMetric(
            "\uE7FC",
            "游戏库",
            LibraryValue,
            state is null
                ? "游戏库状态暂时不可用"
                : $"{state.Items.Count(item => item.PublishedToClients)} 个项目已发布到客户端"));
        DeviceValue = pairedCount;
        Metrics.Add(new OverviewMetric(
            "\uE772",
            "配对设备",
            DeviceValue,
            deviceDescription));

        RecentItems.Clear();
        if (state is null)
        {
            RecentSummary = "游戏库恢复后，这里会显示最近使用的项目。";
            return;
        }

        foreach (var item in state.Items
                     .Where(item => !item.IsSystemEntry)
                     .OrderByDescending(item => item.LastPlayedAt ?? item.AddedAt)
                     .Take(4))
        {
            RecentItems.Add(new OverviewRecentItem(
                item.IconGlyph,
                item.Name,
                item.LastPlayedAt.HasValue
                    ? $"最近启动 · {item.LastPlayedAt.Value.LocalDateTime:g}"
                    : $"添加于 {item.AddedAt.LocalDateTime:d}"));
        }
        RecentSummary = RecentItems.Count == 0
            ? "还没有最近使用的游戏，添加后会显示在这里。"
            : string.Join("  ·  ", RecentItems.Take(3).Select(item => item.Name));
    }

    private void AppendStatusMessage(string message) =>
        StatusMessage = string.IsNullOrWhiteSpace(StatusMessage)
            ? message
            : $"{StatusMessage} {message}";
}
