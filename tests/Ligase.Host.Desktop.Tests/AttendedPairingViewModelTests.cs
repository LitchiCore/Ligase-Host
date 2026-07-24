using Ligase.Host.Core.Models;
using Ligase.Host.Desktop.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class AttendedPairingViewModelTests
{
    [DataTestMethod]
    [DataRow("192.168.1.4", null, "局域网 IPv4")]
    [DataRow("8.8.8.8", null, "其他 IPv4 网络")]
    [DataRow("fe80::1", 7u, "局域网 IPv6（链路本地）")]
    [DataRow("2001:db8::1", null, "局域网 IPv6")]
    public void SourceClassificationIsNaturalLanguage(
        string address,
        uint? scopeId,
        string expected)
    {
        Assert.AreEqual(
            expected,
            PendingPairingCard.DescribeSource(
                new PairingSourceAddress(address, scopeId)));
    }
}
