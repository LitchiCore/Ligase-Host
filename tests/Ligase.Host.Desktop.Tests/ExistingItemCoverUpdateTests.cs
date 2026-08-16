using System.Security.Cryptography;
using System.Text.Json;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class ExistingItemCoverUpdateTests
{
    private string _root = null!;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "Ligase.ExistingCover.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task DefaultCoverUpdatesSameItemAndPersistsAcrossRestart()
    {
        var fixture = await Fixture.CreateAsync(_root);

        var result = await fixture.Coordinator.UpdateExistingSteamCoverAsync(
            fixture.Request());

        Assert.AreEqual(fixture.Item.Id, result.LibraryItemId);
        Assert.AreEqual(fixture.Item.PortableIdentity, result.PortableIdentity);
        Assert.AreEqual("steamClientLibraryCache", result.CoverSourceKind);
        Assert.AreEqual(AppId.ToString(), result.CoverSourceId);
        Assert.IsFalse(result.Idempotent);
        var restarted = new ApplicationLibrary(
            fixture.Paths,
            fixture.AppsWriter,
            syncWriter: new LigaseSyncDocumentWriter(fixture.Paths));
        var persisted = (await restarted.LoadAsync()).Items.Single(item => item.Id == fixture.Item.Id);
        Assert.AreEqual(result.CoverContentSha256, persisted.CoverContentSha256);
        Assert.AreEqual(result.CoverImagePath, persisted.CoverImagePath);
        Assert.AreEqual(fixture.Item.Name, persisted.Name);
        Assert.AreEqual(fixture.Item.SteamInstallPath, persisted.SteamInstallPath);
        using var sync = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.Paths.SyncFile));
        var projected = sync.RootElement.GetProperty("library").GetProperty("items")
            .EnumerateArray().Single(value => value.GetProperty("id").GetGuid() == fixture.Item.Id);
        Assert.AreEqual(result.CoverContentSha256,
            projected.GetProperty("coverSha256").GetString());
        Assert.AreEqual(fixture.Item.Id.ToString("D"),
            fixture.Authority.LastReadback!.LibraryItems.Single(item =>
                item.Id.Equals(fixture.Item.Id.ToString("D"), StringComparison.OrdinalIgnoreCase)).Id);
    }

    [TestMethod]
    public async Task IdentityAndSourceSplicesFailBeforeArtifactWrite()
    {
        var fixture = await Fixture.CreateAsync(_root);
        var requests = new[]
        {
            fixture.Request() with { LibraryItemId = Guid.NewGuid() },
            fixture.Request() with
            {
                PortableIdentity = new PortableGameIdentityV1("steam", "42")
            },
            fixture.Request() with
            {
                Candidate = fixture.Candidate with { SteamAppId = 42 }
            },
            fixture.Request() with
            {
                Candidate = fixture.Candidate with { SourceId = "42" }
            },
            fixture.Request() with
            {
                Candidate = fixture.Candidate with { SourceKind = "gameDbIgdb" }
            },
            fixture.Request() with
            {
                Candidate = fixture.Candidate with
                {
                    DownloadUrl = new Uri(Path.Combine(_root, "foreign.jpg")).AbsoluteUri
                }
            }
        };

        foreach (var request in requests)
            await Assert.ThrowsExceptionAsync<ExistingItemCoverUpdateException>(() =>
                fixture.Coordinator.UpdateExistingSteamCoverAsync(request));

        Assert.AreEqual(0, fixture.Artifacts.SuccessfulPrepareCount);
        var persisted = (await fixture.Library.LoadAsync()).Items.Single(item => item.Id == fixture.Item.Id);
        Assert.IsNull(persisted.CoverImagePath);
    }

    [TestMethod]
    public async Task TranscodeAndLibraryFailuresRestoreProjectionsAndOwnedArtifact()
    {
        var transcode = await Fixture.CreateAsync(Path.Combine(_root, "transcode"));
        transcode.Artifacts.FailPrepare = true;
        await Assert.ThrowsExceptionAsync<ExistingItemCoverUpdateException>(() =>
            transcode.Coordinator.UpdateExistingSteamCoverAsync(transcode.Request()));
        Assert.IsFalse(Directory.Exists(transcode.Paths.CoversDirectory));

        var libraryFailure = await Fixture.CreateAsync(Path.Combine(_root, "library"));
        var beforeLibrary = await File.ReadAllBytesAsync(libraryFailure.Paths.LibraryFile);
        var beforeSync = await File.ReadAllBytesAsync(libraryFailure.Paths.SyncFile);
        var failing = new ThrowingUpdateRepository(libraryFailure.Library);
        var coordinator = new LibraryMutationCoordinator(
            libraryFailure.Paths, failing, libraryFailure.Authority, libraryFailure.Artifacts);

        await Assert.ThrowsExceptionAsync<ExistingItemCoverUpdateException>(() =>
            coordinator.UpdateExistingSteamCoverAsync(libraryFailure.Request()));

        CollectionAssert.AreEqual(beforeLibrary, await File.ReadAllBytesAsync(libraryFailure.Paths.LibraryFile));
        CollectionAssert.AreEqual(beforeSync, await File.ReadAllBytesAsync(libraryFailure.Paths.SyncFile));
        Assert.AreEqual(1, libraryFailure.Artifacts.RollbackCount);
        Assert.AreEqual(0, Directory.Exists(libraryFailure.Paths.CoversDirectory)
            ? Directory.GetFiles(libraryFailure.Paths.CoversDirectory).Length : 0);
    }

    [TestMethod]
    public async Task CoreReadbackFailureRestoresLibrarySyncAndCoverReference()
    {
        var fixture = await Fixture.CreateAsync(_root);
        var beforeLibrary = await File.ReadAllBytesAsync(fixture.Paths.LibraryFile);
        var beforeSync = await File.ReadAllBytesAsync(fixture.Paths.SyncFile);
        fixture.Authority.FailReadbackNumber = 2;

        await Assert.ThrowsExceptionAsync<ExistingItemCoverUpdateException>(() =>
            fixture.Coordinator.UpdateExistingSteamCoverAsync(fixture.Request()));

        CollectionAssert.AreEqual(beforeLibrary, await File.ReadAllBytesAsync(fixture.Paths.LibraryFile));
        CollectionAssert.AreEqual(beforeSync, await File.ReadAllBytesAsync(fixture.Paths.SyncFile));
        Assert.AreEqual(1, fixture.Artifacts.RollbackCount);
        var restored = (await fixture.Library.LoadAsync()).Items.Single(item => item.Id == fixture.Item.Id);
        Assert.IsNull(restored.CoverImagePath);
    }

    [TestMethod]
    public async Task SyncPublishFailureRestoresOriginalLibraryAndSyncBytes()
    {
        var fixture = await Fixture.CreateAsync(_root);
        var beforeLibrary = await File.ReadAllBytesAsync(fixture.Paths.LibraryFile);
        var beforeSync = await File.ReadAllBytesAsync(fixture.Paths.SyncFile);
        var failing = new LockSyncDuringUpdateRepository(fixture.Library, fixture.Paths.SyncFile);
        var coordinator = new LibraryMutationCoordinator(
            fixture.Paths, failing, fixture.Authority, fixture.Artifacts);

        await Assert.ThrowsExceptionAsync<ExistingItemCoverUpdateException>(() =>
            coordinator.UpdateExistingSteamCoverAsync(fixture.Request()));

        CollectionAssert.AreEqual(beforeLibrary, await File.ReadAllBytesAsync(fixture.Paths.LibraryFile));
        CollectionAssert.AreEqual(beforeSync, await File.ReadAllBytesAsync(fixture.Paths.SyncFile));
        Assert.AreEqual(1, fixture.Artifacts.RollbackCount);
    }

    [TestMethod]
    public async Task ReplacingCoverPrunesOnlyUnreferencedOldArtifactAndSameCoverIsIdempotent()
    {
        var fixture = await Fixture.CreateAsync(_root, seedOldCover: true);
        var oldPath = fixture.Item.CoverImagePath!;
        var foreign = Path.Combine(fixture.Paths.RootDirectory, "foreign.png");
        await File.WriteAllBytesAsync(foreign, [1, 2, 3]);

        var first = await fixture.Coordinator.UpdateExistingSteamCoverAsync(fixture.Request());
        var libraryBytes = await File.ReadAllBytesAsync(fixture.Paths.LibraryFile);
        var syncBytes = await File.ReadAllBytesAsync(fixture.Paths.SyncFile);
        var second = await fixture.Coordinator.UpdateExistingSteamCoverAsync(fixture.Request());

        Assert.IsFalse(File.Exists(oldPath));
        Assert.IsTrue(File.Exists(foreign));
        Assert.IsTrue(second.Idempotent);
        Assert.AreEqual(first.LibraryRevision, second.LibraryRevision);
        CollectionAssert.AreEqual(libraryBytes, await File.ReadAllBytesAsync(fixture.Paths.LibraryFile));
        CollectionAssert.AreEqual(syncBytes, await File.ReadAllBytesAsync(fixture.Paths.SyncFile));
    }

    private const uint AppId = 3548580;

    private sealed class Fixture
    {
        private Fixture(
            LigasePaths paths,
            ApplicationLibrary library,
            ProjectionAppsWriter appsWriter,
            FakeArtifactService artifacts,
            ReadbackAuthority authority,
            LibraryItem item,
            CoverCandidate candidate)
        {
            Paths = paths;
            Library = library;
            AppsWriter = appsWriter;
            Artifacts = artifacts;
            Authority = authority;
            Item = item;
            Candidate = candidate;
            Coordinator = new LibraryMutationCoordinator(paths, library, authority, artifacts);
        }

        public LigasePaths Paths { get; }
        public ApplicationLibrary Library { get; }
        public ProjectionAppsWriter AppsWriter { get; }
        public FakeArtifactService Artifacts { get; }
        public ReadbackAuthority Authority { get; }
        public LibraryMutationCoordinator Coordinator { get; }
        public LibraryItem Item { get; }
        public CoverCandidate Candidate { get; }

        public ExistingItemCoverUpdateRequest Request() =>
            new(Item.Id, Item.PortableIdentity!, Candidate);

        public static async Task<Fixture> CreateAsync(
            string root,
            bool seedOldCover = false)
        {
            Directory.CreateDirectory(root);
            var paths = new LigasePaths(root);
            var writer = new ProjectionAppsWriter(paths);
            var library = new ApplicationLibrary(
                paths,
                writer,
                syncWriter: new LigaseSyncDocumentWriter(paths));
            var artifacts = new FakeArtifactService(paths);
            string? oldCover = null;
            if (seedOldCover)
            {
                var oldCandidate = CandidateFor(paths, "old");
                using var old = await artifacts.PrepareAsync(oldCandidate);
                oldCover = old.Path;
            }
            var item = await library.AddSteamAsync(
                new SteamGame(
                    AppId, "Chill", "Chill", root,
                    Path.Combine(root, $"appmanifest_{AppId}.acf"), 1),
                oldCover);
            var authority = new ReadbackAuthority(library);
            return new Fixture(
                paths,
                library,
                writer,
                artifacts,
                authority,
                item,
                CandidateFor(paths, "new"));
        }

        private static CoverCandidate CandidateFor(LigasePaths paths, string key)
        {
            var source = Path.Combine(paths.RootDirectory, $"{key}.jpg");
            File.WriteAllBytes(source, key == "old" ? [1, 1, 1] : [2, 2, 2]);
            return new CoverCandidate(
                "Chill",
                $"steam_{AppId}_{key}",
                new Uri(source).AbsoluteUri,
                new Uri(source).AbsoluteUri,
                "steamClientLibraryCache",
                AppId.ToString(),
                "thirdPartyArtworkLocalUseOnlyNoRedistribution",
                AppId);
        }
    }

    private sealed class FakeArtifactService(LigasePaths paths) : ICoverArtifactService
    {
        public bool FailPrepare { get; set; }
        public int SuccessfulPrepareCount { get; private set; }
        public int RollbackCount { get; private set; }

        public async Task<PreparedCoverArtifact> PrepareAsync(
            CoverCandidate candidate,
            CancellationToken cancellationToken = default)
        {
            if (FailPrepare)
                throw new InvalidOperationException("transcode failed");
            var expected = Path.Combine(paths.RootDirectory,
                candidate.Key.EndsWith("_old", StringComparison.Ordinal) ? "old.jpg" : "new.jpg");
            if (!Uri.TryCreate(candidate.DownloadUrl, UriKind.Absolute, out var uri) ||
                !uri.IsFile ||
                !string.Equals(Path.GetFullPath(uri.LocalPath), Path.GetFullPath(expected),
                    StringComparison.OrdinalIgnoreCase))
                throw new ExistingItemCoverUpdateException("sourcePathMismatch", "source mismatch");
            var content = await File.ReadAllBytesAsync(expected, cancellationToken);
            var sha = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            Directory.CreateDirectory(paths.CoversDirectory);
            var destination = Path.Combine(paths.CoversDirectory, $"{candidate.Key}_{sha[..16]}.png");
            var created = !File.Exists(destination);
            if (created) await File.WriteAllBytesAsync(destination, content, cancellationToken);
            var authority = new CoverCacheAuthority(
                Path.GetRelativePath(paths.RootDirectory, destination).Replace('\\', '/'),
                sha,
                candidate.SourceKind,
                candidate.SourceId,
                candidate.UsageRights,
                candidate.SteamAppId,
                DateTimeOffset.UtcNow);
            await WriteAuthorityAsync(paths, authority, cancellationToken);
            SuccessfulPrepareCount++;
            return new PreparedCoverArtifact(destination, authority, created, null);
        }

        public Task RollbackAsync(
            PreparedCoverArtifact artifact,
            CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            artifact.Dispose();
            if (artifact.CreatedOwned && File.Exists(artifact.Path)) File.Delete(artifact.Path);
            return Task.CompletedTask;
        }

        public async Task PruneUnreferencedAsync(
            IReadOnlyCollection<LibraryItem> items,
            CancellationToken cancellationToken = default)
        {
            var retained = items.Select(item => item.CoverImagePath)
                .Where(path => path is not null)
                .Select(path => Path.GetFullPath(path!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(paths.CoversDirectory)) return;
            foreach (var file in Directory.GetFiles(paths.CoversDirectory))
                if (!retained.Contains(Path.GetFullPath(file))) File.Delete(file);
            await Task.CompletedTask;
        }

        private static async Task WriteAuthorityAsync(
            LigasePaths paths,
            CoverCacheAuthority authority,
            CancellationToken cancellationToken)
        {
            var entries = new List<CoverCacheAuthority>();
            if (File.Exists(paths.CoverCacheAuthorityFile))
            {
                using var current = JsonDocument.Parse(
                    await File.ReadAllBytesAsync(paths.CoverCacheAuthorityFile, cancellationToken));
                foreach (var item in current.RootElement.GetProperty("entries").EnumerateArray())
                {
                    var parsed = JsonSerializer.Deserialize<CoverCacheAuthority>(
                        item.GetRawText(), Camel)!;
                    if (!string.Equals(parsed.RelativePath, authority.RelativePath, StringComparison.Ordinal))
                        entries.Add(parsed);
                }
            }
            entries.Add(authority);
            await File.WriteAllTextAsync(
                paths.CoverCacheAuthorityFile,
                JsonSerializer.Serialize(new { schemaVersion = 1, entries }, Camel),
                cancellationToken);
        }
    }

    private sealed class ProjectionAppsWriter(LigasePaths paths) : IApolloAppsWriter
    {
        public int WriteCount { get; private set; }
        public Task WriteAsync(
            IReadOnlyCollection<LibraryItem> items,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            Directory.CreateDirectory(paths.ApolloDirectory);
            File.WriteAllText(paths.ApolloAppsFile, JsonSerializer.Serialize(
                items.Select(item => new { uuid = item.Id, imagePath = item.CoverImagePath })));
            return Task.CompletedTask;
        }
    }

    private sealed class ReadbackAuthority(ApplicationLibrary library) : ILibraryAuthorityService
    {
        private static readonly ApolloCoreEndpoint Core = new(
            LigaseEndpoint.Create(
                LigaseEndpointScheme.Http, "127.0.0.1", 49989,
                source: LigaseEndpointSource.Loopback),
            "host", "Ligase");

        public int? FailReadbackNumber { get; set; }
        public int ReadbackCount { get; private set; }
        public AuthorityReadbackDocument? LastReadback { get; private set; }

        public Task<LibraryAuthorityState> GetStateAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryAuthorityState(
                LibraryAuthorityKind.ManagedAuthoritative,
                "managedAuthoritative", "ok", Core));

        public async Task<AuthorityReadbackDocument> RequireReadbackAsync(
            ApolloCoreEndpoint core,
            bool reload,
            CancellationToken cancellationToken = default)
        {
            ReadbackCount++;
            if (ReadbackCount == FailReadbackNumber)
                throw new InvalidOperationException("readback failed");
            var state = await library.LoadAsync(cancellationToken);
            LastReadback = new AuthorityReadbackDocument(
                1, "token", "nonce", "root", Core.UniqueId,
                state.Items.Select(item => new AuthorityReadbackLibraryItem(
                    item.Id.ToString("D"),
                    item.Kind.ToString(),
                    item.SteamAppId,
                    item.PublishedToClients,
                    item.CoverContentSha256,
                    item.CoverSourceKind,
                    item.CoverSourceId,
                    item.CoverUsageRights,
                    item.PortableIdentity,
                    item.LayoutBinding)).ToArray(),
                state.Items.Where(item => item.Kind != LibraryItemKind.VirtualDesktop)
                    .Select(item => new AuthorityReadbackApp(
                        item.Id.ToString("D"),
                        "1")).ToArray(),
                state.Revision,
                state.SortMode.ToString() switch
                {
                    "Manual" => "manual",
                    _ => "nameAscending"
                },
                state.Items.Select(item => item.Id.ToString("D")).ToArray());
            return LastReadback;
        }
    }

    private sealed class ThrowingUpdateRepository(IApplicationLibrary inner) : IApplicationLibrary
    {
        public Task<LibraryState> LoadAsync(CancellationToken cancellationToken = default) =>
            inner.LoadAsync(cancellationToken);
        public Task<LibraryItem> AddSteamAsync(SteamGame game, string? coverImagePath = null,
            CancellationToken cancellationToken = default) =>
            inner.AddSteamAsync(game, coverImagePath, cancellationToken);
        public Task<LibraryItem> AddExecutableAsync(string name, string executablePath,
            string? arguments, string? workingDirectory, string? coverImagePath = null,
            CancellationToken cancellationToken = default) =>
            inner.AddExecutableAsync(name, executablePath, arguments, workingDirectory,
                coverImagePath, cancellationToken);
        public Task<LibraryState> SetManualOrderAsync(long baseRevision,
            IReadOnlyList<Guid> orderedPublishedAppIds,
            CancellationToken cancellationToken = default) =>
            inner.SetManualOrderAsync(baseRevision, orderedPublishedAppIds, cancellationToken);
        public Task SetPublishedToClientsAsync(Guid id, bool published,
            CancellationToken cancellationToken = default) =>
            inner.SetPublishedToClientsAsync(id, published, cancellationToken);
        public Task<LibraryItem> UpdateSteamCoverAsync(Guid id,
            PortableGameIdentityV1 expectedPortableIdentity, string coverImagePath,
            CancellationToken cancellationToken = default) =>
            throw new IOException("library write failed");
        public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.RemoveAsync(id, cancellationToken);
    }

    private sealed class LockSyncDuringUpdateRepository(
        IApplicationLibrary inner,
        string syncPath) : IApplicationLibrary
    {
        public Task<LibraryState> LoadAsync(CancellationToken cancellationToken = default) =>
            inner.LoadAsync(cancellationToken);
        public Task<LibraryItem> AddSteamAsync(SteamGame game, string? coverImagePath = null,
            CancellationToken cancellationToken = default) =>
            inner.AddSteamAsync(game, coverImagePath, cancellationToken);
        public Task<LibraryItem> AddExecutableAsync(string name, string executablePath,
            string? arguments, string? workingDirectory, string? coverImagePath = null,
            CancellationToken cancellationToken = default) =>
            inner.AddExecutableAsync(name, executablePath, arguments, workingDirectory,
                coverImagePath, cancellationToken);
        public Task<LibraryState> SetManualOrderAsync(long baseRevision,
            IReadOnlyList<Guid> orderedPublishedAppIds,
            CancellationToken cancellationToken = default) =>
            inner.SetManualOrderAsync(baseRevision, orderedPublishedAppIds, cancellationToken);
        public Task SetPublishedToClientsAsync(Guid id, bool published,
            CancellationToken cancellationToken = default) =>
            inner.SetPublishedToClientsAsync(id, published, cancellationToken);
        public async Task<LibraryItem> UpdateSteamCoverAsync(Guid id,
            PortableGameIdentityV1 expectedPortableIdentity, string coverImagePath,
            CancellationToken cancellationToken = default)
        {
            await using var lease = new FileStream(
                syncPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await inner.UpdateSteamCoverAsync(
                id, expectedPortableIdentity, coverImagePath, cancellationToken);
        }
        public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.RemoveAsync(id, cancellationToken);
    }

    private static readonly JsonSerializerOptions Camel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
