using Ligase.Host.Core.Domain.WindowsFirewall;
using Ligase.Host.Desktop.Presentation.Settings.Firewall;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests.Presentation.Settings.Firewall;

[TestClass]
public sealed class FirewallSettingsViewModelTests
{
    [TestMethod]
    public async Task LoadUsesReadbackWithoutElevation()
    {
        var gateway = new FakeGateway(Result(FirewallOutcomeCodes.NotConfigured));
        var viewModel = new FirewallSettingsViewModel(gateway);
        await viewModel.LoadAsync();
        Assert.AreEqual(1, gateway.ReadCount);
        Assert.AreEqual(0, gateway.ApplyCount);
        Assert.AreEqual("局域网访问尚未配置", viewModel.StatusTitle);
    }

    [TestMethod]
    public async Task ExplicitConfigureElevatesOnceAndReadsBack()
    {
        var gateway = new FakeGateway(
            Result(FirewallOutcomeCodes.NotConfigured),
            Result(FirewallOutcomeCodes.Configured, true));
        var viewModel = new FirewallSettingsViewModel(gateway);
        await viewModel.ConfigureAsync();
        Assert.AreEqual(1, gateway.ApplyCount);
        Assert.AreEqual(1, gateway.ReadCount);
        Assert.AreEqual("局域网访问已配置", viewModel.StatusTitle);
    }

    [TestMethod]
    public async Task UacCancelKeepsRetryableStateWithoutReadback()
    {
        var gateway = new FakeGateway(
            Result(FirewallOutcomeCodes.NotConfigured),
            new FirewallOperationResult(
                FirewallOutcomeCodes.RequiresElevation,
                false,
                true,
                "uacCancelled"));
        var viewModel = new FirewallSettingsViewModel(gateway);
        await viewModel.ConfigureAsync();
        Assert.AreEqual(1, gateway.ApplyCount);
        Assert.AreEqual(0, gateway.ReadCount);
        StringAssert.Contains(viewModel.StatusMessage, "Host 仍在运行");
    }

    [TestMethod]
    public async Task RetryButtonDoesNotElevateAfterReadFailure()
    {
        var gateway = new FakeGateway(Result(FirewallOutcomeCodes.CommandFailed));
        var viewModel = new FirewallSettingsViewModel(gateway);
        await viewModel.LoadAsync();

        await viewModel.ExecutePrimaryActionAsync();

        Assert.AreEqual(2, gateway.ReadCount);
        Assert.AreEqual(0, gateway.ApplyCount);
    }

    [TestMethod]
    public async Task ConfigureHelperFailureStillReadsBackButRemainsFailed()
    {
        var gateway = new FakeGateway(
            Result(FirewallOutcomeCodes.NotConfigured),
            Result(FirewallOutcomeCodes.CommandFailed));
        var viewModel = new FirewallSettingsViewModel(gateway);

        await viewModel.ConfigureAsync();

        Assert.AreEqual(1, gateway.ReadCount);
        Assert.AreEqual("局域网状态读取失败", viewModel.StatusTitle);
    }

    [DataTestMethod]
    [DataRow(FirewallOutcomeCodes.Configured, "局域网访问已配置")]
    [DataRow(FirewallOutcomeCodes.Drifted, "局域网规则需要修复")]
    [DataRow(FirewallOutcomeCodes.InvalidManifest, "配置资源不可用")]
    [DataRow(FirewallOutcomeCodes.InvalidProgram, "核心程序不可用")]
    [DataRow(FirewallOutcomeCodes.CommandFailed, "局域网状态读取失败")]
    public async Task TypedResultProjectsNaturalStatus(string code, string title)
    {
        var viewModel = new FirewallSettingsViewModel(new FakeGateway(Result(code)));
        await viewModel.LoadAsync();
        Assert.AreEqual(title, viewModel.StatusTitle);
    }

    [TestMethod]
    public async Task NetworkAndLegacyWarningsNeverClaimAutomaticCleanup()
    {
        var gateway = new FakeGateway(
            Result(FirewallOutcomeCodes.Configured, true),
            environment: new FirewallEnvironmentSnapshot(
                FirewallNetworkCategory.Public,
                true));
        var viewModel = new FirewallSettingsViewModel(gateway);
        await viewModel.LoadAsync();
        StringAssert.Contains(viewModel.AuditSummary, "不会扩大范围");
        StringAssert.Contains(viewModel.AuditSummary, "不会自动删除或接管");
        Assert.AreEqual(0, gateway.ApplyCount);
    }

    [TestMethod]
    public async Task LegacyRuleIsOnlyReportedAndNeverClaimedAsManaged()
    {
        var gateway = new FakeGateway(
            Result(FirewallOutcomeCodes.Configured, true),
            environment: new FirewallEnvironmentSnapshot(
                FirewallNetworkCategory.Private,
                true));
        var viewModel = new FirewallSettingsViewModel(gateway);

        await viewModel.LoadAsync();

        StringAssert.Contains(viewModel.AuditSummary, "发现非 Ligase 管理");
        StringAssert.Contains(viewModel.AuditSummary, "不会自动删除或接管");
        Assert.AreEqual(0, gateway.ApplyCount);
    }

    [TestMethod]
    public async Task GatewayExceptionKeepsHostRetryable()
    {
        var viewModel = new FirewallSettingsViewModel(new ThrowingGateway());

        await viewModel.LoadAsync();

        Assert.AreEqual("局域网状态读取失败", viewModel.StatusTitle);
        StringAssert.Contains(viewModel.StatusMessage, "Host 仍可本机使用");
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task ConcurrentConfigureIsSingleFlight()
    {
        var gateway = new FakeGateway(Result(FirewallOutcomeCodes.Configured, true))
        {
            BlockApply = true
        };
        var viewModel = new FirewallSettingsViewModel(gateway);
        var first = viewModel.ConfigureAsync();
        await gateway.ApplyStarted.Task;
        await viewModel.ConfigureAsync();
        Assert.AreEqual(1, gateway.ApplyCount);
        gateway.ReleaseApply.SetResult();
        await first;
    }

    private static FirewallOperationResult Result(string code, bool configured = false) =>
        new(code, configured, false);

    private sealed class FakeGateway(
        FirewallOperationResult read,
        FirewallOperationResult? apply = null,
        FirewallEnvironmentSnapshot? environment = null)
        : IFirewallAccessGateway
    {
        public int ReadCount { get; private set; }
        public int ApplyCount { get; private set; }
        public bool BlockApply { get; init; }
        public TaskCompletionSource ApplyStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseApply { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<FirewallAccessReadback> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(new FirewallAccessReadback(
                ApplyCount > 0 && apply is not null ? apply : read,
                environment,
                "TCP 48984, 48989, 49010；UDP 48998, 48999, 49000"));
        }

        public async Task<FirewallOperationResult> ApplyElevatedAsync(
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            ApplyStarted.TrySetResult();
            if (BlockApply)
                await ReleaseApply.Task.WaitAsync(cancellationToken);
            return apply ?? read;
        }
    }

    private sealed class ThrowingGateway : IFirewallAccessGateway
    {
        public Task<FirewallAccessReadback> ReadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<FirewallAccessReadback>(new IOException("test"));

        public Task<FirewallOperationResult> ApplyElevatedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<FirewallOperationResult>(new IOException("test"));
    }
}
