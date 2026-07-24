using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed class LibraryMutationCoordinator(
    LigasePaths paths,
    IApplicationLibrary repository,
    ILibraryAuthorityService authorityService)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string[] _projectionFiles =
    [
        paths.LibraryFile,
        paths.SyncFile,
        paths.ApolloAppsFile
    ];

    public Task<LibraryItem> AddSteamAsync(
        SteamGame game,
        string? coverImagePath = null,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            token => repository.AddSteamAsync(game, coverImagePath, token),
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
            token => repository.AddExecutableAsync(
                name, executablePath, arguments, workingDirectory, coverImagePath, token),
            (readback, item) =>
                HasPublishedItem(readback, item) && HasLaunchMapping(readback, item.Id),
            cancellationToken);

    public Task SetManualOrderAsync(
        long baseRevision,
        IReadOnlyList<Guid> orderedPublishedAppIds,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            token => repository.SetManualOrderAsync(
                baseRevision, orderedPublishedAppIds, token),
            static (readback, state) => MatchesCanonicalOrder(readback, state),
            cancellationToken);

    public Task SetPublishedToClientsAsync(
        Guid id,
        bool published,
        CancellationToken cancellationToken = default) =>
        MutateAsync<object?>(
            async token =>
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

    public Task RemoveAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        MutateAsync<object?>(
            async token =>
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

    private async Task<T> MutateAsync<T>(
        Func<CancellationToken, Task<T>> mutation,
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
                var result = await mutation(cancellationToken);
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
            candidate.PublishedToClients);

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
