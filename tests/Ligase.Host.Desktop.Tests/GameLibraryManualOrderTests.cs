using Ligase.Host.Desktop.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class GameLibraryManualOrderTests
{
    [TestMethod]
    public void CompleteOrderAcceptsReorderedSystemAndGameItems()
    {
        var desktop = Guid.NewGuid();
        var virtualDesktop = Guid.NewGuid();
        var game = Guid.NewGuid();

        Assert.IsTrue(ManualLibraryOrder.IsCompleteOrder(
            [desktop, virtualDesktop, game],
            [virtualDesktop, game, desktop]));
    }

    [TestMethod]
    public void CompleteOrderRejectsMissingOrUnknownItems()
    {
        var desktop = Guid.NewGuid();
        var virtualDesktop = Guid.NewGuid();
        var game = Guid.NewGuid();

        Assert.IsFalse(ManualLibraryOrder.IsCompleteOrder(
            [desktop, virtualDesktop, game],
            [desktop, virtualDesktop]));
        Assert.IsFalse(ManualLibraryOrder.IsCompleteOrder(
            [desktop, virtualDesktop, game],
            [desktop, virtualDesktop, Guid.NewGuid()]));
    }

    [TestMethod]
    public void CompleteOrderRejectsDuplicateItems()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Assert.IsFalse(ManualLibraryOrder.IsCompleteOrder(
            [first, second],
            [first, first]));
    }

}
