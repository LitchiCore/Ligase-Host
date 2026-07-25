using Ligase.Host.Core.Domain.WindowsFirewall;
using Ligase.Host.Core.Domain.Installation;

namespace Ligase.Host.Core.Application.WindowsFirewall;

public static class FirewallDeploymentConvention
{
    public const string DirectoryName = "Firewall";
    public const string ScriptFileName = "Manage-LigaseFirewall.ps1";
    public const string ManifestFileName = "ligase-firewall-v1.json";

    public static FirewallDeploymentAssets Resolve(string desktopOutputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopOutputDirectory);
        var layout = InstallationLayoutResolver.ResolveFromDesktopBase(
            desktopOutputDirectory);
        var directory = layout.FirewallDirectory;
        var script = Path.Combine(directory, ScriptFileName);
        var manifest = Path.Combine(directory, ManifestFileName);
        var hasScript = File.Exists(script);
        var hasManifest = File.Exists(manifest);
        var status = (hasScript, hasManifest) switch
        {
            (true, true) => FirewallAssetStatus.Available,
            (false, true) => FirewallAssetStatus.MissingScript,
            (true, false) => FirewallAssetStatus.MissingManifest,
            _ => FirewallAssetStatus.MissingBoth
        };
        return new FirewallDeploymentAssets(
            status,
            hasScript ? script : null,
            hasManifest ? manifest : null);
    }
}

public static class ManagedSunshineExecutableValidator
{
    public static ManagedSunshineExecutable Validate(string? authoritativePath)
    {
        if (string.IsNullOrWhiteSpace(authoritativePath))
            return new(ManagedSunshineStatus.Unavailable, null);

        string path;
        try
        {
            path = Path.GetFullPath(authoritativePath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(ManagedSunshineStatus.InvalidExecutable, null);
        }

        if (!string.Equals(
                Path.GetFileName(path),
                "sunshine.exe",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
        {
            return new(ManagedSunshineStatus.InvalidExecutable, null);
        }

        return new(ManagedSunshineStatus.Available, path);
    }
}
