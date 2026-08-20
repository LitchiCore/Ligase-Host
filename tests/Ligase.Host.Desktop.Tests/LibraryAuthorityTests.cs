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

        await Assert.ThrowsExceptionAsync<LibraryAuthorityReadbackException>(() =>
            coordinator.AddSteamAsync(Game));

        Assert.AreEqual("old-library", File.ReadAllText(paths.LibraryFile));
        Assert.AreEqual("old-sync", File.ReadAllText(paths.SyncFile));
        Assert.AreEqual("old-apps", File.ReadAllText(paths.ApolloAppsFile));
        Assert.AreEqual("old-layout", File.ReadAllText(paths.LayoutCatalogFile));
        Assert.AreEqual(3, authority.ReadbackCount);
        using var outcome = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(paths.LibraryMutationOutcomeFile));
        Assert.AreEqual("addSteam", outcome.RootElement.GetProperty("operation").GetString());
        Assert.AreEqual("rolledBack", outcome.RootElement.GetProperty("state").GetString());
        Assert.AreEqual("deadlineExceeded", outcome.RootElement.GetProperty("primary")
            .GetProperty("reasonCode").GetString());
        Assert.AreEqual("completed", outcome.RootElement.GetProperty("rollback")
            .GetProperty("resultCode").GetString());
        Assert.IsFalse(File.Exists(paths.LibraryMutationOutcomeFile + ".tmp"));
    }

    [TestMethod]
    public async Task RollbackReadbackFailurePersistsPrimaryAndUnprovenRollback()
    {
        var paths = CreateProjectionFiles();
        var repository = new FakeRepository(paths);
        var authority = new FakeAuthority(ManagedState, failReadbackNumbers: [2, 3]);
        var coordinator = new LibraryMutationCoordinator(paths, repository, authority);

        await Assert.ThrowsExceptionAsync<LibraryAuthorityReadbackException>(() =>
            coordinator.AddSteamAsync(Game));

        using var outcome = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(paths.LibraryMutationOutcomeFile));
        Assert.AreEqual("rollbackUnproven", outcome.RootElement.GetProperty("state").GetString());
        Assert.AreEqual("deadlineExceeded", outcome.RootElement.GetProperty("primary")
            .GetProperty("reasonCode").GetString());
        Assert.AreEqual("deadlineExceeded", outcome.RootElement.GetProperty("rollback")
            .GetProperty("reasonCode").GetString());
        Assert.AreEqual(3, authority.ReadbackCount);
        Assert.IsFalse(File.Exists(paths.LibraryMutationOutcomeFile + ".tmp"));
    }

    [TestMethod]
    public async Task RollbackWriteFailureStillPersistsPrimaryAndRollbackStage()
    {
        var paths = CreateProjectionFiles();
        Directory.CreateDirectory(paths.LibraryFile + ".rollback.tmp");
        var repository = new FakeRepository(paths);
        var authority = new FakeAuthority(ManagedState, failReadbackNumber: 2);
        var coordinator = new LibraryMutationCoordinator(paths, repository, authority);

        var primary = await Assert.ThrowsExceptionAsync<LibraryAuthorityReadbackException>(() =>
            coordinator.AddSteamAsync(Game));

        using var outcome = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(paths.LibraryMutationOutcomeFile));
        Assert.AreEqual("rollbackUnproven", outcome.RootElement.GetProperty("state").GetString());
        Assert.AreEqual("deadlineExceeded", outcome.RootElement.GetProperty("primary")
            .GetProperty("reasonCode").GetString());
        Assert.AreEqual("rollback.mutation", outcome.RootElement.GetProperty("rollback")
            .GetProperty("stage").GetString());
        Assert.AreEqual("mutationFailed", outcome.RootElement.GetProperty("rollback")
            .GetProperty("reasonCode").GetString());
        Assert.AreEqual(2, authority.ReadbackCount);
        CollectionAssert.Contains(
            LibraryMutationCoordinator.SecondaryFailures(primary).ToList(),
            new LibraryMutationSecondaryFailure("rollback.mutation", "mutationFailed"));
    }

    [TestMethod]
    public async Task CommittedProjectionIsNotReinterpretedWhenOutcomePersistenceFails()
    {
        var paths = CreateProjectionFiles();
        var repository = new FakeRepository(paths);
        var authority = new SuccessfulAddAuthority();
        var coordinator = new LibraryMutationCoordinator(
            paths,
            repository,
            authority,
            outcomeStore: new ThrowingOutcomeStore());

        var item = await coordinator.AddSteamAsync(Game);

        Assert.AreEqual(Guid.Parse("f3d67f4d-b1fe-4c5d-a77e-b78a51051c1a"), item.Id);
        Assert.AreEqual("new-library", File.ReadAllText(paths.LibraryFile));
        Assert.AreEqual("persistenceUnavailable",
            coordinator.LastOutcomePersistenceStatus.ResultCode);
        Assert.AreEqual("unknown", coordinator.LastOutcomePersistenceStatus.State);
        Assert.AreEqual(2, authority.ReadbackCount);
    }

    [TestMethod]
    public void AtomicOutcomeStoreUsesHandleReadbackAndFailsClosedAtEveryStage()
    {
        var stages = Enum.GetValues<LibraryMutationOutcomeStoreStage>()
            .Where(stage => stage != LibraryMutationOutcomeStoreStage.CleanupTemporary)
            .ToArray();
        foreach (var injected in stages)
        {
            var root = Path.Combine(_root, injected.ToString());
            var paths = new LigasePaths(root);
            var store = new AtomicLibraryMutationOutcomeStore(
                paths,
                stage =>
                {
                    if (stage == injected) throw new IOException($"injected {stage}");
                });

            var error = Assert.ThrowsException<LibraryMutationOutcomeStoreException>(() =>
                store.PersistAndReadback(CommittedOutcome()));

            Assert.AreEqual(injected, error.Stage);
            Assert.IsFalse(error.CleanupFailed);
            Assert.AreEqual(0, Directory.Exists(root)
                ? Directory.GetFiles(root, "*.tmp", SearchOption.TopDirectoryOnly).Length
                : 0, $"temporary residue after {injected}");
        }
    }

    [TestMethod]
    public void AtomicOutcomeStoreReportsCleanupFailureWithoutMaskingPrimaryStage()
    {
        var paths = new LigasePaths(Path.Combine(_root, "cleanup-failure"));
        var store = new AtomicLibraryMutationOutcomeStore(
            paths,
            stage =>
            {
                if (stage is LibraryMutationOutcomeStoreStage.WriteTemporary or
                    LibraryMutationOutcomeStoreStage.CleanupTemporary)
                    throw new IOException($"injected {stage}");
            });

        var error = Assert.ThrowsException<LibraryMutationOutcomeStoreException>(() =>
            store.PersistAndReadback(CommittedOutcome()));

        Assert.AreEqual(LibraryMutationOutcomeStoreStage.WriteTemporary, error.Stage);
        Assert.IsTrue(error.CleanupFailed);
        Assert.AreEqual(1, Directory.GetFiles(paths.RootDirectory, "*.tmp").Length);
    }

    [TestMethod]
    public void AtomicOutcomeStoreRoundTripsOnlySemanticallyValidDocument()
    {
        var paths = new LigasePaths(Path.Combine(_root, "valid-roundtrip"));
        var store = new AtomicLibraryMutationOutcomeStore(paths);

        var readback = store.PersistAndReadback(CommittedOutcome());

        LibraryMutationOutcomeSemanticValidator.Validate(readback);
        Assert.AreEqual(CommittedOutcome(), readback);
        Assert.AreEqual(0, Directory.GetFiles(paths.RootDirectory, "*.tmp").Length);

        var invalid = CommittedOutcome() with
        {
            Primary = new LibraryMutationAttempt(
                "completed", "reload.readback", "none", 201, 1)
        };
        Assert.ThrowsException<InvalidDataException>(() => store.PersistAndReadback(invalid));
    }

    [TestMethod]
    public async Task RolledBackProjectionKeepsOriginalPrimaryWhenOutcomePersistenceFails()
    {
        var paths = CreateProjectionFiles();
        var repository = new FakeRepository(paths);
        var authority = new FakeAuthority(ManagedState, failReadbackNumber: 2);
        var coordinator = new LibraryMutationCoordinator(
            paths, repository, authority, outcomeStore: new ThrowingOutcomeStore());

        var primary = await Assert.ThrowsExceptionAsync<LibraryAuthorityReadbackException>(() =>
            coordinator.AddSteamAsync(Game));

        Assert.AreEqual("deadlineExceeded", primary.Failure.ReasonCode);
        Assert.AreEqual("old-library", File.ReadAllText(paths.LibraryFile));
        CollectionAssert.AreEqual(
            new[] { new LibraryMutationSecondaryFailure(
                "outcome.persistence", "writeOrReadbackFailed") },
            LibraryMutationCoordinator.SecondaryFailures(primary).ToArray());
        Assert.AreEqual("persistenceUnavailable",
            coordinator.LastOutcomePersistenceStatus.ResultCode);
    }

    [TestMethod]
    public async Task RollbackAndPersistenceFailuresRemainSecondaryToOriginalPrimary()
    {
        var paths = CreateProjectionFiles();
        Directory.CreateDirectory(paths.LibraryFile + ".rollback.tmp");
        var repository = new FakeRepository(paths);
        var authority = new FakeAuthority(ManagedState, failReadbackNumber: 2);
        var coordinator = new LibraryMutationCoordinator(
            paths, repository, authority, outcomeStore: new ThrowingOutcomeStore());

        var primary = await Assert.ThrowsExceptionAsync<LibraryAuthorityReadbackException>(() =>
            coordinator.AddSteamAsync(Game));

        Assert.AreEqual("deadlineExceeded", primary.Failure.ReasonCode);
        CollectionAssert.AreEquivalent(
            new[]
            {
                new LibraryMutationSecondaryFailure("rollback.mutation", "mutationFailed"),
                new LibraryMutationSecondaryFailure("outcome.persistence", "writeOrReadbackFailed")
            },
            LibraryMutationCoordinator.SecondaryFailures(primary).ToArray());
        Assert.AreEqual("persistenceUnavailable",
            coordinator.LastOutcomePersistenceStatus.ResultCode);
    }

    [TestMethod]
    public async Task ExceptionDispatchInfoPreservesExactPrimaryAcrossRollbackAndPersistenceFailures()
    {
        var paths = CreateProjectionFiles();
        Directory.CreateDirectory(paths.LibraryFile + ".rollback.tmp");
        var expected = new InvalidOperationException("injected primary mutation failure");
        var coordinator = new LibraryMutationCoordinator(
            paths,
            new ThrowingRepository(expected),
            new FakeAuthority(ManagedState),
            outcomeStore: new ThrowingOutcomeStore());

        var actual = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            coordinator.AddSteamAsync(Game));

        Assert.AreSame(expected, actual);
        CollectionAssert.AreEquivalent(
            new[]
            {
                new LibraryMutationSecondaryFailure("rollback.mutation", "mutationFailed"),
                new LibraryMutationSecondaryFailure("outcome.persistence", "writeOrReadbackFailed")
            },
            LibraryMutationCoordinator.SecondaryFailures(actual).ToArray());
    }

    [TestMethod]
    public async Task OutcomeReadbackMismatchIsPersistenceUnavailableWithoutRollback()
    {
        var paths = CreateProjectionFiles();
        var repository = new FakeRepository(paths);
        var coordinator = new LibraryMutationCoordinator(
            paths,
            repository,
            new SuccessfulAddAuthority(),
            outcomeStore: new MismatchingOutcomeStore());

        await coordinator.AddSteamAsync(Game);

        Assert.AreEqual("new-library", File.ReadAllText(paths.LibraryFile));
        Assert.AreEqual("persistenceUnavailable",
            coordinator.LastOutcomePersistenceStatus.ResultCode);
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
        using var outcome = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(paths.LibraryMutationOutcomeFile));
        Assert.AreEqual("setLayoutBinding", outcome.RootElement.GetProperty("operation").GetString());
        Assert.AreEqual("committed", outcome.RootElement.GetProperty("state").GetString());
        Assert.AreEqual("completed", outcome.RootElement.GetProperty("primary")
            .GetProperty("resultCode").GetString());
        Assert.AreEqual("notAttempted", outcome.RootElement.GetProperty("rollback")
            .GetProperty("resultCode").GetString());
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

    private static LibraryMutationOutcomeDocument CommittedOutcome() => new(
        1,
        "addSteam",
        "committed",
        new LibraryMutationAttempt("completed", "reload.readback", "none", 200, 1),
        LibraryMutationAttempt.NotAttempted,
        new DateTimeOffset(2026, 8, 21, 5, 0, 0, TimeSpan.Zero));

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
        int? failReadbackNumber = null,
        IReadOnlyCollection<int>? failReadbackNumbers = null) : ILibraryAuthorityService
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
            if (ReadbackCount == failReadbackNumber ||
                failReadbackNumbers?.Contains(ReadbackCount) == true)
                throw new LibraryAuthorityReadbackException(
                    new LibraryAuthorityReadbackFailure(
                        "timeout", "reload.transport", "deadlineExceeded", null, 3000),
                    "readback failed");
            return Task.FromResult(new AuthorityReadbackDocument(
                1, "token", "nonce", "root", "host", [], [],
                Reload: reload
                    ? new AuthorityReloadResult(1, "completed", "readback", "none", 4)
                    : null));
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

    private sealed class SuccessfulAddAuthority : ILibraryAuthorityService
    {
        public int ReadbackCount { get; private set; }

        public Task<LibraryAuthorityState> GetStateAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(ManagedState);

        public Task<AuthorityReadbackDocument> RequireReadbackAsync(
            ApolloCoreEndpoint core,
            bool reload,
            CancellationToken cancellationToken = default)
        {
            ReadbackCount++;
            var items = ReadbackCount == 1
                ? Array.Empty<AuthorityReadbackLibraryItem>()
                : [new AuthorityReadbackLibraryItem(
                    "f3d67f4d-b1fe-4c5d-a77e-b78a51051c1a", "Steam", 3548580, true)];
            var apps = ReadbackCount == 1
                ? Array.Empty<AuthorityReadbackApp>()
                : [new AuthorityReadbackApp(
                    "f3d67f4d-b1fe-4c5d-a77e-b78a51051c1a", "123")];
            return Task.FromResult(new AuthorityReadbackDocument(
                1, "token", "nonce", "root", "host", items, apps,
                Reload: reload
                    ? new AuthorityReloadResult(1, "completed", "readback", "none", 4)
                    : null));
        }
    }

    private sealed class ThrowingOutcomeStore : ILibraryMutationOutcomeStore
    {
        public LibraryMutationOutcomeDocument PersistAndReadback(
            LibraryMutationOutcomeDocument outcome) =>
            throw new IOException("injected outcome persistence failure");
    }

    private sealed class MismatchingOutcomeStore : ILibraryMutationOutcomeStore
    {
        public LibraryMutationOutcomeDocument PersistAndReadback(
            LibraryMutationOutcomeDocument outcome) => outcome with { State = "rolledBack" };
    }

    private sealed class ThrowingRepository(Exception expected) : IApplicationLibrary
    {
        public Task<LibraryState> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryState());
        public Task<LibraryItem> AddSteamAsync(SteamGame game, string? coverImagePath = null,
            CancellationToken cancellationToken = default) => Task.FromException<LibraryItem>(expected);
        public Task<LibraryItem> AddExecutableAsync(string name, string executablePath,
            string? arguments, string? workingDirectory, string? coverImagePath = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LibraryState> SetManualOrderAsync(long baseRevision,
            IReadOnlyList<Guid> orderedPublishedAppIds,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetPublishedToClientsAsync(Guid id, bool published,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LibraryItem> SetLayoutBindingAsync(Guid id, LayoutBindingV1? binding,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LibraryItem> UpdateSteamCoverAsync(Guid id,
            PortableGameIdentityV1 expectedPortableIdentity, string coverImagePath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
