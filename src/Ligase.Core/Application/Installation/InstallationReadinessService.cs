using Ligase.Host.Core.Domain.Installation;

namespace Ligase.Host.Core.Application.Installation;

public interface IInstallationReadinessService
{
    Task<InstallationReadinessSnapshot> ReadAsync(
        CancellationToken cancellationToken = default);
}

public interface IInstallationRecoveryLauncher
{
    Task<InstallationActionOutcome> OpenInstallerAsync(
        CancellationToken cancellationToken = default);
}

public interface IInstallationReadbackSource
{
    Task<InstallationReadbackData?> ReadAsync(
        CancellationToken cancellationToken = default);
}

public interface IInstallationSetupProbe
{
    InstallationOperationalReadiness Read();
}

public sealed record InstallationOperationalReadiness(
    InstallationSetupReadiness Setup,
    HostRuntimeReadiness HostRuntime,
    StreamingCapabilityReadiness StreamingCapability);

public sealed record InstallationReadbackArtifact(
    string Role,
    bool Available,
    string Version,
    bool HashMatches,
    string MachineCode,
    string SignatureStatus);

public sealed record InstallationReadbackVirtualDisplay(
    string State,
    string MachineCode,
    bool PhysicalDesktopAvailable);

public sealed record InstallationReadbackFirewall(
    string State,
    string MachineCode,
    int LegacyBroadRuleCount,
    bool LegacyCleanupRequiresExplicitConsent);

public sealed record InstallationReadbackDriverTrust(
    string State,
    string MachineCode,
    int StoreCount,
    bool SelfSigned,
    bool Timestamped);

public sealed record InstallationReadbackData(
    IReadOnlyList<InstallationReadbackArtifact> Artifacts,
    InstallationReadbackVirtualDisplay VirtualDisplay,
    InstallationReadbackFirewall Firewall,
    InstallationReadbackDriverTrust DriverTrust,
    string DataRootState,
    bool RestartRequired);

public sealed class InstallationReadinessService(
    IInstallationReadbackSource readbackSource,
    IInstallationSetupProbe setupProbe) : IInstallationReadinessService
{
    private static readonly TimeSpan ReadbackTimeout = TimeSpan.FromSeconds(8);
    private readonly object _sync = new();
    private Task<InstallationReadinessSnapshot>? _activeRead;

    public Task<InstallationReadinessSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        Task<InstallationReadinessSnapshot> active;
        lock (_sync)
        {
            if (_activeRead is null)
            {
                active = ReadCoreAsync();
                _activeRead = active;
                _ = active.ContinueWith(
                    completed =>
                    {
                        lock (_sync)
                        {
                            if (ReferenceEquals(_activeRead, completed))
                                _activeRead = null;
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            else
            {
                active = _activeRead;
            }
        }
        return active.WaitAsync(cancellationToken);
    }

    private async Task<InstallationReadinessSnapshot> ReadCoreAsync()
    {
        using var timeout = new CancellationTokenSource(ReadbackTimeout);
        InstallationReadbackData? readback;
        try
        {
            readback = await readbackSource.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            readback = null;
        }
        var operational = ReadOperationalSafely();
        if (readback is null)
            return Unavailable(operational);

        var desktop = MapArtifact(readback, "desktop");
        var core = MapArtifact(readback, "managedCore");
        var watcher = MapArtifact(readback, "gameWatcher");
        var virtualDisplay = MapVirtualDisplay(readback.VirtualDisplay);
        var firewall = MapFirewall(readback.Firewall);
        var dataRoot = MapDataRoot(readback.DataRootState);
        var signing = MapSigning(readback.Artifacts);
        var driverTrust = MapDriverTrust(readback.DriverTrust);
        var (code, recovery) = Overall(
            desktop,
            core,
            watcher,
            operational.Setup,
            dataRoot,
            signing,
            firewall,
            driverTrust,
            virtualDisplay,
            readback.RestartRequired);
        return new(
            desktop,
            core,
            watcher,
            virtualDisplay,
            firewall,
            dataRoot,
            signing,
            driverTrust,
            operational.Setup,
            operational.HostRuntime,
            operational.StreamingCapability,
            readback.RestartRequired,
            code,
            recovery);
    }

    private InstallationOperationalReadiness ReadOperationalSafely()
    {
        try
        {
            return setupProbe.Read();
        }
        catch
        {
            return InstallationOperationalReadinessDefaults.Failed;
        }
    }

    private static InstallationArtifactReadiness MapArtifact(
        InstallationReadbackData readback,
        string role)
    {
        var item = readback.Artifacts.SingleOrDefault(
            artifact => string.Equals(
                artifact.Role,
                role,
                StringComparison.Ordinal));
        if (item is null)
            return InvalidArtifact("artifactProjectionMissing");

        var status = item switch
        {
            { Available: false } => InstallationArtifactStatus.Missing,
            { HashMatches: false } => InstallationArtifactStatus.HashMismatch,
            { MachineCode: "available" } => InstallationArtifactStatus.Available,
            _ => InstallationArtifactStatus.Invalid
        };
        return new(
            status,
            StableCode(item.MachineCode, "artifactReadbackInvalid"),
            status == InstallationArtifactStatus.Available
                ? InstallationRecoveryAction.None
                : InstallationRecoveryAction.RepairInstallation,
            item.Version,
            item.HashMatches);
    }

    private static VirtualDisplayReadiness MapVirtualDisplay(
        InstallationReadbackVirtualDisplay item)
    {
        var status = item.State switch
        {
            "available" => VirtualDisplayReadinessStatus.Available,
            "notInstalled" => VirtualDisplayReadinessStatus.NotInstalled,
            "rebootRequired" => VirtualDisplayReadinessStatus.RebootRequired,
            "unsupported" => VirtualDisplayReadinessStatus.Unsupported,
            _ => VirtualDisplayReadinessStatus.Failed
        };
        var recovery = status switch
        {
            VirtualDisplayReadinessStatus.Available =>
                InstallationRecoveryAction.None,
            VirtualDisplayReadinessStatus.RebootRequired =>
                InstallationRecoveryAction.RestartWindows,
            VirtualDisplayReadinessStatus.Failed =>
                InstallationRecoveryAction.RepairInstallation,
            _ => InstallationRecoveryAction.OpenInstaller
        };
        return new(
            status,
            StableCode(item.MachineCode, "virtualDisplayReadbackFailed"),
            recovery,
            item.PhysicalDesktopAvailable);
    }

    private static InstallationFirewallReadiness MapFirewall(
        InstallationReadbackFirewall item)
    {
        var status = item.State switch
        {
            "configured" => InstallationFirewallStatus.Configured,
            "notConfigured" => InstallationFirewallStatus.NotConfigured,
            "requiresElevation" => InstallationFirewallStatus.RequiresElevation,
            _ => InstallationFirewallStatus.Failed
        };
        return new(
            status,
            StableCode(item.MachineCode, "firewallReadbackFailed"),
            status == InstallationFirewallStatus.Configured
                ? InstallationRecoveryAction.None
                : InstallationRecoveryAction.ConfigureFirewall,
            Math.Max(0, item.LegacyBroadRuleCount),
            item.LegacyCleanupRequiresExplicitConsent);
    }

    private static InstallationDataRootReadiness MapDataRoot(string state)
    {
        var status = state switch
        {
            "fresh" => InstallationDataRootStatus.Fresh,
            "existing" => InstallationDataRootStatus.Existing,
            "quarantined" => InstallationDataRootStatus.Quarantined,
            "missing" => InstallationDataRootStatus.Missing,
            "wrongUser" => InstallationDataRootStatus.WrongUser,
            "aclDrift" => InstallationDataRootStatus.AclDrift,
            _ => InstallationDataRootStatus.Inaccessible
        };
        var ready = status == InstallationDataRootStatus.Existing;
        return new(
            status,
            status switch
            {
                InstallationDataRootStatus.Fresh => "dataRootFresh",
                InstallationDataRootStatus.Existing => "dataRootReady",
                InstallationDataRootStatus.Quarantined => "dataRootQuarantined",
                InstallationDataRootStatus.Missing => "dataRootMissing",
                InstallationDataRootStatus.WrongUser => "dataRootWrongUser",
                InstallationDataRootStatus.AclDrift => "dataRootAclDrift",
                _ => "dataRootInaccessible"
            },
            ready
                ? InstallationRecoveryAction.None
                : InstallationRecoveryAction.RepairInstallation);
    }

    private static InstallationSigningReadiness MapSigning(
        IReadOnlyList<InstallationReadbackArtifact> artifacts)
    {
        var signatures = artifacts
            .Select(item => item.SignatureStatus)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var status = signatures switch
        {
            ["valid"] => InstallationSigningStatus.TrustedPublisher,
            ["nonRelease"] => InstallationSigningStatus.DevUnsigned,
            { Length: > 1 } => InstallationSigningStatus.MixedPublisher,
            _ => InstallationSigningStatus.Invalid
        };
        return new(
            status,
            status switch
            {
                InstallationSigningStatus.TrustedPublisher =>
                    "trustedPublisher",
                InstallationSigningStatus.DevUnsigned => "devUnsigned",
                InstallationSigningStatus.MixedPublisher => "mixedPublisher",
                _ => "signatureInvalid"
            },
            status is InstallationSigningStatus.Invalid
                or InstallationSigningStatus.MixedPublisher
                ? InstallationRecoveryAction.RepairInstallation
                : InstallationRecoveryAction.None);
    }

    private static InstallationDriverTrustReadiness MapDriverTrust(
        InstallationReadbackDriverTrust item)
    {
        var status = item.State switch
        {
            "caTrusted" => InstallationDriverTrustStatus.CaTrusted,
            "locallyTrustedSelfSigned" =>
                InstallationDriverTrustStatus.LocallyTrustedSelfSigned,
            "untrusted" => InstallationDriverTrustStatus.Untrusted,
            "missing" => InstallationDriverTrustStatus.Missing,
            "expired" => InstallationDriverTrustStatus.Expired,
            _ => InstallationDriverTrustStatus.Untrusted
        };
        return new(
            status,
            StableCode(item.MachineCode, "driverTrustUnavailable"),
            status == InstallationDriverTrustStatus.CaTrusted
                ? InstallationRecoveryAction.None
                : InstallationRecoveryAction.ReviewDriverTrust,
            Math.Max(0, item.StoreCount),
            item.SelfSigned,
            item.Timestamped);
    }

    private static (string Code, InstallationRecoveryAction Recovery) Overall(
        InstallationArtifactReadiness desktop,
        InstallationArtifactReadiness core,
            InstallationArtifactReadiness watcher,
        InstallationSetupReadiness setup,
        InstallationDataRootReadiness dataRoot,
        InstallationSigningReadiness signing,
        InstallationFirewallReadiness firewall,
        InstallationDriverTrustReadiness driverTrust,
        VirtualDisplayReadiness virtualDisplay,
        bool restartRequired)
    {
        foreach (var artifact in new[] { desktop, core, watcher })
        {
            if (artifact.Status != InstallationArtifactStatus.Available)
                return (artifact.MachineCode, artifact.RecoveryAction);
        }
        if (setup.Status != InstallationSetupStatus.Ready)
            return (setup.MachineCode, setup.RecoveryAction);
        if (dataRoot.Status is InstallationDataRootStatus.Missing
            or InstallationDataRootStatus.WrongUser
            or InstallationDataRootStatus.AclDrift
            or InstallationDataRootStatus.Inaccessible)
            return (dataRoot.MachineCode, dataRoot.RecoveryAction);
        if (signing.Status is InstallationSigningStatus.Invalid
            or InstallationSigningStatus.MixedPublisher)
            return (signing.MachineCode, signing.RecoveryAction);
        if (restartRequired)
            return ("restartRequired", InstallationRecoveryAction.RestartWindows);
        if (firewall.Status != InstallationFirewallStatus.Configured)
            return (firewall.MachineCode, firewall.RecoveryAction);
        if (driverTrust.Status != InstallationDriverTrustStatus.CaTrusted)
            return (driverTrust.MachineCode, driverTrust.RecoveryAction);
        if (virtualDisplay.Status == VirtualDisplayReadinessStatus.Failed)
            return (virtualDisplay.MachineCode, virtualDisplay.RecoveryAction);
        return ("ready", InstallationRecoveryAction.None);
    }

    private static InstallationReadinessSnapshot Unavailable(
        InstallationOperationalReadiness operational)
    {
        var artifact = InvalidArtifact("installationReadbackUnavailable");
        return new(
            artifact,
            artifact,
            artifact,
            new(
                VirtualDisplayReadinessStatus.Failed,
                "installationReadbackUnavailable",
                InstallationRecoveryAction.RepairInstallation,
                false),
            new(
                InstallationFirewallStatus.Failed,
                "installationReadbackUnavailable",
                InstallationRecoveryAction.ConfigureFirewall,
                0,
                false),
            new(
                InstallationDataRootStatus.Inaccessible,
                "installationReadbackUnavailable",
                InstallationRecoveryAction.RepairInstallation),
            new(
                InstallationSigningStatus.Invalid,
                "installationReadbackUnavailable",
                InstallationRecoveryAction.RepairInstallation),
            new(
                InstallationDriverTrustStatus.Missing,
                "installationReadbackUnavailable",
                InstallationRecoveryAction.ReviewDriverTrust,
                0,
                false,
                false),
            operational.Setup,
            operational.HostRuntime,
            operational.StreamingCapability,
            false,
            "installationReadbackUnavailable",
            InstallationRecoveryAction.RepairInstallation);
    }

    private static InstallationArtifactReadiness InvalidArtifact(string code) =>
        new(
            InstallationArtifactStatus.Invalid,
            code,
            InstallationRecoveryAction.RepairInstallation,
            string.Empty,
            false);

    private static string StableCode(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Length > 80 ||
        value.Any(character => !char.IsAsciiLetterOrDigit(character))
            ? fallback
            : value;
}

public static class InstallationOperationalReadinessDefaults
{
    public static InstallationOperationalReadiness Failed { get; } = new(
        new(
            InstallationSetupStatus.Failed,
            "setupProbeFailed",
            InstallationRecoveryAction.RepairInstallation,
            false),
        new(
            HostRuntimeReadinessStatus.Failed,
            "hostRuntimeProbeFailed",
            InstallationRecoveryAction.RepairInstallation),
        new(
            StreamingCapabilityStatus.NotAssessed,
            "streamingCapabilityNotAssessed",
            InstallationRecoveryAction.None));
}
