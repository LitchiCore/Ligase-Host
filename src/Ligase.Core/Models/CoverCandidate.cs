namespace Ligase.Host.Core.Models;

public sealed record CoverCandidate(
    string Name,
    string Key,
    string PreviewUrl,
    string DownloadUrl,
    string SourceKind = "gameDbIgdb",
    string SourceId = "",
    string UsageRights = "thirdPartyArtworkLocalCacheOnly",
    uint? SteamAppId = null);

public sealed record CoverCacheAuthority(
    string RelativePath,
    string ContentSha256,
    string SourceKind,
    string SourceId,
    string UsageRights,
    uint? SteamAppId,
    DateTimeOffset CachedAtUtc);
