using System.Text.Json;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class ApplicationLibraryTests
{
    private string _temporaryDirectory = null!;

    [TestInitialize]
    public void SetUp()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "Ligase.Host.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, true);
        }
    }

    [TestMethod]
    public async Task SteamItemAndSortModePersistAcrossRepositoryInstances()
    {
        var paths = new LigasePaths(_temporaryDirectory);
        var writer = new RecordingAppsWriter();
        var library = new ApplicationLibrary(paths, writer);
        var game = new SteamGame(
            1091500,
            "Cyberpunk 2077",
            "Cyberpunk 2077",
            @"D:\Steam",
            @"D:\Steam\steamapps\appmanifest_1091500.acf",
            1);

        var first = await library.AddSteamAsync(game);
        var duplicate = await library.AddSteamAsync(game);
        await library.SetSortModeAsync(LibrarySortMode.AddedNewest);

        var reloaded = await new ApplicationLibrary(paths, writer).LoadAsync();
        Assert.AreEqual(first.Id, duplicate.Id);
        Assert.AreEqual(1, reloaded.Items.Count(item => item.Kind == LibraryItemKind.Steam));
        Assert.AreEqual(1, reloaded.Items.Count(item => item.Kind == LibraryItemKind.Desktop));
        Assert.AreEqual(1, reloaded.Items.Count(item => item.Kind == LibraryItemKind.VirtualDesktop));
        Assert.IsTrue(reloaded.Items.Any(item =>
            item.Kind == LibraryItemKind.Desktop &&
            item.Id == SystemLibraryIds.Desktop));
        Assert.IsTrue(reloaded.Items.Any(item =>
            item.Kind == LibraryItemKind.VirtualDesktop &&
            item.Id == SystemLibraryIds.VirtualDesktop));
        Assert.AreEqual(LibrarySortMode.AddedNewest, reloaded.SortMode);
        Assert.IsTrue(reloaded.Revision >= 3);
        Assert.AreEqual(3, writer.WriteCount);
    }

    [TestMethod]
    public async Task ExecutableItemUsesExecutableDirectoryByDefault()
    {
        var executable = Path.Combine(_temporaryDirectory, "Sample.exe");
        await File.WriteAllBytesAsync(executable, []);
        var library = new ApplicationLibrary(
            new LigasePaths(_temporaryDirectory),
            new RecordingAppsWriter());

        var item = await library.AddExecutableAsync("Sample", executable, "--demo", null);

        Assert.AreEqual(LibraryItemKind.Executable, item.Kind);
        Assert.AreEqual(Path.GetDirectoryName(executable), item.WorkingDirectory);
        Assert.AreEqual("--demo", item.Arguments);
    }

    [TestMethod]
    public async Task ApolloWriterProducesVersionTwoTrackedCommands()
    {
        var paths = new LigasePaths(_temporaryDirectory);
        var writer = new ApolloAppsWriter(paths);
        var item = new LibraryItem
        {
            Kind = LibraryItemKind.Steam,
            Name = "Test Game",
            SteamAppId = 42,
            SteamInstallPath = @"D:\Steam\steamapps\common\Test"
        };

        await writer.WriteAsync([item]);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(paths.ApolloAppsFile));
        Assert.AreEqual(2, json.RootElement.GetProperty("version").GetInt32());
        var app = json.RootElement.GetProperty("apps")[0];
        Assert.AreEqual(item.Id.ToString(), app.GetProperty("uuid").GetString());
        StringAssert.Contains(app.GetProperty("cmd").GetString(), "--steam-app-id 42");
        Assert.IsFalse(app.GetProperty("auto-detach").GetBoolean());
    }

    [TestMethod]
    public async Task ApolloWriterUsesDesktopEntryAndLeavesVirtualDisplayToApollo()
    {
        var paths = new LigasePaths(_temporaryDirectory);
        var writer = new ApolloAppsWriter(paths);
        var desktop = new LibraryItem
        {
            Id = SystemLibraryIds.Desktop,
            Kind = LibraryItemKind.Desktop,
            Name = "监控桌面"
        };
        var virtualDesktop = new LibraryItem
        {
            Id = SystemLibraryIds.VirtualDesktop,
            Kind = LibraryItemKind.VirtualDesktop,
            Name = "虚拟桌面"
        };

        await writer.WriteAsync([desktop, virtualDesktop]);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(paths.ApolloAppsFile));
        var apps = json.RootElement.GetProperty("apps");
        Assert.AreEqual(1, apps.GetArrayLength());
        Assert.AreEqual("监控桌面", apps[0].GetProperty("name").GetString());
        Assert.IsFalse(apps[0].GetProperty("virtual-display").GetBoolean());
    }

    [TestMethod]
    public async Task LegacyVirtualDesktopIdMigratesToApolloUuid()
    {
        var paths = new LigasePaths(_temporaryDirectory);
        var legacyState = new LibraryState
        {
            Items =
            [
                new LibraryItem
                {
                    Id = Guid.Parse("70B9F1D5-0CB7-438F-B3C7-18F1489F4BE6"),
                    Kind = LibraryItemKind.VirtualDesktop,
                    Name = "虚拟桌面"
                }
            ]
        };
        await File.WriteAllTextAsync(
            paths.LibraryFile,
            JsonSerializer.Serialize(legacyState, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

        var reloaded = await new ApplicationLibrary(
            paths,
            new RecordingAppsWriter()).LoadAsync();

        Assert.AreEqual(
            SystemLibraryIds.VirtualDesktop,
            reloaded.Items.Single(item => item.Kind == LibraryItemKind.VirtualDesktop).Id);
    }

    [TestMethod]
    public void PortFamilyContainsEveryApolloTransportOffset()
    {
        CollectionAssert.AreEqual(
            new[] { 48984, 48989, 48990, 48998, 48999, 49000, 49010 },
            ApolloPortAllocator.ExpandPortFamily(48989).ToArray());
    }

    [TestMethod]
    public void StartupCommandQuotesExecutableAndStartsMinimized()
    {
        Assert.AreEqual(
            "\"C:\\Program Files\\Ligase Host\\Ligase.Host.Desktop.exe\" --minimized",
            HostPreferencesService.BuildStartupCommand(
                @"C:\Program Files\Ligase Host\Ligase.Host.Desktop.exe"));
    }

    [TestMethod]
    public async Task LanguagePreferencePersistsAndMapsToWinUiLanguageTag()
    {
        var paths = new LigasePaths(_temporaryDirectory);
        var preferences = new HostPreferencesService(paths);

        await preferences.SetLanguageAsync(HostLanguage.English);
        var reloaded = new HostPreferencesService(paths);
        await reloaded.InitializeAsync();

        Assert.AreEqual(HostLanguage.English, reloaded.Current.Language);
        Assert.AreEqual(
            "en-US",
            HostPreferencesService.GetPrimaryLanguageOverride(reloaded.Current.Language));
        Assert.AreEqual(
            string.Empty,
            HostPreferencesService.GetPrimaryLanguageOverride(HostLanguage.System));
    }

    [TestMethod]
    public async Task AppResolutionOverridesGlobalAndCanReturnToGlobal()
    {
        var paths = new LigasePaths(_temporaryDirectory);
        var service = new StreamingSettingsService(paths);
        var appId = Guid.NewGuid();

        var global = await service.SetGlobalResolutionAsync(new StreamResolution(2560, 1440));
        var overridden = await service.SetAppResolutionAsync(
            appId,
            new StreamResolution(1280, 720),
            global.Revision);

        Assert.AreEqual(new StreamResolution(1280, 720), overridden.GetEffectiveResolution(appId));

        var inherited = await service.SetAppResolutionAsync(
            appId,
            null,
            overridden.Revision);

        Assert.AreEqual(new StreamResolution(2560, 1440), inherited.GetEffectiveResolution(appId));
        Assert.IsFalse(inherited.Apps.ContainsKey(appId));
    }

    [TestMethod]
    public async Task StreamingSettingsRejectStaleRevision()
    {
        var paths = new LigasePaths(_temporaryDirectory);
        var service = new StreamingSettingsService(paths);
        var first = await service.SetGlobalResolutionAsync(new StreamResolution(1920, 1200));

        var exception = await Assert.ThrowsExceptionAsync<RevisionConflictException>(() =>
            service.SetGlobalResolutionAsync(
                new StreamResolution(3840, 2160),
                first.Revision - 1));

        Assert.AreEqual(first.Revision - 1, exception.ExpectedRevision);
        Assert.AreEqual(first.Revision, exception.ActualRevision);
    }

    [TestMethod]
    public async Task SyncDocumentContainsSafeLibraryMetadataAndStreamingSettings()
    {
        var paths = new LigasePaths(_temporaryDirectory);
        var writer = new LigaseSyncDocumentWriter(paths);
        var appId = Guid.NewGuid();
        var library = new LibraryState
        {
            Revision = 7,
            SortMode = LibrarySortMode.LastPlayedNewest,
            Items =
            [
                new LibraryItem
                {
                    Id = appId,
                    Kind = LibraryItemKind.Executable,
                    Name = "Private Tool",
                    ExecutablePath = @"C:\Secret\Tool.exe",
                    WorkingDirectory = @"C:\Secret"
                }
            ]
        };
        var streaming = new StreamingSettingsState
        {
            Revision = 3,
            GlobalResolution = new StreamResolution(2560, 1440),
            Apps =
            {
                [appId] = new AppStreamingSettings
                {
                    Resolution = new StreamResolution(1920, 1080)
                }
            }
        };

        await writer.WriteAsync(library, streaming);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(paths.SyncFile));
        var root = json.RootElement;
        Assert.AreEqual(7, root.GetProperty("library").GetProperty("revision").GetInt64());
        Assert.AreEqual(
            "lastPlayedNewest",
            root.GetProperty("library").GetProperty("sortMode").GetString());
        Assert.AreEqual(
            1920,
            root.GetProperty("streaming").GetProperty("apps")
                .GetProperty(appId.ToString()).GetProperty("resolution")
                .GetProperty("width").GetInt32());
        var serialized = root.GetRawText();
        Assert.IsFalse(serialized.Contains("executablePath", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains(@"C:\Secret", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ApolloDeviceSnapshotMapsCoreFieldNames()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "devices": [{
                "name": "V2353A",
                "uuid": "device-uuid",
                "display_mode": "2560x1440x120",
                "perm": 119480064,
                "allow_client_commands": true,
                "always_use_virtual_display": false,
                "connected": true
              }]
            }
            """;

        var device = ApolloDeviceService.DeserializeSnapshot(json).Devices.Single();

        Assert.AreEqual("V2353A", device.Name);
        Assert.AreEqual("2560x1440x120", device.DisplayMode);
        Assert.AreEqual(119480064u, device.Permissions);
        Assert.IsTrue(device.AllowClientCommands);
        Assert.IsTrue(device.Connected);
    }

    private sealed class RecordingAppsWriter : IApolloAppsWriter
    {
        public int WriteCount { get; private set; }

        public Task WriteAsync(
            IReadOnlyCollection<LibraryItem> items,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            return Task.CompletedTask;
        }
    }
}
