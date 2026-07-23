using Ligase.Host.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class LigaseEndpointTests
{
    [DataTestMethod]
    [DataRow("Apollo.Local", "apollo.local")]
    [DataRow("192.168.001.010", "192.168.1.10")]
    [DataRow("2001:0DB8:0:0:0:0:0:1", "2001:db8::1")]
    [DataRow("::1", "::1")]
    public void NormalizesSupportedHosts(string input, string expected)
    {
        var endpoint = LigaseEndpoint.Create(LigaseEndpointScheme.Http, input, 48989);

        Assert.AreEqual(expected, endpoint.Host);
        Assert.AreEqual((ushort)48989, endpoint.Port);
    }

    [TestMethod]
    public void FormatsIpv6AuthorityWithBrackets()
    {
        var endpoint = LigaseEndpoint.Create(LigaseEndpointScheme.Http, "::1", 49989);

        Assert.AreEqual(
            "http://[::1]:49989/serverinfo?uniqueid=test",
            endpoint.BuildUri("/serverinfo?uniqueid=test").AbsoluteUri);
    }

    [TestMethod]
    public void FormatsLinkLocalZoneUsingRfc6874Delimiter()
    {
        var endpoint = LigaseEndpoint.Create(
            LigaseEndpointScheme.Http,
            "fe80::1234",
            48989,
            "Ethernet 2");

        Assert.AreEqual(
            "http://[fe80::1234%25Ethernet%202]:48989/serverinfo",
            endpoint.FormatUri("/serverinfo"));
        Assert.ThrowsException<NotSupportedException>(() => endpoint.BuildUri("/serverinfo"));
    }

    [TestMethod]
    public void RequiresZoneForLinkLocalAndRejectsItElsewhere()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            LigaseEndpoint.Create(LigaseEndpointScheme.Http, "fe80::1", 48989));
        Assert.ThrowsException<ArgumentException>(() =>
            LigaseEndpoint.Create(LigaseEndpointScheme.Http, "::1", 48989, "2"));
        Assert.ThrowsException<ArgumentException>(() =>
            LigaseEndpoint.Create(LigaseEndpointScheme.Http, "2001:db8::1", 48989, "2"));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(65536)]
    public void RejectsPortOutsideWireRange(int port)
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            LigaseEndpoint.Create(LigaseEndpointScheme.Http, "localhost", port));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("[::1]")]
    [DataRow("::1:48989")]
    [DataRow("http://apollo.local")]
    [DataRow("host/path")]
    [DataRow("bad..host")]
    public void RejectsInvalidHost(string host)
    {
        Assert.ThrowsException<ArgumentException>(() =>
            LigaseEndpoint.Create(LigaseEndpointScheme.Http, host, 48989));
    }

    [DataTestMethod]
    [DataRow("0")]
    [DataRow("%12")]
    [DataRow("Ethernet/2")]
    [DataRow("Ethernet#2")]
    public void RejectsInvalidZone(string zone)
    {
        Assert.ThrowsException<ArgumentException>(() =>
            LigaseEndpoint.Create(LigaseEndpointScheme.Http, "fe80::1", 48989, zone));
    }

    [TestMethod]
    public void CandidateKeyExcludesSource()
    {
        var manual = LigaseEndpoint.Create(
            LigaseEndpointScheme.Http, "apollo.local", 48989, source: LigaseEndpointSource.Manual);
        var mdns = LigaseEndpoint.Create(
            LigaseEndpointScheme.Http, "apollo.local", 48989, source: LigaseEndpointSource.Mdns);

        Assert.AreEqual(manual.CandidateKey, mdns.CandidateKey);
    }
}
