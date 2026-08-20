using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class VirtualDisplayControlContractTests
{
    [TestMethod]
    public void TypedStatesExposeHonestChineseStatus()
    {
        var enabled = State("enabled", "none", driver: true, present: true);
        Assert.IsTrue(enabled.IsEnabled);
        StringAssert.Contains(enabled.StatusText, "已启用");

        var disabled = State("disabled", "none", driver: true, present: false);
        Assert.IsFalse(disabled.IsEnabled);
        StringAssert.Contains(disabled.StatusText, "驱动已就绪");

        var busy = State("unavailable", "streamActive", driver: true, present: false);
        Assert.AreEqual("串流进行中，暂时不能改变虚拟桌面状态", busy.StatusText);

        var readback = State("unavailable", "readbackFailed", driver: true, present: false);
        Assert.AreEqual("Windows 显示状态回读失败", readback.StatusText);
    }

    [TestMethod]
    public void CoreRouteReusesApolloVdisplayAndIsLoopbackOnly()
    {
        var root = RepositoryRoot();
        var process = File.ReadAllText(Path.Combine(root, "src", "process.cpp"));
        var http = File.ReadAllText(Path.Combine(root, "src", "nvhttp.cpp"));
        var desktop = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Core", "Services", "VirtualDisplayControlService.cs"));

        StringAssert.Contains(process, "VDISPLAY::createVirtualDisplay");
        StringAssert.Contains(process, "VDISPLAY::removeVirtualDisplay");
        StringAssert.Contains(process, "VDISPLAY::getDeviceSettings");
        StringAssert.Contains(http, "^/ligase/v1/virtual-display/enable$");
        StringAssert.Contains(http, "^/ligase/v1/virtual-display/disable$");
        StringAssert.Contains(http, "ligase_request_is_loopback(request)");
        StringAssert.Contains(desktop, "SemaphoreSlim _operation");
        StringAssert.Contains(desktop, "Timeout = TimeSpan.FromSeconds(8)");
        Assert.IsFalse(desktop.Contains("DeviceIoControl", StringComparison.Ordinal));
        Assert.IsFalse(desktop.Contains("SUDOVDA", StringComparison.Ordinal));
    }

    private static VirtualDisplayState State(
        string state, string reason, bool driver, bool present) =>
        new(1, state, reason, present ? @"\\.\DISPLAY9" : "", driver, present,
            driver, false, "apolloVirtualDisplayApp");

    private static string RepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("LIGASE_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CMakeLists.txt")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "Ligase.Desktop")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("repositoryRootUnavailable");
    }
}
