using System.Text.Json;
using Ligase.Host.Core.Application.WindowsFirewall;
using Ligase.Host.Core.Domain.WindowsFirewall;
using Ligase.Host.Core.Infrastructure.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class WindowsFirewallTests
{
    private static readonly string ManifestFile = FindRepositoryFile(
        "src_assets",
        "windows",
        "misc",
        "firewall",
        "ligase-firewall-v1.json");

    [TestMethod]
    public void ManifestBuildsMinimumPrivateLocalSubnetPlan()
    {
        var planner = new WindowsFirewallPlanner();
        var manifest = planner.LoadManifest(ManifestFile);
        var plan = planner.Build(
            manifest,
            @"C:\Program Files\Ligase Host\sunshine.exe",
            48989);

        Assert.AreEqual("Private", plan.Profile);
        Assert.AreEqual("LocalSubnet", plan.RemoteAddress);
        Assert.AreEqual("Ligase Host LAN Access", plan.RuleGroup);
        Assert.AreEqual(2, plan.Rules.Count);
        CollectionAssert.AreEqual(
            new[] { 48984, 48989, 49010 },
            plan.Rules.Single(rule => rule.Transport == FirewallTransport.Tcp)
                .LocalPorts.ToArray());
        CollectionAssert.AreEqual(
            new[] { 48998, 48999, 49000 },
            plan.Rules.Single(rule => rule.Transport == FirewallTransport.Udp)
                .LocalPorts.ToArray());
        Assert.IsFalse(plan.Rules.SelectMany(rule => rule.LocalPorts).Contains(48990));
    }

    [TestMethod]
    public void PlanDerivesAllPortsFromConfiguredBase()
    {
        var planner = new WindowsFirewallPlanner();
        var plan = planner.Build(
            planner.LoadManifest(ManifestFile),
            @"D:\Deploy\sunshine.exe",
            50039);

        CollectionAssert.AreEqual(
            new[] { 50034, 50039, 50060 },
            plan.Rules[0].LocalPorts.ToArray());
        CollectionAssert.AreEqual(
            new[] { 50048, 50049, 50050 },
            plan.Rules[1].LocalPorts.ToArray());
    }

    [TestMethod]
    public void ManifestRejectsUnknownFieldAndUnsafeScope()
    {
        var source = File.ReadAllText(ManifestFile);
        var unknown = WriteTemporaryManifest(
            source.Replace(
                "\"schemaVersion\": 1,",
                "\"schemaVersion\": 1, \"surprise\": true,",
                StringComparison.Ordinal));
        var publicProfile = WriteTemporaryManifest(
            source.Replace("\"Private\"", "\"Public\"", StringComparison.Ordinal));
        var planner = new WindowsFirewallPlanner();

        Assert.AreEqual(
            FirewallOutcomeCodes.InvalidManifest,
            Assert.ThrowsException<FirewallPlanException>(
                () => planner.LoadManifest(unknown)).Code);
        Assert.AreEqual(
            FirewallOutcomeCodes.InvalidManifest,
            Assert.ThrowsException<FirewallPlanException>(
                () => planner.LoadManifest(publicProfile)).Code);
    }

    [TestMethod]
    public void ManifestRejectsAdminPortOrBroadProtocols()
    {
        var source = File.ReadAllText(ManifestFile);
        var admin = WriteTemporaryManifest(
            source.Replace("[-5, 0, 21]", "[-5, 0, 1, 21]", StringComparison.Ordinal));
        var missingUdp = WriteTemporaryManifest(
            source.Replace("[9, 10, 11]", "[9, 11]", StringComparison.Ordinal));
        var planner = new WindowsFirewallPlanner();

        Assert.ThrowsException<FirewallPlanException>(
            () => planner.LoadManifest(admin));
        Assert.ThrowsException<FirewallPlanException>(
            () => planner.LoadManifest(missingUdp));
    }

    [TestMethod]
    public void PlannerRejectsNonSunshineProgramAndOverflow()
    {
        var planner = new WindowsFirewallPlanner();
        var manifest = planner.LoadManifest(ManifestFile);

        Assert.AreEqual(
            FirewallOutcomeCodes.InvalidProgram,
            Assert.ThrowsException<FirewallPlanException>(
                () => planner.Build(manifest, @"C:\Ligase\Ligase.Host.Desktop.exe", 48989))
                .Code);
        Assert.AreEqual(
            FirewallOutcomeCodes.InvalidBasePort,
            Assert.ThrowsException<FirewallPlanException>(
                () => planner.Build(manifest, @"C:\Ligase\sunshine.exe", 65530))
                .Code);
    }

    [TestMethod]
    public async Task ServiceReturnsTypedReadbackAndNeverRequestsElevationItself()
    {
        var executor = new FakeExecutor(new FirewallCommandResult(
            0,
            """{"code":"configured","configured":true}""",
            false));
        var service = new WindowsFirewallService(
            new WindowsFirewallPlanner(),
            ManifestFile,
            @"C:\Ligase\Manage-LigaseFirewall.ps1",
            executor);

        var result = await service.ExecuteAsync(
            FirewallOperation.Readback,
            @"C:\Ligase\sunshine.exe",
            48989);

        Assert.AreEqual(FirewallOutcomeCodes.Configured, result.Code);
        Assert.IsTrue(result.IsConfigured);
        Assert.AreEqual(FirewallOperation.Readback, executor.LastCommand!.Operation);
    }

    [TestMethod]
    public async Task ServiceReportsElevationCancellationWithoutApplying()
    {
        var executor = new FakeExecutor(new FirewallCommandResult(
            5,
            string.Empty,
            true,
            "requiresElevation"));
        var service = new WindowsFirewallService(
            new WindowsFirewallPlanner(),
            ManifestFile,
            @"C:\Ligase\Manage-LigaseFirewall.ps1",
            executor);

        var result = await service.ExecuteAsync(
            FirewallOperation.Apply,
            @"C:\Ligase\sunshine.exe",
            48989);

        Assert.AreEqual(FirewallOutcomeCodes.RequiresElevation, result.Code);
        Assert.IsTrue(result.RequiresElevation);
        Assert.IsFalse(result.IsConfigured);
    }

    [TestMethod]
    public void DeploymentScriptOnlyOwnsStableLigaseRules()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "src_assets",
            "windows",
            "misc",
            "firewall",
            "Manage-LigaseFirewall.ps1"));

        StringAssert.Contains(script, "-Profile Private");
        StringAssert.Contains(script, "-RemoteAddress LocalSubnet");
        StringAssert.Contains(script, "-EdgeTraversalPolicy Block");
        Assert.IsFalse(script.Contains("Set-NetFirewallProfile", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("RemoteAddress Any", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("netsh", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("Get-NetFirewallRule |", StringComparison.Ordinal));

        using var document = JsonDocument.Parse(File.ReadAllBytes(ManifestFile));
        var names = document.RootElement.GetProperty("ownedRuleNames")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        Assert.IsTrue(names.All(name =>
            name.StartsWith("Ligase.Host.Lan.", StringComparison.Ordinal)));
        Assert.IsFalse(names.Contains("Apollo", StringComparer.Ordinal));
        StringAssert.Contains(script, "Get-OwnedSnapshots");
        StringAssert.Contains(script, "Restore-OwnedSnapshots");
    }

    private static string WriteTemporaryManifest(string content)
    {
        var file = Path.Combine(
            Path.GetTempPath(),
            $"ligase-firewall-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, content);
        return file;
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }

    private sealed class FakeExecutor(FirewallCommandResult result)
        : IFirewallCommandExecutor
    {
        public FirewallCommand? LastCommand { get; private set; }

        public Task<FirewallCommandResult> ExecuteAsync(
            FirewallCommand command,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            return Task.FromResult(result);
        }
    }
}
