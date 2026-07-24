using Ligase.Host.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class PairingNotificationServiceTests
{
    [TestMethod]
    public void ActivationArgumentsRoundTripEncodedInstanceAndRequest()
    {
        var values = PairingNotificationService.ParseArguments(
            "action=pairing&instance=start%7C48989&" +
            "request=9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4");

        Assert.AreEqual("pairing", values["action"]);
        Assert.AreEqual("start|48989", values["instance"]);
        Assert.AreEqual(
            "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
            values["request"]);
    }

    [TestMethod]
    public void DuplicateActivationArgumentIsRejected()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            PairingNotificationService.ParseArguments(
                "request=a&request=b"));
    }
}
