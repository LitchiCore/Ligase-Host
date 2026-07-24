using CommunityToolkit.Mvvm.ComponentModel;
using Ligase.Host.Core.Domain.WindowsFirewall;
using Microsoft.UI.Xaml.Controls;

namespace Ligase.Host.Desktop.Presentation.Settings.Firewall;

public sealed record FirewallAccessReadback(
    FirewallOperationResult Result,
    FirewallEnvironmentSnapshot? Environment,
    string PortSummary);

public interface IFirewallAccessGateway
{
    Task<FirewallAccessReadback> ReadAsync(CancellationToken cancellationToken = default);
    Task<FirewallOperationResult> ApplyElevatedAsync(
        CancellationToken cancellationToken = default);
}

public partial class FirewallSettingsViewModel(IFirewallAccessGateway gateway)
    : ObservableObject
{
    private readonly SemaphoreSlim _singleFlight = new(1, 1);
    private bool _primaryActionConfigures;

    [ObservableProperty] private string _statusTitle = "正在检查局域网访问";
    [ObservableProperty] private string _statusMessage = "正在读取 Ligase 管理的防火墙规则。";
    [ObservableProperty] private string _actionLabel = "配置局域网访问";
    [ObservableProperty] private string _portSummary = "端口摘要尚不可用。";
    [ObservableProperty] private string _auditSummary =
        "当前网络类型与非 Ligase 旧规则尚未评估；不会自动删除或接管旧 Apollo/Sunshine 规则。";
    [ObservableProperty] private InfoBarSeverity _severity = InfoBarSeverity.Informational;
    [ObservableProperty] private bool _isBusy;

    public Task ExecutePrimaryActionAsync(
        CancellationToken cancellationToken = default) =>
        _primaryActionConfigures
            ? ConfigureAsync(cancellationToken)
            : LoadAsync(cancellationToken);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!await _singleFlight.WaitAsync(0, cancellationToken)) return;
        try
        {
            IsBusy = true;
            try
            {
                Apply(await gateway.ReadAsync(cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                ApplyFailure();
            }
        }
        finally
        {
            IsBusy = false;
            _singleFlight.Release();
        }
    }

    public async Task ConfigureAsync(CancellationToken cancellationToken = default)
    {
        if (!await _singleFlight.WaitAsync(0, cancellationToken)) return;
        try
        {
            IsBusy = true;
            StatusTitle = "等待 Windows 管理员授权";
            StatusMessage = "仅本次配置会请求 UAC；取消不会关闭 Host，可随时重试。";
            Severity = InfoBarSeverity.Informational;
            FirewallOperationResult applied;
            try
            {
                applied = await gateway.ApplyElevatedAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StatusTitle = "配置已取消";
                StatusMessage = "Host 仍在运行；需要时可重新配置局域网访问。";
                Severity = InfoBarSeverity.Warning;
                return;
            }
            catch
            {
                ApplyFailure();
                return;
            }
            if (applied.RequiresElevation)
            {
                StatusTitle = "需要管理员授权";
                StatusMessage = applied.Detail == "uacCancelled"
                    ? "已取消 Windows 管理员授权。Host 仍在运行；需要时可重新配置。"
                    : "Windows 未授予管理员权限。Host 仍可本机使用；请重新配置。";
                Severity = InfoBarSeverity.Warning;
                return;
            }
            FirewallAccessReadback readback;
            try
            {
                readback = await gateway.ReadAsync(cancellationToken);
            }
            catch
            {
                ApplyFailure();
                return;
            }
            if (applied.Code == FirewallOutcomeCodes.CommandFailed)
            {
                ApplyFailure();
                return;
            }
            Apply(readback);
        }
        finally
        {
            IsBusy = false;
            _singleFlight.Release();
        }
    }

    private void ApplyFailure()
    {
        _primaryActionConfigures = false;
        StatusTitle = "局域网状态读取失败";
        StatusMessage = "未能读取 Ligase 管理的规则。Host 仍可本机使用，请稍后重试。";
        Severity = InfoBarSeverity.Error;
        ActionLabel = "重新检查";
    }

    private void Apply(FirewallAccessReadback readback)
    {
        _primaryActionConfigures = readback.Result.Code is
            FirewallOutcomeCodes.Configured or
            FirewallOutcomeCodes.NotConfigured or
            FirewallOutcomeCodes.Drifted;
        PortSummary = readback.PortSummary;
        var networkSummary = readback.Environment switch
        {
            { NetworkCategory: FirewallNetworkCategory.Public } =>
                "当前网络不是 Private；规则只允许 Private LocalSubnet，不会扩大范围。",
            { NetworkCategory: FirewallNetworkCategory.NoActiveNetwork } =>
                "当前没有可用网络；规则状态保留，联网后可重新检查。",
            { NetworkCategory: FirewallNetworkCategory.Domain } =>
                "当前为域网络，不会启用仅限 Private 的 Ligase 规则。",
            { NetworkCategory: FirewallNetworkCategory.Unknown } =>
                "当前网络类型无法安全确认；不会扩大规则范围。",
            { NetworkCategory: FirewallNetworkCategory.Private } =>
                "当前网络为 Private；Ligase 规则仍只允许 LocalSubnet。",
            _ => "当前网络类型尚未评估；不会扩大规则范围。"
        };
        AuditSummary = readback.Environment?.HasLegacyBroadSunshineRule == true
            ? $"{networkSummary} 发现非 Ligase 管理的旧 Apollo/Sunshine 规则；不会自动删除或接管。"
            : $"{networkSummary} 不会自动删除或接管非 Ligase 管理的旧规则。";
        (StatusTitle, StatusMessage, Severity, ActionLabel) = readback.Result.Code switch
        {
            FirewallOutcomeCodes.Configured => (
                "局域网访问已配置",
                "Ligase 管理的规则与当前核心精确一致，仅适用于 Private LocalSubnet。",
                InfoBarSeverity.Success,
                "重新配置局域网访问"),
            FirewallOutcomeCodes.NotConfigured => (
                "局域网访问尚未配置",
                "其他设备暂时可能无法从局域网连接；点击配置时才会请求一次管理员授权。",
                InfoBarSeverity.Informational,
                "配置局域网访问"),
            FirewallOutcomeCodes.Drifted => (
                "局域网规则需要修复",
                "Ligase 管理的规则与当前核心不一致。可重新配置，Host 会保持运行。",
                InfoBarSeverity.Warning,
                "修复局域网访问"),
            FirewallOutcomeCodes.InvalidProgram => (
                "核心程序不可用",
                "无法确认 Ligase 管理的核心程序，已停止防火墙配置。请先恢复核心。",
                InfoBarSeverity.Error,
                "重新检查"),
            FirewallOutcomeCodes.InvalidManifest => (
                "配置资源不可用",
                "局域网配置资源缺失或无效。请修复 Ligase Host 安装后重试。",
                InfoBarSeverity.Error,
                "重新检查"),
            _ => (
                "局域网状态读取失败",
                "未能读取 Ligase 管理的规则。Host 仍可本机使用，请稍后重试。",
                InfoBarSeverity.Error,
                "重新检查")
        };
    }
}
