using System.Text.Json;
using System.Text.Json.Serialization;
using Ligase.Host.Core.Domain.Installation;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed class ApolloAppsWriter : IApolloAppsWriter
{
    private readonly LigasePaths _paths;
    private readonly InstallationLayout _installationLayout;

    public ApolloAppsWriter(LigasePaths paths)
        : this(
            paths,
            InstallationLayoutResolver.ResolveFromDesktopBase(
                AppContext.BaseDirectory))
    {
    }

    public ApolloAppsWriter(
        LigasePaths paths,
        InstallationLayout installationLayout)
    {
        _paths = paths;
        _installationLayout = installationLayout;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task WriteAsync(
        IReadOnlyCollection<LibraryItem> items,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.ApolloDirectory);
        var apps = items
            // Apollo injects its own Virtual Display entry when its driver is available.
            .Where(item => item.Kind != LibraryItemKind.VirtualDesktop)
            .Select(CreateApp)
            .ToList();

        var document = new
        {
            version = 2,
            env = new { },
            apps
        };
        var json = JsonSerializer.Serialize(document, JsonOptions)
            .Replace("\"imagePath\":", "\"image-path\":")
            .Replace("\"allowClientCommands\":", "\"allow-client-commands\":")
            .Replace("\"virtualDisplay\":", "\"virtual-display\":")
            .Replace("\"workingDir\":", "\"working-dir\":")
            .Replace("\"waitAll\":", "\"wait-all\":")
            .Replace("\"autoDetach\":", "\"auto-detach\":");

        var temporaryPath = _paths.ApolloAppsFile + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _paths.ApolloAppsFile, true);
    }

    private object CreateApp(LibraryItem item)
    {
        if (item.Kind == LibraryItemKind.Desktop)
        {
            return new
            {
                uuid = item.Id.ToString(),
                name = item.Name,
                imagePath = item.CoverImagePath ?? "desktop.png",
                virtualDisplay = false,
                allowClientCommands = false
            };
        }

        if (item.Kind == LibraryItemKind.Steam)
        {
            var watcher = FindWatcherExecutable();
            var command = $"{Quote(watcher)} --steam-app-id {item.SteamAppId} --install-path {Quote(item.SteamInstallPath!)}";
            return new
            {
                uuid = item.Id.ToString(),
                name = item.Name,
                cmd = command,
                imagePath = item.CoverImagePath,
                workingDir = Path.GetDirectoryName(watcher),
                waitAll = true,
                autoDetach = false
            };
        }

        var arguments = string.IsNullOrWhiteSpace(item.Arguments) ? string.Empty : $" {item.Arguments}";
        return new
        {
            uuid = item.Id.ToString(),
            name = item.Name,
            cmd = $"{Quote(item.ExecutablePath!)}{arguments}",
            imagePath = item.CoverImagePath,
            workingDir = item.WorkingDirectory,
            waitAll = true,
            autoDetach = false
        };
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private string FindWatcherExecutable()
    {
        var candidates = new[]
        {
            _installationLayout.GameWatcherExecutable,
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "..",
                "tools", "Ligase.GameWatcher", "bin", "Debug",
                "net8.0-windows10.0.19041.0", "Ligase.GameWatcher.exe"))
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
