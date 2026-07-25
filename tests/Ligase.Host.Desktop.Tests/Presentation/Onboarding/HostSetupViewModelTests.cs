using Ligase.Host.Core.Application.Installation;
using Ligase.Host.Core.Domain.Installation;
using Ligase.Host.Desktop.Presentation.Onboarding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests.Presentation.Onboarding;

[TestClass]
public sealed class HostSetupViewModelTests
{
    [TestMethod]
    public async Task MissingVirtualDisplayKeepsPhysicalDesktopAvailable()
    {
        var viewModel = ViewModel(Snapshot(
            virtualDisplay: new(
                VirtualDisplayReadinessStatus.NotInstalled,
                "virtualDisplayNotInstalled",
                InstallationRecoveryAction.OpenInstaller,
                true)));

        await viewModel.LoadAsync();

        Assert.AreEqual("物理桌面可串流", viewModel.DisplayCapability.Status);
        StringAssert.Contains(viewModel.DisplayCapability.Description, "物理显示器仍可串流");
        StringAssert.Contains(viewModel.DisplayCapability.Description, "虚拟桌面尚不可用");
    }

    [TestMethod]
    public async Task FirstRunUsesSetupStatusAndDoesNotInferFromDataRoot()
    {
        var viewModel = ViewModel(Snapshot(
            setup: new(
                InstallationSetupStatus.FirstRunRequired,
                "firstRunRequired",
                InstallationRecoveryAction.None,
                false),
            dataRoot: new(
                InstallationDataRootStatus.Existing,
                "dataRootReady",
                InstallationRecoveryAction.None)));

        await viewModel.LoadAsync();

        Assert.IsTrue(viewModel.RequiresSetup);
        Assert.IsFalse(viewModel.CanFinish);
        Assert.AreEqual("正在创建新身份", viewModel.HostReadiness.Status);
    }

    [TestMethod]
    public async Task ReadySetupDoesNotClaimUnassessedStreamingCapability()
    {
        var viewModel = ViewModel(Snapshot());

        await viewModel.LoadAsync();

        Assert.IsFalse(viewModel.RequiresSetup);
        Assert.IsTrue(viewModel.CanFinish);
        Assert.AreEqual("基础设置完成", viewModel.HostReadiness.Status);
        StringAssert.Contains(viewModel.HostReadiness.Detail, "不等于已可开始串流");
    }

    [DataTestMethod]
    [DataRow(
        InstallationDataRootStatus.Missing,
        "dataRootMissing",
        "数据目录缺失",
        "Host 数据目录不存在")]
    [DataRow(
        InstallationDataRootStatus.WrongUser,
        "dataRootWrongUser",
        "Windows 账户不匹配",
        "此 Host 数据属于另一 Windows 账户")]
    [DataRow(
        InstallationDataRootStatus.AclDrift,
        "dataRootAclDrift",
        "数据目录权限异常",
        "数据目录权限不符合安全要求")]
    [DataRow(
        InstallationDataRootStatus.Inaccessible,
        "dataRootInaccessible",
        "数据目录不可访问",
        "无法访问 Host 数据目录")]
    public async Task DataRootFailuresUseTypedHostPresentation(
        InstallationDataRootStatus status,
        string machineCode,
        string expectedStatus,
        string expectedDescription)
    {
        var viewModel = ViewModel(Snapshot(
            dataRoot: new(
                status,
                machineCode,
                InstallationRecoveryAction.RepairInstallation)));

        await viewModel.LoadAsync();

        Assert.IsTrue(viewModel.RequiresSetup);
        Assert.IsFalse(viewModel.CanFinish);
        Assert.AreEqual(expectedStatus, viewModel.HostReadiness.Status);
        StringAssert.Contains(
            viewModel.HostReadiness.Description,
            expectedDescription);
    }

    [TestMethod]
    public async Task ReadyDataRootPreservesReadyHostPresentation()
    {
        var viewModel = ViewModel(Snapshot(
            dataRoot: new(
                InstallationDataRootStatus.Existing,
                "dataRootReady",
                InstallationRecoveryAction.None)));

        await viewModel.LoadAsync();

        Assert.IsFalse(viewModel.RequiresSetup);
        Assert.IsTrue(viewModel.CanFinish);
        Assert.AreEqual("基础设置完成", viewModel.HostReadiness.Status);
    }

    [TestMethod]
    public async Task DevelopmentSigningAndLocalSelfSignedDriverAreDisclosed()
    {
        var viewModel = ViewModel(Snapshot(
            signing: new(
                InstallationSigningStatus.DevUnsigned,
                "devUnsigned",
                InstallationRecoveryAction.None),
            trust: new(
                InstallationDriverTrustStatus.LocallyTrustedSelfSigned,
                "locallyTrustedSelfSigned",
                InstallationRecoveryAction.ReviewDriverTrust,
                2,
                true,
                false)));

        await viewModel.LoadAsync();

        StringAssert.Contains(viewModel.Notice, "开发版，发布者未验证");
        StringAssert.Contains(viewModel.Notice, "本机已信任的自签证书");
        Assert.IsFalse(
            viewModel.Notice!.Contains("驱动使用可信发布者", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task LegacyFirewallRulesAreOnlyDisclosed()
    {
        var viewModel = ViewModel(Snapshot(
            firewall: new(
                InstallationFirewallStatus.NotConfigured,
                "firewallNotConfigured",
                InstallationRecoveryAction.ConfigureFirewall,
                3,
                true)));

        await viewModel.LoadAsync();

        StringAssert.Contains(viewModel.LanAccess.Detail, "明确选择后清理");
        StringAssert.Contains(viewModel.Notice, "不会自动删除或接管");
    }

    [TestMethod]
    public async Task UnavailableRecoveryNeverPretendsInstallerStarted()
    {
        var launcher = new FakeLauncher(
            new(InstallationActionCode.Unavailable, false));
        var viewModel = ViewModel(Snapshot(
            desktop: Artifact(
                InstallationArtifactStatus.Missing,
                InstallationRecoveryAction.RepairInstallation)),
            launcher);
        await viewModel.LoadAsync();

        await viewModel.OpenInstallerAsync();

        Assert.AreEqual(1, launcher.Count);
        StringAssert.Contains(viewModel.ActionStatus, "没有可用的修复入口");
    }

    [DataTestMethod]
    [DataRow(InstallationSigningStatus.Invalid)]
    [DataRow(InstallationSigningStatus.MixedPublisher)]
    public async Task InvalidReleaseSigningCannotBeIgnored(
        InstallationSigningStatus signingStatus)
    {
        var viewModel = ViewModel(Snapshot(
            signing: new(
                signingStatus,
                "invalidSigning",
                InstallationRecoveryAction.RepairInstallation)));

        await viewModel.LoadAsync();

        Assert.IsFalse(viewModel.RequiresSetup);
        Assert.IsFalse(viewModel.CanFinish);
        StringAssert.Contains(viewModel.Notice, "不能忽略并继续");
    }

    private static HostSetupViewModel ViewModel(
        InstallationReadinessSnapshot snapshot,
        IInstallationRecoveryLauncher? launcher = null) =>
        new(new FakeReadiness(snapshot), launcher ?? new FakeLauncher(
            new InstallationActionOutcome(
                InstallationActionCode.Unavailable,
                false)));

    private static InstallationReadinessSnapshot Snapshot(
        InstallationArtifactReadiness? desktop = null,
        VirtualDisplayReadiness? virtualDisplay = null,
        InstallationFirewallReadiness? firewall = null,
        InstallationDataRootReadiness? dataRoot = null,
        InstallationSigningReadiness? signing = null,
        InstallationDriverTrustReadiness? trust = null,
        InstallationSetupReadiness? setup = null) =>
        new(
            desktop ?? Artifact(),
            Artifact(),
            Artifact(),
            virtualDisplay ?? new(
                VirtualDisplayReadinessStatus.Available,
                "virtualDisplayAvailable",
                InstallationRecoveryAction.None,
                true),
            firewall ?? new(
                InstallationFirewallStatus.Configured,
                "firewallConfigured",
                InstallationRecoveryAction.None,
                0,
                false),
            dataRoot ?? new(
                InstallationDataRootStatus.Existing,
                "dataRootReady",
                InstallationRecoveryAction.None),
            signing ?? new(
                InstallationSigningStatus.TrustedPublisher,
                "trustedPublisher",
                InstallationRecoveryAction.None),
            trust ?? new(
                InstallationDriverTrustStatus.CaTrusted,
                "caTrusted",
                InstallationRecoveryAction.None,
                1,
                false,
                true),
            setup ?? new(
                InstallationSetupStatus.Ready,
                "setupReady",
                InstallationRecoveryAction.None,
                true),
            new(
                HostRuntimeReadinessStatus.Ready,
                "hostRuntimeReady",
                InstallationRecoveryAction.None),
            new(
                StreamingCapabilityStatus.NotAssessed,
                "streamingCapabilityNotAssessed",
                InstallationRecoveryAction.None),
            false,
            "ready",
            InstallationRecoveryAction.None);

    private static InstallationArtifactReadiness Artifact(
        InstallationArtifactStatus status = InstallationArtifactStatus.Available,
        InstallationRecoveryAction recovery = InstallationRecoveryAction.None) =>
        new(status, status == InstallationArtifactStatus.Available
            ? "available"
            : "missing", recovery, "1.0.0", status == InstallationArtifactStatus.Available);

    private sealed class FakeReadiness(InstallationReadinessSnapshot snapshot)
        : IInstallationReadinessService
    {
        public Task<InstallationReadinessSnapshot> ReadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class FakeLauncher(InstallationActionOutcome outcome)
        : IInstallationRecoveryLauncher
    {
        public int Count { get; private set; }

        public Task<InstallationActionOutcome> OpenInstallerAsync(
            CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.FromResult(outcome);
        }
    }
}
