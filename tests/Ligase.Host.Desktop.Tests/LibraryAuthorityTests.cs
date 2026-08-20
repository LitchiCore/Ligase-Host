using Ligase.Host.Core.Application.LayoutCatalog;
using Ligase.Host.Core.Domain.LayoutCatalog;
using Ligase.Host.Core.Infrastructure.Storage;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class LibraryAuthorityTests
{
    private string _root = null!;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "Ligase.Authority.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public void ExplicitRedirectedLocalAppDataDoesNotBecomeImplicitAuthority()
    {
        var redirected = Path.Combine(_root, "Packages", "OpenAI.Codex", "LocalCache", "Local");
        var paths = LigasePaths.CreateFromCommandLine(["Ligase.Host.Desktop.exe"], redirected);

        Assert.AreEqual(
            Path.GetFullPath(Path.Combine(redirected, "Ligase Host")),
            paths.RootDirectory);
        Assert.IsFalse(File.Exists(paths.AuthorityFile));
    }

    [TestMethod]
    public void ExplicitDataRootOverridesRedirectedPackageLocalAppData()
    {
        var redirected = Path.Combine(_root, "Packages", "OpenAI.Codex", "LocalCache", "Local");
        var managedRoot = Path.Combine(_root, "managed");

        var paths = LigasePaths.CreateFromCommandLine(
            ["Ligase.Host.Desktop.exe", "--data-root", managedRoot, "--minimized"],
            redirected);

        Assert.AreEqual(Path.GetFullPath(managedRoot), paths.RootDirectory);
        Assert.IsFalse(paths.RootDirectory.StartsWith(redirected, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ProcessEnvironmentDataRootOverridesRedirectedPackageLocalAppData()
    {
        var redirected = Path.Combine(_root, "Packages", "OpenAI.Codex", "LocalCache", "Local");
        var managedRoot = Path.Combine(_root, "managed-from-environment");

        var paths = LigasePaths.CreateFromCommandLine(
            ["Ligase.Host.Desktop.exe"],
            redirected,
            managedRoot);

        Assert.AreEqual(Path.GetFullPath(managedRoot), paths.RootDirectory);
    }

    [TestMethod]
    public void BootstrapFileProvidesExplicitDataRootForDirectExeLaunch()
    {
        var redirected = Path.Combine(_root, "Packages", "OpenAI.Codex", "LocalCache", "Local");
        var managedRoot = Path.Combine(_root, "managed-from-bootstrap");
        var bootstrap = Path.Combine(_root, "ligase-bootstrap.json");
        File.WriteAllText(
            bootstrap,
            System.Text.Json.JsonSerializer.Serialize(new { dataRoot = managedRoot }));

        var paths = LigasePaths.CreateFromCommandLine(
            ["Ligase.Host.Desktop.exe"],
            redirected,
            bootstrapFile: bootstrap);

        Assert.AreEqual(Path.GetFullPath(managedRoot), paths.RootDirectory);
    }

    [TestMethod]
    public void DataRootRejectsRelativeMissingAndDuplicateValues()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            LigasePaths.CreateFromCommandLine(
                ["Ligase.Host.Desktop.exe", "--data-root", "relative"]));
        Assert.ThrowsException<ArgumentException>(() =>
            LigasePaths.CreateFromCommandLine(
                ["Ligase.Host.Desktop.exe", "--data-root"]));
        Assert.ThrowsException<ArgumentException>(() =>
            LigasePaths.CreateFromCommandLine(
                ["Ligase.Host.Desktop.exe", "--data-root", _root, "--data-root", _root]));
    }

    [TestMethod]
    public async Task ExternalCoreFailsClosedBeforeRepositoryMutation()
    {
        var paths = CreateProjectionFiles();
        var repository = new FakeRepository(paths);
        var authority = new FakeAuthority(
            new LibraryAuthorityState(
                LibraryAuthorityKind.ExternalUnknown,
                "externalCore",
                "只读"));
        var coordinator = new LibraryMutationCoordinator(paths, repository, authority);

        await Assert.ThrowsExceptionAsync<LibraryAuthorityException>(() =>
            coordinator.AddSteamAsync(Game));

        Assert.AreEqual(0, repository.MutationCount);
        Assert.AreEqual("old-library", File.ReadAllText(paths.LibraryFile));
    }

    [TestMethod]
    public async Task ReadbackFailureRestoresExactProjectionSnapshots()
    {
        var paths = CreateProjectionFiles();
        var repository = new FakeRepository(paths);
        var authority = new FakeAuthority(ManagedState, failReadbackNumber: 2);
        var coordinator = new LibraryMutationCoordinator(paths, repository, authority);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            coordinator.AddSteamAsync(Game));

        Assert.AreEqual("old-library", File.ReadAllText(paths.LibraryFile));
        Assert.AreEqual("old-sync", File.ReadAllText(paths.SyncFile));
        Assert.AreEqual("old-apps", File.ReadAllText(paths.ApolloAppsFile));
        Assert.AreEqual("old-layout", File.ReadAllText(paths.LayoutCatalogFile));
        Assert.AreEqual(3, authority.ReadbackCount);
    }

    [TestMethod]
    public void ManualOrderReadbackRequiresRevisionModeAndExactSequence()
    {
        var first = new LibraryItem
        {
            Id = SystemLibraryIds.Desktop,
            Kind = LibraryItemKind.Desktop,
            Name = "监控桌面"
        };
        var second = new LibraryItem
        {
            Id = Guid.Parse("f3d67f4d-b1fe-4c5d-a77e-b78a51051c1a"),
            Kind = LibraryItemKind.Steam,
            Name = "Chill"
        };
        var state = new LibraryState
        {
            Revision = 12,
            SortMode = LibrarySortMode.Manual,
            Items = [first, second]
        };
        var matching = new AuthorityReadbackDocument(
            SchemaVersion: 1,
            AuthorityToken: "token",
            StartNonce: "nonce",
            RootFingerprint: "fingerprint",
            HostUniqueId: "host",
            LibraryItems: [],
            Apps: [],
            LibraryRevision: 12,
            LibrarySortMode: "manual",
            LibraryOrder: [first.Id.ToString("D"), second.Id.ToString("D")]);

        Assert.IsTrue(LibraryMutationCoordinator.MatchesCanonicalOrder(matching, state));
        Assert.IsFalse(LibraryMutationCoordinator.MatchesCanonicalOrder(
            matching with { LibraryRevision = 13 }, state));
        Assert.IsFalse(LibraryMutationCoordinator.MatchesCanonicalOrder(
            matching with { LibrarySortMode = "nameAscending" }, state));
        Assert.IsFalse(LibraryMutationCoordinator.MatchesCanonicalOrder(
            matching with
            {
                LibraryOrder = [second.Id.ToString("D"), first.Id.ToString("D")]
            },
            state));
    }

    [TestMethod]
    public async Task LayoutBindingUpdatesCatalogAndRequiresExactCoreReadback()
    {
        var paths = new LigasePaths(_root);
        Directory.CreateDirectory(paths.ApolloDirectory);
        File.WriteAllText(paths.LibraryFile, "old-library");
        File.WriteAllText(paths.SyncFile, "old-sync");
        File.WriteAllText(paths.ApolloAppsFile, "old-apps");
        var layoutId = "10000000-0000-4000-8000-000000000000";
        var binding = new LayoutBindingV1(layoutId, 1);
        var catalog = new JsonLayoutCatalogRepository(paths.LayoutCatalogFile);
        await catalog.SaveAsync(new LayoutCatalogSnapshot(
            1,
            [new LayoutDescriptorV1(
                1,
                layoutId,
                1,
                [new PortableGameIdentityV1("steam", "3548580")],
                new LayoutCompatibilityV1(1, 1),
                "published",
                [new LayoutVariantV1(
                    "20000000-0000-4000-8000-000000000000",
                    "touch",
                    ["phone"],
                    ["landscape"])])],
            []));
        var repository = new FakeRepository(paths);
        var authority = new BindingAuthority(binding);
        var coordinator = new LibraryMutationCoordinator(
            paths,
            repository,
            authority,
            layoutCatalogService: new LayoutCatalogService(catalog));

        var item = await coordinator.SetLayoutBindingAsync(BindingAppId, binding);

        Assert.AreEqual(binding, item.LayoutBinding);
        var snapshot = await catalog.LoadAsync();
        var saved = snapshot.ExplicitBindings.Single();
        Assert.AreEqual(BindingAppId.ToString("D"), saved.Instance.AppUuid);
        Assert.AreEqual(BindingCore.UniqueId, saved.Instance.HostUniqueId);
        Assert.AreEqual(binding, saved.Binding);
        Assert.AreEqual(2, authority.ReadbackCount);
    }

    private LigasePaths CreateProjectionFiles()
    {
        var paths = new LigasePaths(_root);
        Directory.CreateDirectory(paths.ApolloDirectory);
        File.WriteAllText(paths.LibraryFile, "old-library");
        File.WriteAllText(paths.SyncFile, "old-sync");
        File.WriteAllText(paths.ApolloAppsFile, "old-apps");
        File.WriteAllText(paths.LayoutCatalogFile, "old-layout");
        return paths;
    }

    private static readonly SteamGame Game = new(
        3548580,
        "Chill with You Lo-Fi Story",
        "Chill with You Lo-Fi Story",
        @"D:\Steam",
        @"D:\Steam\steamapps\appmanifest_3548580.acf",
        1);

    private static readonly ApolloCoreEndpoint Core = new(
        LigaseEndpoint.Create(
            LigaseEndpointScheme.Http,
            "127.0.0.1",
            49989,
            source: LigaseEndpointSource.Loopback),
        "host",
        "Ligase");

    private static readonly Guid BindingAppId =
        Guid.Parse("30000000-0000-4000-8000-000000000000");
    private static readonly ApolloCoreEndpoint BindingCore = new(
        LigaseEndpoint.Create(
            LigaseEndpointScheme.Http,
            "127.0.0.1",
            49989,
            source: LigaseEndpointSource.Loopback),
        "40000000-0000-4000-8000-000000000000",
        "Ligase");

    private static readonly LibraryAuthorityState ManagedState = new(
        LibraryAuthorityKind.ManagedAuthoritative,
        "managedAuthoritative",
        "ok",
        Core);

    private sealed class FakeRepository(LigasePaths paths) : IApplicationLibrary
    {
        public int MutationCount { get; private set; }

        public Task<LibraryItem> AddSteamAsync(
            SteamGame game,
            string? coverImagePath = null,
            CancellationToken cancellationToken = default)
        {
            MutationCount++;
            File.WriteAllText(paths.LibraryFile, "new-library");
            File.WriteAllText(paths.SyncFile, "new-sync");
            File.WriteAllText(paths.ApolloAppsFile, "new-apps");
            return Task.FromResult(new LibraryItem
            {
                Id = Guid.Parse("f3d67f4d-b1fe-4c5d-a77e-b78a51051c1a"),
                Kind = LibraryItemKind.Steam,
                Name = game.Name,
                SteamAppId = game.AppId
            });
        }

        public Task<LibraryState> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryState());
        public Task<LibraryItem> AddExecutableAsync(
            string name,
            string executablePath,
            string? arguments,
            string? workingDirectory,
            string? coverImagePath = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<LibraryState> SetManualOrderAsync(
            long baseRevision,
            IReadOnlyList<Guid> orderedPublishedAppIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SetPublishedToClientsAsync(Guid id, bool published, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<LibraryItem> SetLayoutBindingAsync(
            Guid id,
            LayoutBindingV1? binding,
            CancellationToken cancellationToken = default)
        {
            MutationCount++;
            File.WriteAllText(paths.LibraryFile, "new-library-binding");
            File.WriteAllText(paths.SyncFile, "new-sync-binding");
            File.WriteAllText(paths.ApolloAppsFile, "new-apps-binding");
            return Task.FromResult(new LibraryItem
            {
                Id = id,
                Kind = LibraryItemKind.Steam,
                Name = "Chill",
                SteamAppId = 3548580,
                PortableIdentity = new PortableGameIdentityV1("steam", "3548580"),
                LayoutBinding = binding
            });
        }
        public Task<LibraryItem> UpdateSteamCoverAsync(
            Guid id,
            PortableGameIdentityV1 expectedPortableIdentity,
            string coverImagePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAuthority(
        LibraryAuthorityState state,
        int? failReadbackNumber = null) : ILibraryAuthorityService
    {
        public int ReadbackCount { get; private set; }

        public Task<LibraryAuthorityState> GetStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(state);

        public Task<AuthorityReadbackDocument> RequireReadbackAsync(
            ApolloCoreEndpoint core,
            bool reload,
            CancellationToken cancellationToken = default)
        {
            ReadbackCount++;
            if (ReadbackCount == failReadbackNumber)
                throw new InvalidOperationException("readback failed");
            return Task.FromResult(new AuthorityReadbackDocument(
                1, "token", "nonce", "root", "host", [], []));
        }
    }

    private sealed class BindingAuthority(LayoutBindingV1 binding)
        : ILibraryAuthorityService
    {
        public int ReadbackCount { get; private set; }

        public Task<LibraryAuthorityState> GetStateAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryAuthorityState(
                LibraryAuthorityKind.ManagedAuthoritative,
                "managedAuthoritative",
                "ok",
                BindingCore));

        public Task<AuthorityReadbackDocument> RequireReadbackAsync(
            ApolloCoreEndpoint core,
            bool reload,
            CancellationToken cancellationToken = default)
        {
            ReadbackCount++;
            var items = ReadbackCount == 1
                ? Array.Empty<AuthorityReadbackLibraryItem>()
                :
                [
                    new AuthorityReadbackLibraryItem(
                        BindingAppId.ToString("D"),
                        "Steam",
                        3548580,
                        true,
                        PortableIdentity: new PortableGameIdentityV1("steam", "3548580"),
                        LayoutBinding: binding)
                ];
            return Task.FromResult(new AuthorityReadbackDocument(
                1,
                "token",
                "nonce",
                "root",
                BindingCore.UniqueId,
                items,
                []));
        }
    }
}
