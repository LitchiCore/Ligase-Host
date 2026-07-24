using Ligase.Host.Core.Application.WindowsFirewall;
using Ligase.Host.Core.Domain.WindowsFirewall;
using Ligase.Host.Core.Infrastructure.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class WindowsFirewallUiSupportTests
{
    [TestMethod]
    public void DeploymentConventionResolvesAvailableAndMissingAssets()
    {
        var directory = CreateTemporaryDirectory();
        var firewall = Path.Combine(
            directory,
            FirewallDeploymentConvention.DirectoryName);
        Directory.CreateDirectory(firewall);
        File.WriteAllText(
            Path.Combine(firewall, FirewallDeploymentConvention.ScriptFileName),
            string.Empty);

        var missing = FirewallDeploymentConvention.Resolve(directory);
        Assert.AreEqual(FirewallAssetStatus.MissingManifest, missing.Status);
        Assert.IsNotNull(missing.ScriptPath);
        Assert.IsNull(missing.ManifestPath);

        File.WriteAllText(
            Path.Combine(firewall, FirewallDeploymentConvention.ManifestFileName),
            "{}");
        var available = FirewallDeploymentConvention.Resolve(directory);
        Assert.AreEqual(FirewallAssetStatus.Available, available.Status);
        Assert.IsTrue(available.IsAvailable);
        Assert.AreEqual(
            FirewallDeploymentConvention.ScriptFileName,
            Path.GetFileName(available.ScriptPath));
        Assert.AreEqual(
            FirewallDeploymentConvention.ManifestFileName,
            Path.GetFileName(available.ManifestPath));
    }

    [TestMethod]
    public void ManagedExecutableRequiresExistingSunshineBinary()
    {
        var directory = CreateTemporaryDirectory();
        var sunshine = Path.Combine(directory, "sunshine.exe");
        var other = Path.Combine(directory, "Ligase.Host.Desktop.exe");
        File.WriteAllBytes(sunshine, []);
        File.WriteAllBytes(other, []);

        Assert.AreEqual(
            ManagedSunshineStatus.Available,
            ManagedSunshineExecutableValidator.Validate(sunshine).Status);
        Assert.AreEqual(
            ManagedSunshineStatus.InvalidExecutable,
            ManagedSunshineExecutableValidator.Validate(other).Status);
        Assert.AreEqual(
            ManagedSunshineStatus.InvalidExecutable,
            ManagedSunshineExecutableValidator.Validate(
                Path.Combine(directory, "missing", "sunshine.exe")).Status);
        Assert.AreEqual(
            ManagedSunshineStatus.Unavailable,
            ManagedSunshineExecutableValidator.Validate(null).Status);
    }

    [DataTestMethod]
    [DataRow("Private", FirewallNetworkCategory.Private)]
    [DataRow("Public", FirewallNetworkCategory.Public)]
    [DataRow("DomainAuthenticated", FirewallNetworkCategory.Domain)]
    [DataRow("FutureCategory", FirewallNetworkCategory.Unknown)]
    public async Task ReadbackMapsSingleActiveNetwork(
        string category,
        FirewallNetworkCategory expected)
    {
        var snapshot = await ReadAsync([category], []);
        Assert.AreEqual(expected, snapshot.NetworkCategory);
    }

    [TestMethod]
    public async Task ReadbackFailsClosedForMixedOrNoActiveNetworks()
    {
        Assert.AreEqual(
            FirewallNetworkCategory.Unknown,
            (await ReadAsync(["Private", "Public"], [])).NetworkCategory);
        Assert.AreEqual(
            FirewallNetworkCategory.NoActiveNetwork,
            (await ReadAsync([], [])).NetworkCategory);
    }

    [TestMethod]
    public async Task ReadbackReportsOnlyNonOwnedBroadSunshineRules()
    {
        var broad = Rule(
            "Sunshine",
            "Sunshine GameStream",
            string.Empty,
            @"C:\Legacy\sunshine.exe",
            "Any",
            ["Any"]);
        var snapshot = await ReadAsync(["Private"], [broad]);
        Assert.IsTrue(snapshot.HasLegacyBroadSunshineRule);

        var exactOwned = Rule(
            "Ligase.Host.Lan.Tcp.v1",
            "Ligase Host LAN TCP",
            "Ligase Host LAN Access",
            @"C:\Ligase\sunshine.exe",
            "Private",
            ["LocalSubnet"]);
        var unrelated = Rule(
            "Browser",
            "Browser",
            string.Empty,
            @"C:\Browser\browser.exe",
            "Any",
            ["Any"]);
        var scopedLegacy = Rule(
            "Sunshine Private LAN",
            "Sunshine Private LAN",
            string.Empty,
            @"C:\Legacy\sunshine.exe",
            "Private",
            ["LocalSubnet"]);
        snapshot = await ReadAsync(
            ["Private"],
            [exactOwned, scopedLegacy, unrelated]);
        Assert.IsFalse(snapshot.HasLegacyBroadSunshineRule);
    }

    [TestMethod]
    public async Task ReadbackIsObservationOnly()
    {
        var source = new FakeDataSource(
            new(["Private"], []));
        var reader = new WindowsFirewallEnvironmentReader(source);

        await reader.ReadAsync();

        Assert.AreEqual(1, source.ReadCount);
    }

    private static Task<FirewallEnvironmentSnapshot> ReadAsync(
        IReadOnlyList<string> categories,
        IReadOnlyList<FirewallRuleObservation> rules) =>
        new WindowsFirewallEnvironmentReader(
            new FakeDataSource(new(categories, rules))).ReadAsync();

    private static FirewallRuleObservation Rule(
        string name,
        string displayName,
        string group,
        string program,
        string profile,
        IReadOnlyList<string> remoteAddresses) =>
        new(name, displayName, group, program, profile, remoteAddresses);

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ligase-firewall-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FakeDataSource(FirewallEnvironmentData data)
        : IFirewallEnvironmentDataSource
    {
        public int ReadCount { get; private set; }

        public Task<FirewallEnvironmentData> ReadAsync(
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(data);
        }
    }
}
