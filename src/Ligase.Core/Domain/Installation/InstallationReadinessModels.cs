namespace Ligase.Host.Core.Domain.Installation;

public enum InstallationArtifactStatus
{
    Available,
    Missing,
    HashMismatch,
    Invalid
}

public enum VirtualDisplayReadinessStatus
{
    Available,
    NotInstalled,
    RebootRequired,
    Unsupported,
    Failed
}

public enum InstallationFirewallStatus
{
    Configured,
    NotConfigured,
    RequiresElevation,
    Failed
}

public enum InstallationDataRootStatus
{
    Fresh,
    Existing,
    Quarantined,
    Missing,
    WrongUser,
    AclDrift,
    Inaccessible
}

public enum InstallationSigningStatus
{
    TrustedPublisher,
    DevUnsigned,
    Invalid,
    MixedPublisher
}

public enum InstallationDriverTrustStatus
{
    CaTrusted,
    LocallyTrustedSelfSigned,
    Untrusted,
    Missing,
    Expired
}

public enum InstallationRecoveryAction
{
    None,
    OpenInstaller,
    RepairInstallation,
    RestartWindows,
    ConfigureFirewall,
    ReviewDriverTrust
}

public enum InstallationSetupStatus
{
    FirstRunRequired,
    Incomplete,
    Ready,
    Failed
}

public enum HostRuntimeReadinessStatus
{
    Ready,
    NotStarted,
    Starting,
    Failed
}

public enum StreamingCapabilityStatus
{
    NotAssessed,
    Available,
    Unavailable,
    Failed
}

public enum InstallationActionCode
{
    Started,
    Unavailable,
    Invalid,
    Canceled,
    Failed
}

public sealed record InstallationArtifactReadiness(
    InstallationArtifactStatus Status,
    string MachineCode,
    InstallationRecoveryAction RecoveryAction,
    string Version,
    bool HashMatches);

public sealed record VirtualDisplayReadiness(
    VirtualDisplayReadinessStatus Status,
    string MachineCode,
    InstallationRecoveryAction RecoveryAction,
    bool PhysicalDesktopStreamingAvailable);

public sealed record InstallationFirewallReadiness(
    InstallationFirewallStatus Status,
    string MachineCode,
    InstallationRecoveryAction RecoveryAction,
    int LegacyBroadRuleCount,
    bool LegacyCleanupRequiresExplicitConsent);

public sealed record InstallationDataRootReadiness(
    InstallationDataRootStatus Status,
    string MachineCode,
    InstallationRecoveryAction RecoveryAction);

public sealed record InstallationSigningReadiness(
    InstallationSigningStatus Status,
    string MachineCode,
    InstallationRecoveryAction RecoveryAction);

public sealed record InstallationDriverTrustReadiness(
    InstallationDriverTrustStatus Status,
    string MachineCode,
    InstallationRecoveryAction RecoveryAction,
    int StoreCount,
    bool SelfSigned,
    bool Timestamped);

public sealed record InstallationSetupReadiness(
    InstallationSetupStatus Status,
    string MachineCode,
    InstallationRecoveryAction RecoveryAction,
    bool IdentityAvailable);

public sealed record HostRuntimeReadiness(
    HostRuntimeReadinessStatus Status,
    string MachineCode,
    InstallationRecoveryAction RecoveryAction);

public sealed record StreamingCapabilityReadiness(
    StreamingCapabilityStatus Status,
    string MachineCode,
    InstallationRecoveryAction RecoveryAction);

public sealed record InstallationReadinessSnapshot(
    InstallationArtifactReadiness Desktop,
    InstallationArtifactReadiness ManagedCore,
    InstallationArtifactReadiness GameWatcher,
    VirtualDisplayReadiness VirtualDisplay,
    InstallationFirewallReadiness Firewall,
    InstallationDataRootReadiness DataRoot,
    InstallationSigningReadiness Signing,
    InstallationDriverTrustReadiness DriverTrust,
    InstallationSetupReadiness Setup,
    HostRuntimeReadiness HostRuntime,
    StreamingCapabilityReadiness StreamingCapability,
    bool RestartRequired,
    string MachineCode,
    InstallationRecoveryAction RecoveryAction);

public sealed record InstallationActionOutcome
{
    public InstallationActionOutcome(InstallationActionCode code, bool started)
    {
        if (started != (code == InstallationActionCode.Started))
            throw new ArgumentException("installationActionOutcomeInvalid");
        Code = code;
        Started = started;
    }

    public InstallationActionCode Code { get; }
    public bool Started { get; }
}
