using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class SteamLibraryServiceTests
{
    private string _testRoot = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "LigaseHostTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "LigaseHostTests"));
        var resolvedRoot = Path.GetFullPath(_testRoot);
        if (resolvedRoot.StartsWith(expectedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(resolvedRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task DiscoverGamesAsync_ReadsPrimaryAndAdditionalLibraries()
    {
        var primary = CreateSteamLibrary("Steam");
        var secondary = CreateSteamLibrary("SteamLibrary");
        File.WriteAllText(
            Path.Combine(primary, "steamapps", "libraryfolders.vdf"),
            $$"""
            "libraryfolders"
            {
                "0" { "path" "{{EscapePath(primary)}}" }
                "1" { "path" "{{EscapePath(secondary)}}" }
            }
            """);
        WriteManifest(primary, 20, "Zulu", "Zulu", 2048);
        WriteManifest(secondary, 10, "Alpha", "Alpha", 1073741824);

        var games = await new SteamLibraryService(new FixedLocator(primary)).DiscoverGamesAsync();

        Assert.AreEqual(2, games.Count);
        Assert.AreEqual("Alpha", games[0].Name);
        Assert.AreEqual((uint)10, games[0].AppId);
        Assert.AreEqual("steam://rungameid/10", games[0].LaunchUri);
        Assert.AreEqual(Path.Combine(secondary, "steamapps", "common", "Alpha"), games[0].InstallPath);
    }

    [TestMethod]
    public async Task DiscoverGamesAsync_SkipsCorruptManifestAndDeduplicatesByAppId()
    {
        var primary = CreateSteamLibrary("Steam");
        WriteManifest(primary, 10, "First name", "First", 100);
        WriteManifest(primary, 10, "Replacement", "Replacement", 200, "appmanifest_duplicate.acf");
        File.WriteAllText(Path.Combine(primary, "steamapps", "appmanifest_broken.acf"), "\"AppState\" {");

        var games = await new SteamLibraryService(new FixedLocator(primary)).DiscoverGamesAsync();

        Assert.AreEqual(1, games.Count);
        Assert.AreEqual((uint)10, games[0].AppId);
    }

    [TestMethod]
    public async Task DiscoverGamesAsync_ExcludesSteamRuntimeComponents()
    {
        var primary = CreateSteamLibrary("Steam");
        WriteManifest(primary, 228980, "Steamworks Common Redistributables", "Steamworks Shared", 100);
        WriteManifest(primary, 10, "A game", "A game", 200);

        var games = await new SteamLibraryService(new FixedLocator(primary)).DiscoverGamesAsync();

        Assert.AreEqual(1, games.Count);
        Assert.AreEqual((uint)10, games[0].AppId);
    }

    private string CreateSteamLibrary(string name)
    {
        var path = Path.Combine(_testRoot, name);
        Directory.CreateDirectory(Path.Combine(path, "steamapps", "common"));
        return path;
    }

    private static void WriteManifest(
        string library,
        uint appId,
        string name,
        string installDirectory,
        ulong size,
        string? fileName = null)
    {
        File.WriteAllText(
            Path.Combine(library, "steamapps", fileName ?? $"appmanifest_{appId}.acf"),
            $$"""
            "AppState"
            {
                "appid" "{{appId}}"
                "name" "{{name}}"
                "installdir" "{{installDirectory}}"
                "SizeOnDisk" "{{size}}"
            }
            """);
    }

    private static string EscapePath(string path) => path.Replace(@"\", @"\\");

    private sealed class FixedLocator(string path) : ISteamInstallationLocator
    {
        public string? FindSteamPath() => path;
    }
}
