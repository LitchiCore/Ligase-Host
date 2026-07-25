using Ligase.Host.Core.Application.Installation;
using Ligase.Host.Core.Domain.Installation;
using Ligase.Host.Core.Services;

namespace Ligase.Host.Core.Infrastructure.Windows;

public sealed class HostInstallationSetupProbe(
    LigasePaths paths,
    ApolloInstanceManager managedCore) : IInstallationSetupProbe
{
    public InstallationOperationalReadiness Read()
    {
        var rootExists = Directory.Exists(paths.RootDirectory);
        var identityAvailable =
            File.Exists(paths.AuthorityFile) &&
            File.Exists(paths.ApolloCertificateFile) &&
            File.Exists(paths.ApolloPrivateKeyFile);
        var setup = !rootExists
            ? new InstallationSetupReadiness(
                InstallationSetupStatus.FirstRunRequired,
                "firstRunRequired",
                InstallationRecoveryAction.None,
                false)
            : identityAvailable
                ? new InstallationSetupReadiness(
                    InstallationSetupStatus.Ready,
                    "setupReady",
                    InstallationRecoveryAction.None,
                    true)
                : new InstallationSetupReadiness(
                    InstallationSetupStatus.Incomplete,
                    "setupIncomplete",
                    InstallationRecoveryAction.None,
                    false);
        var runtime = managedCore.IsRunning
            ? new HostRuntimeReadiness(
                HostRuntimeReadinessStatus.Ready,
                "hostRuntimeReady",
                InstallationRecoveryAction.None)
            : new HostRuntimeReadiness(
                HostRuntimeReadinessStatus.NotStarted,
                "hostRuntimeNotStarted",
                InstallationRecoveryAction.None);
        return new(
            setup,
            runtime,
            new(
                StreamingCapabilityStatus.NotAssessed,
                "streamingCapabilityNotAssessed",
                InstallationRecoveryAction.None));
    }
}
