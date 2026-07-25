using Ligase.Host.Core.Application.Installation;
using Ligase.Host.Core.Domain.Installation;
using Ligase.Host.Core.Infrastructure.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class InstallationReadinessServiceTests
{
    private static IInstallationSetupProbe ReadyProbe() =>
        new FixedSetupProbe(new(
            new(
                InstallationSetupStatus.Ready,
                "setupReady",
                InstallationRecoveryAction.None,
                true),
            new(
                HostRuntimeReadinessStatus.Ready,
                "hostRuntimeReady",
                InstallationRecoveryAction.None),
            new(
                StreamingCapabilityStatus.NotAssessed,
                "streamingCapabilityNotAssessed",
                InstallationRecoveryAction.None)));

    [TestMethod]
    public async Task MapsCompleteDevReadbackWithoutExposingPaths()
    {
        var snapshot = await new InstallationReadinessService(
            new FixedSource(Complete()), ReadyProbe()).ReadAsync();

        Assert.AreEqual(InstallationArtifactStatus.Available, snapshot.Desktop.Status);
        Assert.AreEqual(InstallationArtifactStatus.Available, snapshot.ManagedCore.Status);
        Assert.AreEqual(InstallationArtifactStatus.Available, snapshot.GameWatcher.Status);
        Assert.AreEqual(InstallationSigningStatus.DevUnsigned, snapshot.Signing.Status);
        Assert.AreEqual(
            InstallationDriverTrustStatus.LocallyTrustedSelfSigned,
            snapshot.DriverTrust.Status);
        Assert.AreEqual(InstallationFirewallStatus.NotConfigured, snapshot.Firewall.Status);
        Assert.AreEqual(InstallationRecoveryAction.ConfigureFirewall, snapshot.RecoveryAction);
        Assert.IsFalse(snapshot.RestartRequired);
    }

    [TestMethod]
    public async Task MissingReadbackFailsClosedWithTypedRecovery()
    {
        var snapshot = await new InstallationReadinessService(
            new FixedSource(null), ReadyProbe()).ReadAsync();

        Assert.AreEqual(InstallationArtifactStatus.Invalid, snapshot.Desktop.Status);
        Assert.AreEqual("installationReadbackUnavailable", snapshot.MachineCode);
        Assert.AreEqual(
            InstallationRecoveryAction.RepairInstallation,
            snapshot.RecoveryAction);
    }

    [TestMethod]
    public async Task HashMismatchAndMixedPublisherTakePriority()
    {
        var data = Complete() with
        {
            Artifacts =
            [
                Artifact("desktop", "valid"),
                Artifact("managedCore", "nonRelease") with
                {
                    HashMatches = false,
                    MachineCode = "artifactHashMismatch"
                },
                Artifact("gameWatcher", "valid")
            ]
        };

        var snapshot = await new InstallationReadinessService(
            new FixedSource(data), ReadyProbe()).ReadAsync();

        Assert.AreEqual(
            InstallationArtifactStatus.HashMismatch,
            snapshot.ManagedCore.Status);
        Assert.AreEqual(
            InstallationSigningStatus.MixedPublisher,
            snapshot.Signing.Status);
        Assert.AreEqual("artifactHashMismatch", snapshot.MachineCode);
    }

    [TestMethod]
    public async Task SetupAndPhysicalDesktopAreIndependentTypedAuthorities()
    {
        var setup = new FixedSetupProbe(new(
            new(
                InstallationSetupStatus.Incomplete,
                "setupIncomplete",
                InstallationRecoveryAction.None,
                false),
            new(
                HostRuntimeReadinessStatus.NotStarted,
                "hostRuntimeNotStarted",
                InstallationRecoveryAction.None),
            new(
                StreamingCapabilityStatus.NotAssessed,
                "streamingCapabilityNotAssessed",
                InstallationRecoveryAction.None)));

        var snapshot = await new InstallationReadinessService(
            new FixedSource(Complete()),
            setup).ReadAsync();

        Assert.AreEqual(InstallationSetupStatus.Incomplete, snapshot.Setup.Status);
        Assert.IsFalse(snapshot.Setup.IdentityAvailable);
        Assert.IsTrue(snapshot.VirtualDisplay.PhysicalDesktopStreamingAvailable);
        Assert.AreEqual(
            StreamingCapabilityStatus.NotAssessed,
            snapshot.StreamingCapability.Status);
        Assert.AreEqual("setupIncomplete", snapshot.MachineCode);
    }

    [TestMethod]
    public void StrictParserRejectsUnknownAndDuplicateFields()
    {
        var valid = ReadbackJson();
        var unknown = valid.Replace(
            "\"restartRequired\":false",
            "\"unknown\":1,\"restartRequired\":false",
            StringComparison.Ordinal);
        var duplicate = valid.Replace(
            "\"code\":\"readbackComplete\"",
            "\"code\":\"readbackComplete\",\"code\":\"readbackComplete\"",
            StringComparison.Ordinal);

        Assert.ThrowsException<System.Text.Json.JsonException>(
            () => WindowsInstallationReadbackSource.Parse(unknown));
        Assert.ThrowsException<System.Text.Json.JsonException>(
            () => WindowsInstallationReadbackSource.Parse(duplicate));
    }

    [TestMethod]
    public void StrictParserAcceptsCurrentH2Shape()
    {
        var result = WindowsInstallationReadbackSource.Parse(ReadbackJson());

        Assert.AreEqual(3, result.Artifacts.Count);
        Assert.AreEqual("existing", result.DataRootState);
        Assert.AreEqual("locallyTrustedSelfSigned", result.DriverTrust.State);
    }

    [TestMethod]
    public void StrictParserAcceptsStructuredRootLauncherArtifact()
    {
        var withLauncher = ReadbackJson().Replace(
            "{\"role\":\"desktop\"",
            "{\"role\":\"launcher\",\"available\":true,\"version\":\"1.0.0.0\",\"hashMatches\":true,\"machineCode\":\"available\",\"signatureStatus\":\"nonRelease\"},{\"role\":\"desktop\"",
            StringComparison.Ordinal);
        var result = WindowsInstallationReadbackSource.Parse(withLauncher);

        Assert.AreEqual(4, result.Artifacts.Count);
        Assert.AreEqual("launcher", result.Artifacts[0].Role);
    }

    [TestMethod]
    public void StrictParserRejectsMalformedNoiseAndUnknownEnum()
    {
        AssertJsonFailure(() => WindowsInstallationReadbackSource.Parse("{"));
        AssertJsonFailure(() => WindowsInstallationReadbackSource.Parse(
            ReadbackJson() + "noise"));
        Assert.ThrowsException<JsonException>(
            () => WindowsInstallationReadbackSource.Parse(
                ReadbackJson().Replace(
                    "\"notInstalled\"",
                    "\"futureState\"",
                    StringComparison.Ordinal)));
    }

    private static void AssertJsonFailure(Action action)
    {
        try
        {
            action();
            Assert.Fail("Expected strict JSON rejection.");
        }
        catch (Exception exception)
        {
            Assert.IsInstanceOfType(exception, typeof(JsonException));
        }
    }

    [TestMethod]
    public void DeploymentValidationPinsReadbackHelperHashAndSchema()
    {
        var root = Path.Combine(
            Environment.GetEnvironmentVariable("LIGASE_TEMP_ROOT")
                ?? throw new AssertFailedException("LIGASE_TEMP_ROOT required"),
            "installation-readiness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var deployment = Path.Combine(root, "Deployment");
            Directory.CreateDirectory(Path.Combine(root, "Desktop"));
            Directory.CreateDirectory(deployment);
            var script = Path.Combine(
                deployment,
                "Manage-LigaseInstallation.ps1");
            File.WriteAllText(script, "# readback-only test", Encoding.UTF8);
            var hash = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(script))).ToLowerInvariant();
            var manifest = new
            {
                schemaVersion = 1,
                installLayout = "structured-v1",
                sourceHead = new string('0', 40),
                configuration = "Release",
                platform = "x64",
                installMode = "packaged",
                releaseKind = "UnsignedDev",
                artifacts = Array.Empty<object>(),
                privilegedHelpers = new[]
                {
                    new
                    {
                        relativePath = "Deployment/Manage-LigaseInstallation.ps1",
                        unsignedContentSha256 = hash,
                        signedArtifactSha256 = hash,
                        signerSubject = (string?)null,
                        signerThumbprint = (string?)null,
                        timestamped = false
                    }
                },
                virtualDisplay = new { },
                firewall = new { },
                encoder = new { },
                ownedEntries = Array.Empty<string>(),
                legacyFlatOwnedEntries = Array.Empty<string>()
            };
            File.WriteAllText(
                Path.Combine(root, "ligase-install-manifest.json"),
                JsonSerializer.Serialize(manifest),
                Encoding.UTF8);

            Assert.IsTrue(
                WindowsInstallationReadbackSource.ValidateDeployment(root));
            File.AppendAllText(script, "tampered", Encoding.UTF8);
            Assert.IsFalse(
                WindowsInstallationReadbackSource.ValidateDeployment(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ReadAsyncSharesOneBoundedReadAndCallerCancellation()
    {
        var source = new BlockingSource();
        var service = new InstallationReadinessService(source, ReadyProbe());
        var first = service.ReadAsync();
        using var cancellation = new CancellationTokenSource();
        var second = service.ReadAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => second);
        source.Complete(Complete());
        var snapshot = await first;

        Assert.AreEqual(1, source.CallCount);
        Assert.AreEqual(InstallationSetupStatus.Ready, snapshot.Setup.Status);
    }

    [TestMethod]
    public void ActionOutcomeRejectsContradictoryStartedFlag()
    {
        Assert.ThrowsException<ArgumentException>(
            () => new InstallationActionOutcome(
                InstallationActionCode.Unavailable,
                true));
    }

    [TestMethod]
    public async Task RecoveryLauncherIsTypedUnavailableAndHonorsCancellation()
    {
        var launcher = new UnavailableInstallationRecoveryLauncher();
        var outcome = await launcher.OpenInstallerAsync();
        Assert.AreEqual(InstallationActionCode.Unavailable, outcome.Code);
        Assert.IsFalse(outcome.Started);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(
            () => launcher.OpenInstallerAsync(cancellation.Token));
    }

    private static InstallationReadbackData Complete() =>
        new(
            [
                Artifact("desktop", "nonRelease"),
                Artifact("managedCore", "nonRelease"),
                Artifact("gameWatcher", "nonRelease")
            ],
            new("notInstalled", "virtualDisplayNotInstalled", true),
            new("notConfigured", "notConfigured", 2, true),
            new(
                "locallyTrustedSelfSigned",
                "driverTrustLocallyTrustedSelfSigned",
                4,
                true,
                false),
            "existing",
            false);

    private static InstallationReadbackArtifact Artifact(
        string role,
        string signature) =>
        new(role, true, "1.0.0.0", true, "available", signature);

    private static string ReadbackJson() => """
        {
          "code":"readbackComplete",
          "success":true,
          "installMode":"packaged",
          "sourceHead":"0000000000000000000000000000000000000000",
          "artifacts":[
            {"role":"desktop","available":true,"version":"1.0.0.0","hashMatches":true,"machineCode":"available","signatureStatus":"nonRelease"},
            {"role":"managedCore","available":true,"version":"0.0.0","hashMatches":true,"machineCode":"available","signatureStatus":"nonRelease"},
            {"role":"gameWatcher","available":true,"version":"1.0.0.0","hashMatches":true,"machineCode":"available","signatureStatus":"nonRelease"}
          ],
          "virtualDisplay":{"state":"notInstalled","machineCode":"virtualDisplayNotInstalled","physicalDesktopAvailable":true},
          "driverTrust":{"state":"locallyTrustedSelfSigned","machineCode":"driverTrustLocallyTrustedSelfSigned","storeCount":4,"selfSigned":true,"timestamped":false},
          "firewall":{"state":"notConfigured","machineCode":"notConfigured","legacyBroadRuleCount":0,"legacyBroadRuleNames":[],"legacyCleanupRequiresExplicitConsent":false},
          "encoder":{"state":"requiresRuntimeProbe","machineCode":"encoderProbePendingFirstLaunch","requiredForInstall":false,"requiredForStreaming":true},
          "dataRootState":"existing",
          "restartRequired":false
        }
        """;

    private sealed class FixedSource(InstallationReadbackData? value)
        : IInstallationReadbackSource
    {
        public Task<InstallationReadbackData?> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(value);
        }
    }

    private sealed class FixedSetupProbe(InstallationOperationalReadiness value)
        : IInstallationSetupProbe
    {
        public InstallationOperationalReadiness Read() => value;
    }

    private sealed class BlockingSource : IInstallationReadbackSource
    {
        private readonly TaskCompletionSource<InstallationReadbackData?> _source =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public Task<InstallationReadbackData?> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _source.Task.WaitAsync(cancellationToken);
        }

        public void Complete(InstallationReadbackData value) =>
            _source.SetResult(value);
    }
}
