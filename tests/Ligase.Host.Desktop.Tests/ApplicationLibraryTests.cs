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
        var beforeOrder = await library.LoadAsync();
        await library.SetManualOrderAsync(
            beforeOrder.Revision,
            [SystemLibraryIds.Desktop, first.Id, SystemLibraryIds.VirtualDesktop]);

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
        Assert.AreEqual(LibrarySortMode.Manual, reloaded.SortMode);
        Assert.IsTrue(reloaded.Revision >= 3);
        Assert.AreEqual(3, writer.WriteCount);
    }

    [TestMethod]
    public async Task ManualOrderPersistsCanonicalOrderAndSyncProjection()
    {
        var paths = new LigasePaths(_temporaryDirectory);
        var library = new ApplicationLibrary(
            paths,
            new RecordingAppsWriter(),
            new StreamingSettingsService(paths),
            new LigaseSyncDocumentWriter(paths));
        var zulu = await library.AddSteamAsync(new SteamGame(
            2, "Zulu", "Zulu", @"D:\Steam\Zulu", "z.acf", 1));
        var alpha = await library.AddSteamAsync(new SteamGame(
            1, "Alpha", "Alpha", @"D:\Steam\Alpha", "a.acf", 1));

        var before = await library.LoadAsync();
        var ordered = await library.SetManualOrderAsync(
            before.Revision,
            [zulu.Id, SystemLibraryIds.VirtualDesktop, alpha.Id, SystemLibraryIds.Desktop]);

        CollectionAssert.AreEqual(
            new[]
            {
                zulu.Id,
                SystemLibraryIds.VirtualDesktop,
                alpha.Id,
                SystemLibraryIds.Desktop
            },
            ordered.Items.Select(item => item.Id).ToArray());
        using var sync = JsonDocument.Parse(await File.ReadAllTextAsync(paths.SyncFile));
        Assert.AreEqual("manual",
            sync.RootElement.GetProperty("library").GetProperty("sortMode").GetString());
        CollectionAssert.AreEqual(
            ordered.Items.Select(item => item.Id).ToArray(),
            sync.RootElement.GetProperty("library").GetProperty("items")
                .EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray());
    }

    [TestMethod]
    public async Task ManualOrderRejectsStaleIncompleteDuplicateUnknownAndUnsafeRevision()
    {
        var paths = new LigasePaths(_temporaryDirectory);
        var library = new ApplicationLibrary(
            paths,
            new RecordingAppsWriter(),
            new StreamingSettingsService(paths),
            new LigaseSyncDocumentWriter(paths));
        var alpha = await library.AddSteamAsync(new SteamGame(
            1, "Alpha", "Alpha", @"D:\Steam\Alpha", "a.acf", 1));
        var zulu = await library.AddSteamAsync(new SteamGame(
            2, "Zulu", "Zulu", @"D:\Steam\Zulu", "z.acf", 1));

        var before = await library.LoadAsync();
        var complete =
            new[] { SystemLibraryIds.Desktop, SystemLibraryIds.VirtualDesktop, alpha.Id, zulu.Id };
        await Assert.ThrowsExceptionAsync<LibraryRevisionConflictException>(
            () => library.SetManualOrderAsync(before.Revision - 1, complete));
        await Assert.ThrowsExceptionAsync<InvalidManualLibraryOrderException>(
            () => library.SetManualOrderAsync(before.Revision, [alpha.Id]));
        await Assert.ThrowsExceptionAsync<InvalidManualLibraryOrderException>(
            () => library.SetManualOrderAsync(
                before.Revision,
                [SystemLibraryIds.Desktop, SystemLibraryIds.VirtualDesktop, alpha.Id, alpha.Id]));
        await Assert.ThrowsExceptionAsync<InvalidManualLibraryOrderException>(
            () => library.SetManualOrderAsync(
                before.Revision,
                [SystemLibraryIds.Desktop, SystemLibraryIds.VirtualDesktop, alpha.Id, Guid.NewGuid()]));
        await Assert.ThrowsExceptionAsync<InvalidLibraryRevisionException>(
            () => library.SetManualOrderAsync(0, complete));
        await Assert.ThrowsExceptionAsync<InvalidLibraryRevisionException>(
            () => library.SetManualOrderAsync(
                9_007_199_254_740_992, complete));
    }

    [TestMethod]
    public async Task HiddenAndNewItemsFollowFrozenManualAppendRules()
    {
        var paths = new LigasePaths(_temporaryDirectory);
        var library = new ApplicationLibrary(
            paths,
            new RecordingAppsWriter(),
            new StreamingSettingsService(paths),
            new LigaseSyncDocumentWriter(paths));
        var alpha = await library.AddSteamAsync(new SteamGame(
            1, "Alpha", "Alpha", @"D:\Steam\Alpha", "a.acf", 1));
        var beta = await library.AddSteamAsync(new SteamGame(
            2, "Beta", "Beta", @"D:\Steam\Beta", "b.acf", 1));
        var gamma = await library.AddSteamAsync(new SteamGame(
            3, "Gamma", "Gamma", @"D:\Steam\Gamma", "g.acf", 1));

        var initial = await library.LoadAsync();
        await library.SetManualOrderAsync(
            initial.Revision,
            [gamma.Id, SystemLibraryIds.VirtualDesktop, beta.Id, alpha.Id, SystemLibraryIds.Desktop]);
        await library.SetPublishedToClientsAsync(beta.Id, false);

        var hidden = await library.LoadAsync();
        CollectionAssert.AreEqual(
            new[] { gamma.Id, SystemLibraryIds.VirtualDesktop, alpha.Id, SystemLibraryIds.Desktop },
            hidden.Items.Where(item => item.PublishedToClients).Select(item => item.Id).ToArray());
        Assert.AreEqual(beta.Id, hidden.Items.Last().Id);

        var delta = await library.AddSteamAsync(new SteamGame(
            4, "Delta", "Delta", @"D:\Steam\Delta", "d.acf", 1));
        var added = await library.LoadAsync();
        CollectionAssert.AreEqual(
            new[]
            {
                gamma.Id,
                SystemLibraryIds.VirtualDesktop,
                alpha.Id,
                SystemLibraryIds.Desktop,
                delta.Id
            },
            added.Items.Where(item => item.PublishedToClients).Select(item => item.Id).ToArray());
        Assert.AreEqual(beta.Id, added.Items.Last().Id);

        await library.SetPublishedToClientsAsync(beta.Id, true);
        var republished = await library.LoadAsync();
        CollectionAssert.AreEqual(
            new[]
            {
                gamma.Id,
                SystemLibraryIds.VirtualDesktop,
                alpha.Id,
                SystemLibraryIds.Desktop,
                delta.Id,
                beta.Id
            },
            republished.Items.Where(item => item.PublishedToClients).Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public async Task LegacyManualOrderPreservesSystemPositionsAndMovesHiddenItemsAfterPublished()
    {
        var paths = new LigasePaths(_temporaryDirectory);
        var visibleId = Guid.NewGuid();
        var hiddenId = Guid.NewGuid();
        await File.WriteAllTextAsync(paths.LibraryFile, $$"""
            {
              "schemaVersion": 1,
              "revision": 7,
              "sortMode": 0,
              "items": [
                { "id": "{{visibleId}}", "kind": 3, "name": "Visible" },
                { "id": "{{SystemLibraryIds.VirtualDesktop}}", "kind": 1, "name": "Virtual desktop" },
                { "id": "{{hiddenId}}", "kind": 3, "name": "Hidden", "publishedToClients": false },
                { "id": "{{SystemLibraryIds.Desktop}}", "kind": 0, "name": "Desktop" }
              ]
            }
            """);

        var state = await new ApplicationLibrary(paths, new RecordingAppsWriter()).LoadAsync();

        CollectionAssert.AreEqual(
            new[]
            {
                visibleId,
                SystemLibraryIds.VirtualDesktop,
                SystemLibraryIds.Desktop,
                hiddenId
            },
            state.Items.Select(item => item.Id).ToArray());
        Assert.IsTrue(state.Items.Single(item => item.Id == SystemLibraryIds.Desktop).CanManuallyOrder);
        Assert.IsTrue(state.Items.Single(item => item.Id == SystemLibraryIds.VirtualDesktop).CanManuallyOrder);
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
    public async Task MissingPublicationFieldDefaultsToPublished()
    {
        var paths = new LigasePaths(_temporaryDirectory);
        var appId = Guid.NewGuid();
        await File.WriteAllTextAsync(paths.LibraryFile, $$"""
            {
              "schemaVersion": 1,
              "revision": 1,
              "items": [{
                "id": "{{appId}}",
                "kind": 3,
                "name": "Legacy app"
              }]
            }
            """);

        var state = await new ApplicationLibrary(paths, new RecordingAppsWriter()).LoadAsync();

        Assert.IsTrue(state.Items.Single(item => item.Id == appId).PublishedToClients);
        CollectionAssert.AreEqual(
            new[] { appId, SystemLibraryIds.Desktop, SystemLibraryIds.VirtualDesktop },
            state.Items.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public async Task PublicationPersistsAndAppearsInSyncProjection()
    {
        var paths = new LigasePaths(_temporaryDirectory);
        var syncWriter = new LigaseSyncDocumentWriter(paths);
        var executable = Path.Combine(_temporaryDirectory, "Published.exe");
        await File.WriteAllBytesAsync(executable, []);
        var library = new ApplicationLibrary(
            paths,
            new RecordingAppsWriter(),
            new StreamingSettingsService(paths),
            syncWriter);
        var item = await library.AddExecutableAsync("Published", executable, null, null);

        await library.SetPublishedToClientsAsync(item.Id, false);

        var reloaded = await new ApplicationLibrary(paths, new RecordingAppsWriter()).LoadAsync();
        Assert.IsFalse(reloaded.Items.Single(candidate => candidate.Id == item.Id).PublishedToClients);
        using var sync = JsonDocument.Parse(await File.ReadAllTextAsync(paths.SyncFile));
        var projected = sync.RootElement.GetProperty("library").GetProperty("items")
            .EnumerateArray().Single(candidate => candidate.GetProperty("id").GetGuid() == item.Id);
        Assert.IsFalse(projected.GetProperty("publishedToClients").GetBoolean());
    }

    [TestMethod]
    public async Task SystemEntriesCannotBeHiddenOrDeleted()
    {
        var library = new ApplicationLibrary(
            new LigasePaths(_temporaryDirectory),
            new RecordingAppsWriter());
        await library.LoadAsync();

        await Assert.ThrowsExceptionAsync<SystemLibraryItemMutationException>(
            () => library.SetPublishedToClientsAsync(SystemLibraryIds.Desktop, false));
        await Assert.ThrowsExceptionAsync<SystemLibraryItemMutationException>(
            () => library.RemoveAsync(SystemLibraryIds.VirtualDesktop));
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

    [TestMethod]
    public void CoreLocatorRecognizesOnlyLigasePortFamilies()
    {
        Assert.IsTrue(ApolloCoreLocator.IsLigaseBasePortCandidate(48989));
        Assert.IsTrue(ApolloCoreLocator.IsLigaseBasePortCandidate(49989));
        Assert.IsFalse(ApolloCoreLocator.IsLigaseBasePortCandidate(47989));
        Assert.IsFalse(ApolloCoreLocator.IsLigaseBasePortCandidate(49990));
    }

    [TestMethod]
    public void CoreLocatorRequiresLigaseCapabilityAndIdentity()
    {
        const string ligase = """
            <root>
              <hostname>Ligase Host Parallel</hostname>
              <uniqueid>host-uuid</uniqueid>
              <LigaseSyncVersion>1</LigaseSyncVersion>
            </root>
            """;
        const string apollo = """
            <root><hostname>Apollo</hostname><uniqueid>old</uniqueid></root>
            """;

        var loopback = LigaseEndpoint.Create(
            LigaseEndpointScheme.Http,
            "::1",
            49989,
            source: LigaseEndpointSource.Loopback);
        var endpoint = ApolloCoreLocator.ParseServerInfo(loopback, ligase);

        Assert.IsNotNull(endpoint);
        Assert.AreEqual((ushort)49989, endpoint.BasePort);
        Assert.AreEqual("::1", endpoint.Endpoint.Host);
        Assert.AreEqual("host-uuid", endpoint.UniqueId);
        Assert.IsNull(ApolloCoreLocator.ParseServerInfo(loopback, apollo));
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
