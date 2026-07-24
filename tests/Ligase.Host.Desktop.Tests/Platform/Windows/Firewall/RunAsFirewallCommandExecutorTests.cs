using System.ComponentModel;
using System.Diagnostics;
using Ligase.Host.Core.Domain.WindowsFirewall;
using Ligase.Host.Core.Infrastructure.Windows;
using Ligase.Host.Desktop.Platform.Windows.Firewall;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests.Platform.Windows.Firewall;

[TestClass]
public sealed class RunAsFirewallCommandExecutorTests
{
    [TestMethod]
    public async Task UsesArgumentListWithoutConcatenatingPaths()
    {
        var launcher = new RecordingLauncher(0);
        var executor = new RunAsFirewallCommandExecutor(launcher);
        var command = new FirewallCommand(
            FirewallOperation.Apply,
            @"C:\Program Files\Ligase Host\Firewall\Manage-LigaseFirewall.ps1",
            @"C:\Program Files\Ligase Host\Firewall\ligase-firewall-v1.json",
            @"C:\Program Files\Ligase Host\Apollo\sunshine.exe",
            48989);

        var result = await executor.ExecuteAsync(command, CancellationToken.None);

        Assert.AreEqual(0, result.ExitCode);
        Assert.IsNotNull(launcher.StartInfo);
        Assert.AreEqual("runas", launcher.StartInfo.Verb);
        CollectionAssert.Contains(
            launcher.StartInfo.ArgumentList.ToArray(),
            command.Script);
        CollectionAssert.Contains(
            launcher.StartInfo.ArgumentList.ToArray(),
            command.Manifest);
        CollectionAssert.Contains(
            launcher.StartInfo.ArgumentList.ToArray(),
            command.Program);
    }

    [TestMethod]
    public async Task MapsUacCancellationToRetryableTypedResult()
    {
        var executor = new RunAsFirewallCommandExecutor(
            new RecordingLauncher(exception: new Win32Exception(1223)));

        var result = await executor.ExecuteAsync(Command(), CancellationToken.None);

        Assert.AreEqual(1223, result.ExitCode);
        Assert.AreEqual("uacCancelled", result.MachineDetail);
    }

    [TestMethod]
    public async Task PreservesNonZeroHelperExitCode()
    {
        var executor = new RunAsFirewallCommandExecutor(new RecordingLauncher(17));

        var result = await executor.ExecuteAsync(Command(), CancellationToken.None);

        Assert.AreEqual(17, result.ExitCode);
        Assert.AreEqual("elevatedHelperFailed", result.MachineDetail);
    }

    private static FirewallCommand Command() => new(
        FirewallOperation.Apply,
        "script.ps1",
        "manifest.json",
        "sunshine.exe",
        48989);

    private sealed class RecordingLauncher(
        int exitCode = 0,
        Exception? exception = null) : IFirewallElevatedProcessLauncher
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public Task<int> LaunchAsync(
            ProcessStartInfo startInfo,
            CancellationToken cancellationToken)
        {
            StartInfo = startInfo;
            return exception is null
                ? Task.FromResult(exitCode)
                : Task.FromException<int>(exception);
        }
    }
}
