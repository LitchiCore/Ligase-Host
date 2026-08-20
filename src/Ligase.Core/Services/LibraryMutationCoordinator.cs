using Ligase.Host.Core.Application.LayoutCatalog;
using Ligase.Host.Core.Models;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ligase.Host.Core.Services;

public sealed class LibraryMutationCoordinator(
    LigasePaths paths,
    IApplicationLibrary repository,
    ILibraryAuthorityService authorityService,
    ICoverArtifactService? coverArtService = null,
    LayoutCatalogService? layoutCatalogService = null,
    ILibraryMutationOutcomeStore? outcomeStore = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string[] _projectionFiles =
    [
        paths.LibraryFile,
        paths.SyncFile,
        paths.ApolloAppsFile,
        paths.LayoutCatalogFile,
        paths.CoverCacheAuthorityFile
    ];
    private readonly ILibraryMutationOutcomeStore _outcomeStore =
        outcomeStore ?? new AtomicLibraryMutationOutcomeStore(paths);

    public LibraryMutationPersistenceStatus LastOutcomePersistenceStatus { get; private set; } =
        LibraryMutationPersistenceStatus.NotAttempted;

    public Task<LibraryItem> AddSteamAsync(
        SteamGame game,
        string? coverImagePath = null,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            "addSteam",
            (_, token) => repository.AddSteamAsync(game, coverImagePath, token),
            (readback, item) => HasPublishedItem(readback, item) &&
                                readback.LibraryItems.Single(candidate =>
                                    candidate.Id.Equals(item.Id.ToString("D"), StringComparison.OrdinalIgnoreCase))
                                    .SteamAppId == game.AppId &&
                                HasLaunchMapping(readback, item.Id),
            cancellationToken);

    public Task<LibraryItem> AddExecutableAsync(
        string name,
        string executablePath,
        string? arguments,
        string? workingDirectory,
        string? coverImagePath = null,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            "addExecutable",
            (_, token) => repository.AddExecutableAsync(
                name, executablePath, arguments, workingDirectory, coverImagePath, token),
            (readback, item) =>
                HasPublishedItem(readback, item) && HasLaunchMapping(readback, item.Id),
            cancellationToken);

    public Task SetManualOrderAsync(
        long baseRevision,
        IReadOnlyList<Guid> orderedPublishedAppIds,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            "setManualOrder",
            (_, token) => repository.SetManualOrderAsync(
                baseRevision, orderedPublishedAppIds, token),
            static (readback, state) => MatchesCanonicalOrder(readback, state),
            cancellationToken);

    public Task SetPublishedToClientsAsync(
        Guid id,
        bool published,
        CancellationToken cancellationToken = default) =>
        MutateAsync<object?>(
            "setPublished",
            async (_, token) =>
            {
                await repository.SetPublishedToClientsAsync(id, published, token);
                return null;
            },
            (readback, _) =>
            {
                var item = readback.LibraryItems.SingleOrDefault(candidate =>
                    candidate.Id.Equals(id.ToString("D"), StringComparison.OrdinalIgnoreCase));
                return item is not null &&
                       item.PublishedToClients == published &&
                       (!published || HasLaunchMapping(readback, id));
            },
            cancellationToken);

    public async Task RemoveAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await MutateAsync<object?>(
            "remove",
            async (_, token) =>
            {
                await repository.RemoveAsync(id, token);
                return null;
            },
            (readback, _) =>
                readback.LibraryItems.All(candidate =>
                    !candidate.Id.Equals(id.ToString("D"), StringComparison.OrdinalIgnoreCase)) &&
                readback.Apps.All(candidate =>
                    !candidate.Uuid.Equals(id.ToString("D"), StringComparison.OrdinalIgnoreCase)),
            cancellationToken);
        if (coverArtService is not null)
        {
            var state = await repository.LoadAsync(cancellationToken);
            await coverArtService.PruneUnreferencedAsync(state.Items, cancellationToken);
        }
    }

    public Task<LibraryItem> SetLayoutBindingAsync(
        Guid id,
        LayoutBindingV1? binding,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            "setLayoutBinding",
            async (core, token) =>
            {
                if (layoutCatalogService is null)
                    throw new InvalidOperationException("布局目录服务不可用，未修改游戏库。");
                var item = await repository.SetLayoutBindingAsync(id, binding, token);
                await layoutCatalogService.SetBindingAsync(
                    core.UniqueId,
                    id,
                    binding,
                    token);
                return item;
            },
            (readback, item) =>
            {
                var projected = readback.LibraryItems.SingleOrDefault(candidate =>
                    candidate.Id.Equals(item.Id.ToString("D"), StringComparison.OrdinalIgnoreCase));
                return projected is not null &&
                       Equals(projected.PortableIdentity, item.PortableIdentity) &&
                       Equals(projected.LayoutBinding, item.LayoutBinding);
            },
            cancellationToken);

    public async Task<ExistingItemCoverUpdateResult> UpdateExistingSteamCoverAsync(
        ExistingItemCoverUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (coverArtService is null)
            throw new ExistingItemCoverUpdateException(
                "coverServiceUnavailable",
                "封面服务不可用，游戏库未发生变化。");
        if (!LayoutContractV1Validator.TryNormalizePortableIdentity(
                request.PortableIdentity,
                out var normalized) ||
            normalized != request.PortableIdentity ||
            !string.Equals(normalized.Provider, "steam", StringComparison.Ordinal) ||
            !uint.TryParse(normalized.Id,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var appId) ||
            request.Candidate.SteamAppId != appId ||
            !string.Equals(request.Candidate.SourceKind,
                "steamClientLibraryCache", StringComparison.Ordinal) ||
            !string.Equals(request.Candidate.SourceId, normalized.Id, StringComparison.Ordinal))
            throw new ExistingItemCoverUpdateException(
                "coverRequestCorrelationMismatch",
                "所选封面与当前游戏 UUID、Steam App ID 或来源不一致，未更新游戏库。");

        PreparedCoverArtifact? prepared = null;
        CoverMutationOutcome outcome;
        try
        {
            outcome = await MutateAsync(
                "updateSteamCover",
                async (_, token) =>
                {
                    var beforeState = await repository.LoadAsync(token);
                    var beforeItem = beforeState.Items.SingleOrDefault(candidate =>
                        candidate.Id == request.LibraryItemId)
                        ?? throw new LibraryItemNotFoundException(request.LibraryItemId);
                    if (beforeItem.Kind != LibraryItemKind.Steam ||
                        beforeItem.SteamAppId != appId ||
                        beforeItem.PortableIdentity != normalized)
                        throw new ExistingItemCoverUpdateException(
                            "libraryIdentityChanged",
                            "游戏身份已变化，未更新封面。请刷新游戏库后重试。");

                    prepared = await coverArtService.PrepareAsync(request.Candidate, token);
                    var authority = prepared.Authority;
                    if (authority.SteamAppId != appId ||
                        !string.Equals(authority.SourceKind,
                            request.Candidate.SourceKind, StringComparison.Ordinal) ||
                        !string.Equals(authority.SourceId,
                            request.Candidate.SourceId, StringComparison.Ordinal) ||
                        !string.Equals(authority.UsageRights,
                            request.Candidate.UsageRights, StringComparison.Ordinal))
                        throw new ExistingItemCoverUpdateException(
                            "preparedCoverCorrelationMismatch",
                            "封面落盘后的来源凭据不一致，已取消更新。");

                    var idempotent = string.Equals(
                        beforeItem.CoverContentSha256,
                        authority.ContentSha256,
                        StringComparison.Ordinal) &&
                        string.Equals(beforeItem.CoverImagePath,
                            prepared.Path, StringComparison.OrdinalIgnoreCase);
                    var updated = idempotent
                        ? beforeItem
                        : await repository.UpdateSteamCoverAsync(
                            request.LibraryItemId,
                            normalized,
                            prepared.Path,
                            token);
                    var projectedItems = beforeState.Items
                        .Select(item => item.Id == updated.Id ? updated : item)
                        .ToArray();
                    return new CoverMutationOutcome(
                        updated,
                        idempotent,
                        idempotent ? beforeState.Revision : beforeState.Revision + 1,
                        projectedItems);
                },
                (readback, outcome) =>
                    outcome.Updated.Id == request.LibraryItemId &&
                    outcome.Updated.PortableIdentity == normalized &&
                    readback.LibraryRevision == outcome.Revision &&
                    HasPublishedItem(readback, outcome.Updated) &&
                    HasLaunchMapping(readback, outcome.Updated.Id),
                cancellationToken);

            prepared!.Dispose();
            prepared = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (prepared is not null)
            {
                try
                {
                    await coverArtService.RollbackAsync(prepared, CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    throw new ExistingItemCoverUpdateException(
                        "coverRollbackUnproven",
                        "封面更新失败，且无法证明新缓存已清理；原游戏库投影已恢复，请联系支持。",
                        new AggregateException(exception, rollbackException));
                }
            }
            if (exception is ExistingItemCoverUpdateException) throw;
            throw new ExistingItemCoverUpdateException(
                "coverUpdateFailed",
                "封面没有保存；游戏库和客户端同步已恢复到更新前状态。",
                exception);
        }
        finally
        {
            prepared?.Dispose();
        }

        var cleanupCompleted = true;
        try
        {
            await coverArtService.PruneUnreferencedAsync(
                outcome.ProjectedItems, CancellationToken.None);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            // The library, Apollo and Sync transaction is already committed and read back.
            // Retaining an old, now-unreferenced owned cache file is safer than reporting
            // that the persisted cover was rolled back when it was not.
            cleanupCompleted = false;
        }
        var item = outcome.Updated;
        return new ExistingItemCoverUpdateResult(
            item.Id,
            item.PortableIdentity!,
            item.CoverImagePath!,
            item.CoverContentSha256!,
            item.CoverSourceKind!,
            item.CoverSourceId!,
            item.CoverUsageRights!,
            item.UpdatedAt,
            outcome.Idempotent,
            cleanupCompleted,
            outcome.Revision);
    }

    public async Task<ExistingItemCoverResetResult> ResetExistingSteamCoverAsync(
        Guid libraryItemId,
        PortableGameIdentityV1 portableIdentity,
        CancellationToken cancellationToken = default)
    {
        if (!LayoutContractV1Validator.TryNormalizePortableIdentity(
                portableIdentity, out var normalized) ||
            normalized != portableIdentity ||
            !string.Equals(normalized.Provider, "steam", StringComparison.Ordinal))
            throw new ExistingItemCoverUpdateException(
                "coverRequestCorrelationMismatch",
                "当前游戏身份无效，未恢复默认封面。");

        var outcome = await MutateAsync(
            "resetSteamCover",
            async (_, token) =>
            {
                var beforeState = await repository.LoadAsync(token);
                var beforeItem = beforeState.Items.SingleOrDefault(item =>
                    item.Id == libraryItemId)
                    ?? throw new LibraryItemNotFoundException(libraryItemId);
                if (beforeItem.PortableIdentity != normalized)
                    throw new ExistingItemCoverUpdateException(
                        "libraryIdentityChanged",
                        "游戏身份已变化，未恢复默认封面。");
                var idempotent = beforeItem.CoverImagePath is null &&
                    beforeItem.CoverContentSha256 is null &&
                    beforeItem.CoverSourceKind is null &&
                    beforeItem.CoverSourceId is null &&
                    beforeItem.CoverUsageRights is null;
                var updated = idempotent
                    ? beforeItem
                    : await repository.ResetSteamCoverAsync(
                        libraryItemId, normalized, token);
                return new CoverMutationOutcome(
                    updated,
                    idempotent,
                    idempotent ? beforeState.Revision : beforeState.Revision + 1,
                    beforeState.Items.Select(item =>
                        item.Id == updated.Id ? updated : item).ToArray());
            },
            (readback, value) =>
                value.Updated.Id == libraryItemId &&
                value.Updated.PortableIdentity == normalized &&
                value.Updated.CoverImagePath is null &&
                value.Updated.CoverContentSha256 is null &&
                value.Updated.CoverSourceKind is null &&
                value.Updated.CoverSourceId is null &&
                value.Updated.CoverUsageRights is null &&
                readback.LibraryRevision == value.Revision &&
                HasPublishedItem(readback, value.Updated) &&
                HasLaunchMapping(readback, value.Updated.Id),
            cancellationToken);

        var cleanupCompleted = true;
        if (coverArtService is not null)
        {
            try
            {
                await coverArtService.PruneUnreferencedAsync(
                    outcome.ProjectedItems, CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                cleanupCompleted = false;
            }
        }
        return new ExistingItemCoverResetResult(
            outcome.Updated.Id,
            outcome.Updated.PortableIdentity!,
            outcome.Updated.UpdatedAt,
            outcome.Idempotent,
            cleanupCompleted,
            outcome.Revision);
    }

    private sealed record CoverMutationOutcome(
        LibraryItem Updated,
        bool Idempotent,
        long Revision,
        IReadOnlyCollection<LibraryItem> ProjectedItems);

    private async Task<T> MutateAsync<T>(
        string operation,
        Func<ApolloCoreEndpoint, CancellationToken, Task<T>> mutation,
        Func<AuthorityReadbackDocument, T, bool> verify,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            LastOutcomePersistenceStatus = LibraryMutationPersistenceStatus.NotAttempted;
            var authority = await authorityService.GetStateAsync(cancellationToken);
            if (!authority.CanWrite || authority.Core is null)
                throw new LibraryAuthorityException(authority.Code, authority.Message);

            var before = await authorityService.RequireReadbackAsync(
                authority.Core,
                reload: false,
                cancellationToken);
            var snapshots = SnapshotFiles();
            T result;
            AuthorityReadbackDocument readback;
            try
            {
                result = await mutation(authority.Core, cancellationToken);
                readback = await authorityService.RequireReadbackAsync(
                    authority.Core,
                    reload: true,
                    cancellationToken);
                if (!string.Equals(
                        readback.HostUniqueId,
                        authority.Core.UniqueId,
                        StringComparison.OrdinalIgnoreCase) ||
                    !verify(readback, result))
                    throw new LibraryProjectionMismatchException(
                        "核心读取到的游戏库与刚才的修改不一致。已恢复更新前状态，请重新启动 Ligase Host 后再试。");
            }
            catch (Exception primary)
            {
                var primaryDispatch = ExceptionDispatchInfo.Capture(primary);
                LibraryMutationOutcomeDocument outcome;
                try
                {
                    RestoreFiles(snapshots);
                    var restored = await authorityService.RequireReadbackAsync(
                        authority.Core,
                        reload: true,
                        CancellationToken.None);
                    if (!EquivalentProjection(before, restored))
                    {
                        outcome = new LibraryMutationOutcomeDocument(
                            1, operation, "rollbackUnproven", AttemptFromException(primary, operation),
                            new LibraryMutationAttempt(
                                "projectionMismatch", "rollback.verify", "projectionMismatch",
                                null, restored.Reload?.ElapsedMs), DateTimeOffset.UtcNow);
                        AttachSecondary(primary, "rollback.verify", "projectionMismatch");
                    }
                    else
                    {
                        outcome = new LibraryMutationOutcomeDocument(
                            1, operation, "rolledBack", AttemptFromException(primary, operation),
                            AttemptFromReadback(restored), DateTimeOffset.UtcNow);
                    }
                }
                catch (Exception rollbackFailure) when (!ReferenceEquals(rollbackFailure, primary))
                {
                    AttachSecondary(primary, "rollback.mutation", "mutationFailed");
                    outcome = new LibraryMutationOutcomeDocument(
                        1, operation, "rollbackUnproven", AttemptFromException(primary, operation),
                        AttemptFromException(rollbackFailure, "rollback"), DateTimeOffset.UtcNow);
                }
                PersistOutcome(outcome, primary);
                primaryDispatch.Throw();
                throw;
            }

            PersistOutcome(new LibraryMutationOutcomeDocument(
                1,
                operation,
                "committed",
                AttemptFromReadback(readback),
                LibraryMutationAttempt.NotAttempted,
                DateTimeOffset.UtcNow));
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void PersistOutcome(
        LibraryMutationOutcomeDocument outcome,
        Exception? primary = null)
    {
        try
        {
            LibraryMutationOutcomeSemanticValidator.Validate(outcome);
            var readback = _outcomeStore.PersistAndReadback(outcome);
            LibraryMutationOutcomeSemanticValidator.Validate(readback);
            if (readback != outcome)
                throw new InvalidDataException("library mutation outcome readback mismatch");
            LastOutcomePersistenceStatus = new(
                "completed", outcome.State, "none");
        }
        catch (Exception persistenceFailure) when (
            persistenceFailure is not OperationCanceledException)
        {
            LastOutcomePersistenceStatus = new(
                "persistenceUnavailable", "unknown", "writeOrReadbackFailed");
            if (primary is not null)
                AttachSecondary(primary, "outcome.persistence", "writeOrReadbackFailed");
        }
    }

    internal static IReadOnlyList<LibraryMutationSecondaryFailure> SecondaryFailures(
        Exception exception)
    {
        const string key = "Ligase.LibraryMutation.SecondaryFailures";
        return exception.Data[key] as IReadOnlyList<LibraryMutationSecondaryFailure> ?? [];
    }

    private static void AttachSecondary(Exception primary, string stage, string reasonCode)
    {
        const string key = "Ligase.LibraryMutation.SecondaryFailures";
        var failures = primary.Data[key] as List<LibraryMutationSecondaryFailure> ?? [];
        failures.Add(new(stage, reasonCode));
        primary.Data[key] = failures;
    }

    private static LibraryMutationAttempt AttemptFromReadback(AuthorityReadbackDocument readback) =>
        readback.Reload is { } reload
            ? new LibraryMutationAttempt(
                reload.ResultCode, $"reload.{reload.Stage}", reload.ReasonCode,
                200, reload.ElapsedMs)
            : new LibraryMutationAttempt("completed", "reload.readback", "none", 200, 0);

    private static LibraryMutationAttempt AttemptFromException(
        Exception exception,
        string operation) =>
        exception is LibraryAuthorityReadbackException typed
            ? new LibraryMutationAttempt(
                typed.Failure.ResultCode,
                typed.Failure.Stage,
                typed.Failure.ReasonCode,
                typed.Failure.HttpStatusCode,
                typed.Failure.ElapsedMs)
            : exception is LibraryProjectionMismatchException
                ? new LibraryMutationAttempt(
                    "projectionMismatch", "reload.readback", "projectionMismatch", null, null)
            : new LibraryMutationAttempt(
                "failed", $"{operation}.mutation", "mutationFailed", null, null);

    private Dictionary<string, byte[]?> SnapshotFiles() =>
        _projectionFiles.ToDictionary(
            path => path,
            path => File.Exists(path) ? File.ReadAllBytes(path) : null,
            StringComparer.OrdinalIgnoreCase);

    private static void RestoreFiles(IReadOnlyDictionary<string, byte[]?> snapshots)
    {
        foreach (var (path, content) in snapshots)
        {
            if (content is null)
            {
                if (File.Exists(path)) File.Delete(path);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".rollback.tmp";
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, true);
        }
    }

    private static bool HasPublishedItem(
        AuthorityReadbackDocument readback,
        LibraryItem item) =>
        readback.LibraryItems.Any(candidate =>
            candidate.Id.Equals(item.Id.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
            candidate.Kind.Equals(item.Kind.ToString(), StringComparison.OrdinalIgnoreCase) &&
            candidate.PublishedToClients &&
            string.Equals(candidate.CoverSha256, item.CoverContentSha256, StringComparison.Ordinal) &&
            string.Equals(candidate.CoverSourceKind, item.CoverSourceKind, StringComparison.Ordinal) &&
            string.Equals(candidate.CoverSourceId, item.CoverSourceId, StringComparison.Ordinal) &&
            string.Equals(candidate.CoverUsageRights, item.CoverUsageRights, StringComparison.Ordinal) &&
            Equals(candidate.PortableIdentity, item.PortableIdentity) &&
            Equals(candidate.LayoutBinding, item.LayoutBinding));

    private static bool HasLaunchMapping(
        AuthorityReadbackDocument readback,
        Guid id) =>
        readback.Apps.Any(candidate =>
            candidate.Uuid.Equals(id.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(candidate.AppId, out _));

    internal static bool MatchesCanonicalOrder(
        AuthorityReadbackDocument readback,
        LibraryState state) =>
        readback.LibraryRevision == state.Revision &&
        string.Equals(
            readback.LibrarySortMode,
            ToContractSortMode(state.SortMode),
            StringComparison.Ordinal) &&
        (readback.LibraryOrder ?? []).SequenceEqual(
            state.Items.Select(item => item.Id.ToString("D")),
            StringComparer.OrdinalIgnoreCase);

    private static string ToContractSortMode(LibrarySortMode mode) =>
        mode switch
        {
            LibrarySortMode.NameAscending => "nameAscending",
            LibrarySortMode.NameDescending => "nameDescending",
            LibrarySortMode.AddedNewest => "addedNewest",
            LibrarySortMode.AddedOldest => "addedOldest",
            LibrarySortMode.LastPlayedNewest => "lastPlayedNewest",
            LibrarySortMode.Manual => "manual",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    private static bool EquivalentProjection(
        AuthorityReadbackDocument expected,
        AuthorityReadbackDocument actual) =>
        string.Equals(expected.HostUniqueId, actual.HostUniqueId, StringComparison.OrdinalIgnoreCase) &&
        expected.LibraryRevision == actual.LibraryRevision &&
        string.Equals(expected.LibrarySortMode, actual.LibrarySortMode, StringComparison.Ordinal) &&
        (expected.LibraryOrder ?? []).SequenceEqual(
            actual.LibraryOrder ?? [],
            StringComparer.OrdinalIgnoreCase) &&
        expected.LibraryItems
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(actual.LibraryItems.OrderBy(
                item => item.Id,
                StringComparer.OrdinalIgnoreCase)) &&
        expected.Apps
            .OrderBy(item => item.Uuid, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(actual.Apps.OrderBy(
                item => item.Uuid,
                StringComparer.OrdinalIgnoreCase));
}

public interface ILibraryMutationOutcomeStore
{
    LibraryMutationOutcomeDocument PersistAndReadback(LibraryMutationOutcomeDocument outcome);
}

public sealed class AtomicLibraryMutationOutcomeStore : ILibraryMutationOutcomeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly LigasePaths _paths;
    private readonly Action<LibraryMutationOutcomeStoreStage>? _fault;

    public AtomicLibraryMutationOutcomeStore(LigasePaths paths) : this(paths, null) { }

    internal AtomicLibraryMutationOutcomeStore(
        LigasePaths paths,
        Action<LibraryMutationOutcomeStoreStage>? fault)
    {
        _paths = paths;
        _fault = fault;
    }

    public LibraryMutationOutcomeDocument PersistAndReadback(LibraryMutationOutcomeDocument outcome)
    {
        LibraryMutationOutcomeSemanticValidator.Validate(outcome);
        Directory.CreateDirectory(_paths.RootDirectory);
        var directory = Path.GetDirectoryName(_paths.LibraryMutationOutcomeFile)!;
        var temporary = Path.Combine(directory,
            $".{Path.GetFileName(_paths.LibraryMutationOutcomeFile)}.{Guid.NewGuid():N}.tmp");
        LibraryMutationOutcomeStoreStage stage = LibraryMutationOutcomeStoreStage.CreateTemporary;
        Exception? failure = null;
        Exception? cleanupFailure = null;
        LibraryMutationOutcomeDocument? readback = null;
        try
        {
            var bytes = new UTF8Encoding(false, true).GetBytes(
                JsonSerializer.Serialize(outcome, JsonOptions));
            Invoke(stage);
            using (var stream = new FileStream(temporary, new FileStreamOptions
                   {
                       Mode = FileMode.CreateNew,
                       Access = FileAccess.ReadWrite,
                       Share = FileShare.None,
                       BufferSize = 4096,
                       Options = FileOptions.WriteThrough
                   }))
            {
                stage = LibraryMutationOutcomeStoreStage.WriteTemporary;
                Invoke(stage);
                stream.Write(bytes);
                stage = LibraryMutationOutcomeStoreStage.FlushTemporary;
                Invoke(stage);
                stream.Flush(flushToDisk: true);
                stage = LibraryMutationOutcomeStoreStage.ReadbackTemporary;
                Invoke(stage);
                var temporaryBytes = ReadAll(stream, bytes.Length);
                if (!SHA256.HashData(temporaryBytes).SequenceEqual(SHA256.HashData(bytes)))
                    throw new InvalidDataException("temporary outcome hash mismatch");
            }

            stage = LibraryMutationOutcomeStoreStage.CommitReplace;
            Invoke(stage);
            if (File.Exists(_paths.LibraryMutationOutcomeFile))
                File.Replace(temporary, _paths.LibraryMutationOutcomeFile, null,
                    ignoreMetadataErrors: true);
            else
                File.Move(temporary, _paths.LibraryMutationOutcomeFile, overwrite: false);

            stage = LibraryMutationOutcomeStoreStage.ReadbackCommitted;
            Invoke(stage);
            using var committed = new FileStream(
                _paths.LibraryMutationOutcomeFile, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.SequentialScan);
            var committedBytes = ReadAll(committed, checked((int)committed.Length));
            if (!SHA256.HashData(committedBytes).SequenceEqual(SHA256.HashData(bytes)))
                throw new InvalidDataException("committed outcome hash mismatch");
            readback = JsonSerializer.Deserialize<LibraryMutationOutcomeDocument>(
                committedBytes, JsonOptions)
                ?? throw new InvalidDataException("library mutation outcome readback was empty");
            stage = LibraryMutationOutcomeStoreStage.ValidateCommitted;
            Invoke(stage);
            LibraryMutationOutcomeSemanticValidator.Validate(readback);
            if (readback != outcome)
                throw new InvalidDataException("library mutation outcome readback mismatch");
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (File.Exists(temporary))
        {
            try
            {
                Invoke(LibraryMutationOutcomeStoreStage.CleanupTemporary);
                File.Delete(temporary);
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
        }

        if (failure is not null || cleanupFailure is not null)
            throw new LibraryMutationOutcomeStoreException(
                failure is null ? LibraryMutationOutcomeStoreStage.CleanupTemporary : stage,
                cleanupFailure is not null,
                failure is null ? cleanupFailure! :
                    cleanupFailure is null ? failure : new AggregateException(failure, cleanupFailure));
        return readback!;
    }

    private void Invoke(LibraryMutationOutcomeStoreStage stage) => _fault?.Invoke(stage);

    private static byte[] ReadAll(FileStream stream, int expectedLength)
    {
        stream.Position = 0;
        var bytes = new byte[expectedLength];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) throw new EndOfStreamException("outcome readback ended early");
            offset += read;
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("outcome readback grew");
        return bytes;
    }
}

internal enum LibraryMutationOutcomeStoreStage
{
    CreateTemporary,
    WriteTemporary,
    FlushTemporary,
    ReadbackTemporary,
    CommitReplace,
    ReadbackCommitted,
    ValidateCommitted,
    CleanupTemporary
}

internal sealed class LibraryMutationOutcomeStoreException(
    LibraryMutationOutcomeStoreStage stage,
    bool cleanupFailed,
    Exception innerException)
    : IOException($"library mutation outcome persistence failed at {stage}", innerException)
{
    public LibraryMutationOutcomeStoreStage Stage { get; } = stage;
    public bool CleanupFailed { get; } = cleanupFailed;
}

public sealed class LibraryAuthorityException(string code, string message)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

internal sealed class LibraryProjectionMismatchException(string message)
    : InvalidOperationException(message);
