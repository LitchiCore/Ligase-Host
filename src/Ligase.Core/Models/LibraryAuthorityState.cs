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
    bool PublishedToClients);

public sealed record AuthorityReadbackDocument(
    int SchemaVersion,
    string AuthorityToken,
    string StartNonce,
    string RootFingerprint,
    string HostUniqueId,
    IReadOnlyList<AuthorityReadbackLibraryItem> LibraryItems,
    IReadOnlyList<AuthorityReadbackApp> Apps);
