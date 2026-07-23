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
            Id = Guid.Parse("78a25216-f239-45bd-b4aa-f41c814066e9"),
            Kind = LibraryItemKind.Desktop,
            Name = "监控桌面"
        };
        var virtualDesktop = new LibraryItem
        {
            Id = Guid.Parse("70b9f1d5-0cb7-438f-b3c7-18f1489f4be6"),
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
    public void PortFamilyContainsEveryApolloTransportOffset()
    {
        CollectionAssert.AreEqual(
            new[] { 48984, 48989, 48990, 48998, 48999, 49000, 49010 },
            ApolloPortAllocator.ExpandPortFamily(48989).ToArray());
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
