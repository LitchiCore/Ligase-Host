using Ligase.Host.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class IrreversibleExitGateTests
{
    [TestMethod]
    public void CommitIsOneWayAndExactlyOneCallerWins()
    {
        var gate = new IrreversibleExitGate();
        var wins = 0;

        Parallel.For(0, 32, _ =>
        {
            if (gate.TryCommit()) Interlocked.Increment(ref wins);
        });

        Assert.AreEqual(1, wins);
        Assert.IsTrue(gate.IsCommitted);
        Assert.IsFalse(gate.TryCommit());
    }
}
