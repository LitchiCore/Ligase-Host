using Ligase.Host.Core.Application.LayoutCatalog;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed class LibraryMutationCoordinator(
    LigasePaths paths,
    IApplicationLibrary repository,
    ILibraryAuthorityService authorityService,
    ICoverArtifactService? coverArtService = null,
    LayoutCatalogService? layoutCatalogService = null)
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

    public Task<LibraryItem> AddSteamAsync(
        SteamGame game,
        string? coverImagePath = null,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
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
            (_, token) => repository.SetManualOrderAsync(
                baseRevision, orderedPublishedAppIds, token),
            static (readback, state) => MatchesCanonicalOrder(readback, state),
            cancellationToken);

    public Task SetPublishedToClientsAsync(
        Guid id,
        bool published,
        CancellationToken cancellationToken = default) =>
        MutateAsync<object?>(
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
            async token =>
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
        Func<ApolloCoreEndpoint, CancellationToken, Task<T>> mutation,
        Func<AuthorityReadbackDocument, T, bool> verify,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var authority = await authorityService.GetStateAsync(cancellationToken);
            if (!authority.CanWrite || authority.Core is null)
                throw new LibraryAuthorityException(authority.Code, authority.Message);

            var before = await authorityService.RequireReadbackAsync(
                authority.Core,
                reload: false,
                cancellationToken);
            var snapshots = SnapshotFiles();
            try
            {
                var result = await mutation(authority.Core, cancellationToken);
                var readback = await authorityService.RequireReadbackAsync(
                    authority.Core,
                    reload: true,
                    cancellationToken);
                if (!string.Equals(
                        readback.HostUniqueId,
                        authority.Core.UniqueId,
                        StringComparison.OrdinalIgnoreCase) ||
                    !verify(readback, result))
                    throw new InvalidOperationException(
                        "核心读取到的游戏库与刚才的修改不一致。已恢复更新前状态，请重新启动 Ligase Host 后再试。");
                return result;
            }
            catch
            {
                RestoreFiles(snapshots);
                var restored = await authorityService.RequireReadbackAsync(
                    authority.Core,
                    reload: true,
                    CancellationToken.None);
                if (!EquivalentProjection(before, restored))
                    throw new InvalidOperationException(
                        "游戏库回滚后核心仍未恢复原状态。请保持 Ligase Host 运行并联系支持。");
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

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

public sealed class LibraryAuthorityException(string code, string message)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
