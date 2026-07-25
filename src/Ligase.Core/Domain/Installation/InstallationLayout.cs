using System.Text.Json;

namespace Ligase.Host.Core.Domain.Installation;

public enum InstallationLayoutKind
{
    Structured,
    LegacyFlat,
    Development
}

public sealed record InstallationLayout(
    InstallationLayoutKind Kind,
    string RootDirectory,
    string DesktopDirectory,
    string DesktopExecutable,
    string CoreDirectory,
    string CoreExecutable,
    string CoreAssetsDirectory,
    string GameWatcherExecutable,
    string DeploymentDirectory,
    string FirewallDirectory,
    string DriversDirectory,
    string BootstrapPath,
    string ManifestPath);

public static class InstallationLayoutResolver
{
    public static InstallationLayout ResolveFromDesktopBase(string desktopBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopBaseDirectory);
        var desktop = Path.GetFullPath(desktopBaseDirectory);
        var parent = Directory.GetParent(
            desktop.TrimEnd(Path.DirectorySeparatorChar))?.FullName;
        var looksStructured = string.Equals(
            Path.GetFileName(desktop.TrimEnd(Path.DirectorySeparatorChar)),
            "Desktop",
            StringComparison.OrdinalIgnoreCase);
        if (parent is not null &&
            IsManifest(parent, expectedLayout: "structured-v1"))
        {
            return CreateStructured(parent, desktop);
        }
        if (looksStructured &&
            parent is not null)
        {
            if (File.Exists(Path.Combine(parent, "ligase-install-manifest.json")))
                throw new InvalidDataException("structuredInstallManifestInvalid");
            throw new InvalidDataException("structuredInstallManifestMissing");
        }
        if (IsManifest(desktop, expectedLayout: null))
            return CreateLegacy(desktop);
        return CreateDevelopment(desktop);
    }

    private static bool IsManifest(string root, string? expectedLayout)
    {
        var path = Path.Combine(root, "ligase-install-manifest.json");
        if (!File.Exists(path))
            return false;
        try
        {
            var bytes = File.ReadAllBytes(path);
            var preambleLength =
                bytes.Length >= 3 &&
                bytes[0] == 0xef &&
                bytes[1] == 0xbb &&
                bytes[2] == 0xbf
                    ? 3
                    : 0;
            var json = bytes.AsMemory(preambleLength);
            using var document = JsonDocument.Parse(json);
            var value = document.RootElement;
            var properties = value.ValueKind == JsonValueKind.Object
                ? value.EnumerateObject().Select(property => property.Name).ToArray()
                : [];
            if (value.ValueKind != JsonValueKind.Object ||
                properties.Length != properties.Distinct(
                    StringComparer.Ordinal).Count() ||
                value.GetProperty("schemaVersion").GetInt32() != 1 ||
                value.GetProperty("installMode").GetString() != "packaged")
                return false;
            if (expectedLayout is null)
                return !value.TryGetProperty("installLayout", out _);
            return value.TryGetProperty("installLayout", out var layout) &&
                layout.GetString() == expectedLayout;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidOperationException
                or KeyNotFoundException)
        {
            return false;
        }
    }

    private static InstallationLayout CreateStructured(string root, string desktop)
    {
        root = Path.GetFullPath(root);
        desktop = RequireChild(root, desktop);
        if (!string.Equals(
                Path.GetFileName(desktop.TrimEnd(Path.DirectorySeparatorChar)),
                "Desktop",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("structuredDesktopDirectoryInvalid");
        return new(
            InstallationLayoutKind.Structured,
            root,
            desktop,
            RequireChild(root, Path.Combine(desktop, "Ligase.Host.Desktop.exe")),
            RequireChild(root, Path.Combine(root, "Core")),
            RequireChild(root, Path.Combine(root, "Core", "sunshine.exe")),
            RequireChild(root, Path.Combine(root, "Core", "assets")),
            RequireChild(root, Path.Combine(
                root, "Tools", "GameWatcher", "Ligase.GameWatcher.exe")),
            RequireChild(root, Path.Combine(root, "Deployment")),
            RequireChild(root, Path.Combine(root, "Deployment", "Firewall")),
            RequireChild(root, Path.Combine(root, "Deployment", "Drivers")),
            RequireChild(root, Path.Combine(root, "ligase-bootstrap.json")),
            RequireChild(root, Path.Combine(root, "ligase-install-manifest.json")));
    }

    private static InstallationLayout CreateLegacy(string root)
    {
        root = Path.GetFullPath(root);
        return new(
            InstallationLayoutKind.LegacyFlat,
            root,
            root,
            Path.Combine(root, "Ligase.Host.Desktop.exe"),
            Path.Combine(root, "Apollo"),
            Path.Combine(root, "Apollo", "sunshine.exe"),
            Path.Combine(root, "Apollo", "assets"),
            Path.Combine(root, "Ligase.GameWatcher.exe"),
            root,
            Path.Combine(root, "Firewall"),
            Path.Combine(root, "Drivers"),
            Path.Combine(root, "ligase-bootstrap.json"),
            Path.Combine(root, "ligase-install-manifest.json"));
    }

    private static InstallationLayout CreateDevelopment(string desktop)
    {
        desktop = Path.GetFullPath(desktop);
        return new(
            InstallationLayoutKind.Development,
            desktop,
            desktop,
            Path.Combine(desktop, "Ligase.Host.Desktop.exe"),
            Path.Combine(desktop, "Apollo"),
            Path.Combine(desktop, "Apollo", "sunshine.exe"),
            Path.Combine(desktop, "Apollo", "assets"),
            Path.Combine(desktop, "Ligase.GameWatcher.exe"),
            desktop,
            Path.Combine(desktop, "Firewall"),
            Path.Combine(desktop, "Drivers"),
            Path.Combine(desktop, "ligase-bootstrap.json"),
            Path.Combine(desktop, "ligase-install-manifest.json"));
    }

    private static string RequireChild(string root, string value)
    {
        var canonicalRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var canonical = Path.GetFullPath(value);
        if (!canonical.StartsWith(
                canonicalRoot,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("installationLayoutEscapesRoot");
        return canonical;
    }
}
