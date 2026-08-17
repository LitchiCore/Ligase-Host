using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;

namespace Ligase.Host.Desktop.ViewModels;

public interface IExistingItemCoverWorkflow
{
    Task<IReadOnlyList<CoverCandidate>> FindVerifiedAsync(
        Guid libraryItemId,
        uint steamAppId,
        CancellationToken cancellationToken = default);

    Task<ExistingItemCoverUpdateResult> ApplyAsync(
        Guid libraryItemId,
        uint steamAppId,
        CoverCandidate candidate,
        CancellationToken cancellationToken = default);
}

public sealed class ExistingItemCoverViewModel(
    IApplicationLibrary applicationLibrary,
    ISteamLibraryService steamLibraryService,
    CoverArtService coverArtService,
    LibraryMutationCoordinator mutationCoordinator) : IExistingItemCoverWorkflow
{
    public async Task<IReadOnlyList<CoverCandidate>> FindVerifiedAsync(
        Guid libraryItemId,
        uint steamAppId,
        CancellationToken cancellationToken = default)
    {
        var item = (await applicationLibrary.LoadAsync(cancellationToken)).Items
            .SingleOrDefault(candidate => candidate.Id == libraryItemId);
        if (item is null || item.SteamAppId != steamAppId)
            throw new ExistingItemCoverUpdateException(
                "libraryIdentityChanged",
                "游戏库项目已变化，请刷新后再选择封面。");
        return await FindVerifiedAsync(item, cancellationToken);
    }

    public async Task<IReadOnlyList<CoverCandidate>> FindVerifiedAsync(
        LibraryItem item,
        CancellationToken cancellationToken = default)
    {
        if (item.Kind != LibraryItemKind.Steam ||
            item.SteamAppId is not uint appId ||
            item.PortableIdentity is not { Provider: "steam" } portableIdentity ||
            !string.Equals(portableIdentity.Id,
                appId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
            throw new ExistingItemCoverUpdateException(
                "libraryIdentityUnavailable",
                "当前项目没有可验证的 Steam 身份，不能更新封面。");

        var current = (await applicationLibrary.LoadAsync(cancellationToken)).Items
            .SingleOrDefault(candidate => candidate.Id == item.Id);
        if (current is null ||
            current.SteamAppId != appId ||
            current.PortableIdentity != portableIdentity)
            throw new ExistingItemCoverUpdateException(
                "libraryIdentityChanged",
                "游戏库项目已变化，请刷新后再选择封面。");

        var game = (await steamLibraryService.DiscoverGamesAsync(cancellationToken))
            .SingleOrDefault(candidate => candidate.AppId == appId)
            ?? throw new ExistingItemCoverUpdateException(
                "steamManifestUnavailable",
                $"Steam App ID {appId} 的本机 manifest 不可用，不能建立封面来源。");
        var candidates = await coverArtService.FindSteamAsync(game, cancellationToken);
        if (candidates.Count > 1 || candidates.Any(candidate =>
                candidate.SteamAppId != appId ||
                !string.Equals(candidate.SourceKind,
                    "steamClientLibraryCache", StringComparison.Ordinal) ||
                !string.Equals(candidate.SourceId, portableIdentity.Id, StringComparison.Ordinal)))
            throw new ExistingItemCoverUpdateException(
                "coverCandidateSetInvalid",
                "Steam 封面来源集合不唯一或与当前游戏不一致。");
        return candidates;
    }

    public Task<ExistingItemCoverUpdateResult> ApplyAsync(
        LibraryItem item,
        CoverCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (item.PortableIdentity is null)
            throw new ExistingItemCoverUpdateException(
                "libraryIdentityUnavailable",
                "当前项目没有可验证的 Steam 身份，不能更新封面。");
        return mutationCoordinator.UpdateExistingSteamCoverAsync(
            new ExistingItemCoverUpdateRequest(
                item.Id,
                item.PortableIdentity,
                candidate),
            cancellationToken);
    }

    public async Task<ExistingItemCoverUpdateResult> ApplyAsync(
        Guid libraryItemId,
        uint steamAppId,
        CoverCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        var item = (await applicationLibrary.LoadAsync(cancellationToken)).Items
            .SingleOrDefault(value => value.Id == libraryItemId);
        if (item is null || item.SteamAppId != steamAppId || item.PortableIdentity is null)
            throw new ExistingItemCoverUpdateException(
                "libraryIdentityChanged",
                "游戏库项目已变化，请刷新后再选择封面。");
        return await ApplyAsync(item, candidate, cancellationToken);
    }
}
