using CommunityToolkit.Mvvm.ComponentModel;
using Ligase.Host.Core.Application.Installation;
using Ligase.Host.Core.Domain.Installation;

namespace Ligase.Host.Desktop.Presentation.Onboarding;

public sealed record HostSetupStep(
    string Glyph,
    string Title,
    string Status,
    string Description,
    string Detail);

public partial class HostSetupViewModel(
    IInstallationReadinessService readiness,
    IInstallationRecoveryLauncher recoveryLauncher) : ObservableObject
{
    [ObservableProperty]
    private HostSetupStep _coreFiles = Loading(
        "\uE7B8",
        "核心文件",
        "正在核对 Desktop、串流核心和 GameWatcher");

    [ObservableProperty]
    private HostSetupStep _displayCapability = Loading(
        "\uE7F4",
        "显示能力",
        "正在读取物理显示器和虚拟桌面能力");

    [ObservableProperty]
    private HostSetupStep _lanAccess = Loading(
        "\uE968",
        "局域网访问",
        "正在读取 Ligase 管理的防火墙规则");

    [ObservableProperty]
    private HostSetupStep _hostReadiness = Loading(
        "\uE9D9",
        "Host 就绪",
        "正在确认本机身份和运行状态");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _requiresSetup = true;

    [ObservableProperty]
    private bool _canFinish;

    [ObservableProperty]
    private bool _canOpenInstaller;

    [ObservableProperty]
    private string _actionStatus = string.Empty;

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        ActionStatus = string.Empty;
        try
        {
            Apply(await readiness.ReadAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            ErrorMessage =
                "暂时无法读取安装状态。Host 没有修改系统设置，请稍后重试。";
            CanFinish = false;
            CanOpenInstaller = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OpenInstallerAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        ActionStatus = string.Empty;
        try
        {
            var outcome = await recoveryLauncher.OpenInstallerAsync(cancellationToken);
            ActionStatus = outcome.Code switch
            {
                InstallationActionCode.Started =>
                    "已打开经过验证的安装程序。请在安装程序中确认修复。",
                InstallationActionCode.Canceled =>
                    "已取消打开安装程序，Host 未进行任何更改。",
                InstallationActionCode.Invalid =>
                    "安装程序未通过验证。请重新下载安装包。",
                InstallationActionCode.Failed =>
                    "暂时无法打开安装程序。请稍后重试。",
                _ =>
                    "当前安装包没有可用的修复入口。请重新下载安装包。"
            };
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal void Apply(InstallationReadinessSnapshot snapshot)
    {
        CoreFiles = MapCoreFiles(snapshot);
        DisplayCapability = MapDisplay(snapshot);
        LanAccess = MapFirewall(snapshot.Firewall);
        HostReadiness = MapHost(snapshot);
        RequiresSetup = snapshot.Setup.Status is
            InstallationSetupStatus.FirstRunRequired or
            InstallationSetupStatus.Incomplete or
            InstallationSetupStatus.Failed;
        CanFinish =
            snapshot.Setup.Status == InstallationSetupStatus.Ready &&
            snapshot.HostRuntime.Status == HostRuntimeReadinessStatus.Ready &&
            snapshot.Desktop.Status == InstallationArtifactStatus.Available &&
            snapshot.ManagedCore.Status == InstallationArtifactStatus.Available &&
            snapshot.GameWatcher.Status == InstallationArtifactStatus.Available &&
            snapshot.Signing.Status is not InstallationSigningStatus.Invalid
                and not InstallationSigningStatus.MixedPublisher;
        CanOpenInstaller = snapshot.RecoveryAction is
            InstallationRecoveryAction.OpenInstaller or
            InstallationRecoveryAction.RepairInstallation or
            InstallationRecoveryAction.ReviewDriverTrust;
        Notice = BuildNotice(snapshot);
    }

    private static HostSetupStep MapCoreFiles(
        InstallationReadinessSnapshot snapshot)
    {
        var artifacts = new[]
        {
            snapshot.Desktop,
            snapshot.ManagedCore,
            snapshot.GameWatcher
        };
        if (artifacts.All(item =>
                item.Status == InstallationArtifactStatus.Available))
        {
            var versions = artifacts
                .Select(item => item.Version)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return new(
                "\uE73E",
                "核心文件",
                "已就绪",
                "Desktop、串流核心和 GameWatcher 均来自已验证的安装产物。",
                versions.Length == 0
                    ? "文件哈希与安装清单一致。"
                    : $"版本 {string.Join(" / ", versions)}");
        }

        return new(
            "\uE783",
            "核心文件",
            "需要修复",
            "一个或多个必需文件缺失、损坏或与安装清单不一致。",
            "请使用经过验证的安装程序修复；Host 不会从旧目录补文件。");
    }

    private static HostSetupStep MapDisplay(
        InstallationReadinessSnapshot snapshot)
    {
        var display = snapshot.VirtualDisplay;
        var physical = display.PhysicalDesktopStreamingAvailable
            ? "当前物理显示器仍可串流。"
            : "当前没有可确认的物理桌面串流能力。";
        return display.Status switch
        {
            VirtualDisplayReadinessStatus.Available => new(
                "\uE73E",
                "显示能力",
                "物理桌面与虚拟桌面可用",
                "可以串流当前显示器，也可以使用独立虚拟桌面。",
                "虚拟显示组件已就绪。"),
            VirtualDisplayReadinessStatus.NotInstalled => new(
                "\uE7BA",
                "显示能力",
                display.PhysicalDesktopStreamingAvailable
                    ? "物理桌面可串流"
                    : "虚拟桌面不可用",
                $"{physical} 独立虚拟桌面尚不可用。",
                "安装虚拟显示组件需要通过安装程序明确确认。"),
            VirtualDisplayReadinessStatus.RebootRequired => new(
                "\uE777",
                "显示能力",
                "重启后启用虚拟桌面",
                $"{physical} 虚拟显示组件需要重启 Windows 后生效。",
                "保存工作后再重启；Host 不会自动重启电脑。"),
            VirtualDisplayReadinessStatus.Unsupported => new(
                "\uE946",
                "显示能力",
                "仅使用物理桌面",
                $"{physical} 此设备不支持当前虚拟显示组件。",
                "依赖虚拟桌面的入口将保持不可用。"),
            _ => new(
                "\uE783",
                "显示能力",
                display.PhysicalDesktopStreamingAvailable
                    ? "物理桌面可串流"
                    : "状态读取失败",
                $"{physical} 无法确认虚拟桌面状态。",
                "可稍后重试，或使用安装程序修复虚拟显示组件。")
        };
    }

    private static HostSetupStep MapFirewall(
        InstallationFirewallReadiness firewall) =>
        firewall.Status switch
        {
            InstallationFirewallStatus.Configured => new(
                "\uE73E",
                "局域网访问",
                "已配置",
                "Ligase 精确规则仅允许 Private 局域网 LocalSubnet 访问串流端口。",
                LegacyDetail(firewall)),
            InstallationFirewallStatus.RequiresElevation => new(
                "\uE72E",
                "局域网访问",
                "需要管理员确认",
                "只有点击设置页的“配置局域网访问”后才会出现一次 UAC。",
                LegacyDetail(firewall)),
            InstallationFirewallStatus.NotConfigured => new(
                "\uE968",
                "局域网访问",
                "尚未配置",
                "Host 不会在普通启动时自动修改 Windows 防火墙。",
                LegacyDetail(firewall)),
            _ => new(
                "\uE783",
                "局域网访问",
                "状态读取失败",
                "没有扩大防火墙范围；可前往设置页重试。",
                LegacyDetail(firewall))
        };

    private static HostSetupStep MapHost(
        InstallationReadinessSnapshot snapshot)
    {
        if (snapshot.Setup.Status == InstallationSetupStatus.Failed)
        {
            return new(
                "\uE783",
                "Host 就绪",
                "设置失败",
                "无法确认本机 Host 身份。",
                "请使用安装程序修复；不会复用旧证书或配对。");
        }
        if (!snapshot.Setup.IdentityAvailable)
        {
            return new(
                "\uE77B",
                "Host 就绪",
                "正在创建新身份",
                "首次启动将为这个全新数据目录创建 Host 身份和证书。",
                "不会导入旧电脑、旧证书或旧配对记录。");
        }
        if (snapshot.HostRuntime.Status != HostRuntimeReadinessStatus.Ready)
        {
            return new(
                "\uE768",
                "Host 就绪",
                "核心尚未就绪",
                "Host 身份已创建，正在等待独立串流核心启动。",
                "此状态不代表编码器已经可用。");
        }

        return new(
            "\uE73E",
            "Host 就绪",
            "基础设置完成",
            "新 Host 身份和独立串流核心已就绪。",
            snapshot.StreamingCapability.Status switch
            {
                StreamingCapabilityStatus.Available =>
                    "已确认当前设备具备串流编码能力。",
                StreamingCapabilityStatus.Unavailable =>
                    "当前没有可用的串流编码器，请查看串流监控。",
                StreamingCapabilityStatus.Failed =>
                    "编码能力检查失败，请在串流监控中重试。",
                _ =>
                    "尚未评估编码能力；基础设置完成不等于已可开始串流。"
            });
    }

    private static string? BuildNotice(InstallationReadinessSnapshot snapshot)
    {
        var notices = new List<string>();
        if (snapshot.Signing.Status == InstallationSigningStatus.DevUnsigned)
            notices.Add("这是开发版，发布者未验证；不要将它当作正式发行版本。");
        else if (snapshot.Signing.Status is
                 InstallationSigningStatus.Invalid or
                 InstallationSigningStatus.MixedPublisher)
            notices.Add("安装产物的发布者状态无效或不一致，不能忽略并继续。");

        if (snapshot.DriverTrust.Status ==
            InstallationDriverTrustStatus.LocallyTrustedSelfSigned)
        {
            notices.Add(
                "虚拟显示驱动使用本机已信任的自签证书；这不等于可信发布者。");
        }

        if (snapshot.Firewall.LegacyBroadRuleCount > 0)
        {
            notices.Add(
                $"发现 {snapshot.Firewall.LegacyBroadRuleCount} 条非 Ligase 管理的旧宽泛规则；Host 不会自动删除或接管。");
        }

        return notices.Count == 0 ? null : string.Join(Environment.NewLine, notices);
    }

    private static string LegacyDetail(InstallationFirewallReadiness firewall) =>
        firewall.LegacyBroadRuleCount > 0
            ? $"另发现 {firewall.LegacyBroadRuleCount} 条旧宽泛规则，仅能在明确选择后清理。"
            : "未发现需要提示的旧宽泛规则。";

    private static HostSetupStep Loading(
        string glyph,
        string title,
        string description) =>
        new(glyph, title, "正在检查", description, "请稍候。");
}
