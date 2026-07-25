using System.Security.Cryptography;
using System.Text.Json;

namespace Ligase.Host.Launcher;

public sealed record LauncherTarget(string ExecutablePath);

public static class LauncherManifestValidator
{
    public const string DesktopRelativePath = "Desktop/Ligase.Host.Desktop.exe";

    public static LauncherTarget Resolve(string launcherPath)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(launcherPath))
            ?? throw new InvalidDataException("launcherRootUnavailable");
        var manifestPath = Path.Combine(root, "ligase-install-manifest.json");
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(manifestPath),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
        RejectDuplicateProperties(document.RootElement);

        var rootElement = document.RootElement;
        RequireString(rootElement, "installLayout", "structured-v1");
        if (!rootElement.TryGetProperty("artifacts", out var artifacts) ||
            artifacts.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("launcherManifestInvalid");

        JsonElement? desktop = null;
        foreach (var artifact in artifacts.EnumerateArray())
        {
            if (artifact.ValueKind != JsonValueKind.Object ||
                !artifact.TryGetProperty("role", out var role) ||
                role.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("launcherManifestInvalid");
            if (!string.Equals(role.GetString(), "desktop", StringComparison.Ordinal))
                continue;
            if (desktop is not null)
                throw new InvalidDataException("launcherManifestInvalid");
            desktop = artifact;
        }
        if (desktop is null)
            throw new InvalidDataException("launcherManifestInvalid");

        var relative = ReadString(desktop.Value, "relativePath");
        if (!string.Equals(relative, DesktopRelativePath, StringComparison.Ordinal))
            throw new InvalidDataException("launcherManifestInvalid");
        var expectedHash = ReadString(desktop.Value, "signedArtifactSha256");
        if (expectedHash.Length != 64 ||
            expectedHash.Any(character =>
                character is not (>= '0' and <= '9') and
                    not (>= 'a' and <= 'f')))
            throw new InvalidDataException("launcherManifestInvalid");

        var executable = Path.GetFullPath(
            Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!executable.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(executable))
            throw new InvalidDataException("launcherDesktopUnavailable");
        var actualHash = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(executable))).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException("launcherDesktopHashMismatch");
        return new(executable);
    }

    private static void RequireString(
        JsonElement element,
        string name,
        string expected)
    {
        if (!string.Equals(ReadString(element, name), expected, StringComparison.Ordinal))
            throw new InvalidDataException("launcherManifestInvalid");
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("launcherManifestInvalid");
        return value.GetString()!;
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException("launcherManifestInvalid");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                RejectDuplicateProperties(item);
        }
    }
}
