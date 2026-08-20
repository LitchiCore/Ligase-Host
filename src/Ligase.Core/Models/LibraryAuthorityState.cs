using Ligase.Host.Core.Services;

namespace Ligase.Host.Core.Models;

public enum LibraryAuthorityKind
{
    ManagedAuthoritative,
    ExternalUnknown,
    Ambiguous,
    Unavailable
}

public sealed record LibraryAuthorityState(
    LibraryAuthorityKind Kind,
    string Code,
    string Message,
    ApolloCoreEndpoint? Core = null)
{
    public bool CanWrite => Kind == LibraryAuthorityKind.ManagedAuthoritative;
}

public sealed record AuthorityReadbackApp(string Uuid, string AppId);

public sealed record AuthorityReadbackLibraryItem(
    string Id,
    string Kind,
    uint? SteamAppId,
    bool PublishedToClients,
    string? CoverSha256 = null,
    string? CoverSourceKind = null,
    string? CoverSourceId = null,
    string? CoverUsageRights = null,
    PortableGameIdentityV1? PortableIdentity = null,
    LayoutBindingV1? LayoutBinding = null);

public sealed record AuthorityReadbackDocument(
    int SchemaVersion,
    string AuthorityToken,
    string StartNonce,
    string RootFingerprint,
    string HostUniqueId,
    IReadOnlyList<AuthorityReadbackLibraryItem> LibraryItems,
    IReadOnlyList<AuthorityReadbackApp> Apps,
    long LibraryRevision = 0,
    string LibrarySortMode = "",
    IReadOnlyList<string>? LibraryOrder = null,
    AuthorityReloadResult? Reload = null);

public sealed record AuthorityReloadResult(
    int SchemaVersion,
    string ResultCode,
    string Stage,
    string ReasonCode,
    long ElapsedMs);
