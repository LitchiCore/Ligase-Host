using Ligase.Host.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class TrayActivationTests
{
    [TestMethod]
    public async Task LeftClickRestoresExactlyOnceAfterDoubleClickWindow()
    {
        using var gate = new TrayActivationDeduplicator(TimeSpan.FromMilliseconds(20));
        var restores = 0;

        gate.OnLeftButtonUp(() => Interlocked.Increment(ref restores));
        await Task.Delay(60);

        Assert.AreEqual(1, restores);
    }

    [TestMethod]
    public async Task DoubleClickCancelsPendingSingleAndSuppressesTrailingUp()
    {
        using var gate = new TrayActivationDeduplicator(TimeSpan.FromMilliseconds(40));
        var restores = 0;
        Action restore = () => Interlocked.Increment(ref restores);

        gate.OnLeftButtonUp(restore);
        gate.OnLeftButtonDoubleClick(restore);
        gate.OnLeftButtonUp(restore);
        await Task.Delay(80);

        Assert.AreEqual(1, restores);
    }
}
