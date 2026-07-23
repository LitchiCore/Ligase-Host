using System.Globalization;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed class SteamLibraryService(ISteamInstallationLocator installationLocator) : ISteamLibraryService
{
    private static readonly HashSet<uint> NonLaunchableAppIds = [228980];

    public Task<IReadOnlyList<SteamGame>> DiscoverGamesAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SteamGame>>(() => DiscoverGames(cancellationToken), cancellationToken);

    private IReadOnlyList<SteamGame> DiscoverGames(CancellationToken cancellationToken)
    {
        var steamPath = installationLocator.FindSteamPath();
        if (steamPath is null) return [];

        var games = new Dictionary<uint, SteamGame>();
        foreach (var library in FindLibraryPaths(steamPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var steamAppsPath = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamAppsPath)) continue;

            foreach (var manifestPath in Directory.EnumerateFiles(steamAppsPath, "appmanifest_*.acf"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var state = VdfParser.Parse(File.ReadAllText(manifestPath)).GetObject("AppState");
                    if (state is null ||
                        !uint.TryParse(state.GetString("appid"), NumberStyles.None, CultureInfo.InvariantCulture, out var appId) ||
                        NonLaunchableAppIds.Contains(appId) ||
                        string.IsNullOrWhiteSpace(state.GetString("name")) ||
                        string.IsNullOrWhiteSpace(state.GetString("installdir")))
                    {
                        continue;
                    }

                    _ = ulong.TryParse(state.GetString("SizeOnDisk"), NumberStyles.None, CultureInfo.InvariantCulture, out var sizeOnDisk);
                    games[appId] = new SteamGame(
                        appId, state.GetString("name")!, state.GetString("installdir")!,
                        library, manifestPath, sizeOnDisk);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
                {
                    // A manifest can be replaced by Steam during a scan; keep scanning healthy entries.
                }
            }
        }

        return games.Values
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(game => game.AppId)
            .ToArray();
    }

    private static IReadOnlyList<string> FindLibraryPaths(string steamPath)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(steamPath) };
        var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFile)) return libraries.ToArray();

        try
        {
            var folders = VdfParser.Parse(File.ReadAllText(libraryFile)).GetObject("libraryfolders");
            if (folders is null) return libraries.ToArray();

            foreach (var (key, value) in folders)
            {
                if (!uint.TryParse(key, out _)) continue;
                var candidate = value switch
                {
                    string legacyPath => legacyPath,
                    VdfObject folder => folder.GetString("path"),
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(candidate)) libraries.Add(Path.GetFullPath(candidate));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            // The primary library remains usable if the optional index is unreadable.
        }

        return libraries.ToArray();
    }
}
