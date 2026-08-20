namespace Ligase.Host.Core.Models;

public sealed record ExistingItemCoverUpdateRequest(
    Guid LibraryItemId,
    PortableGameIdentityV1 PortableIdentity,
    CoverCandidate Candidate);

public sealed record ExistingItemCoverUpdateResult(
    Guid LibraryItemId,
    PortableGameIdentityV1 PortableIdentity,
    string CoverImagePath,
    string CoverContentSha256,
    string CoverSourceKind,
    string CoverSourceId,
    string CoverUsageRights,
    DateTimeOffset UpdatedAt,
    bool Idempotent,
    bool SupersededCoverCleanupCompleted,
    long LibraryRevision);

public sealed record ExistingItemCoverResetResult(
    Guid LibraryItemId,
    PortableGameIdentityV1 PortableIdentity,
    DateTimeOffset UpdatedAt,
    bool Idempotent,
    bool SupersededCoverCleanupCompleted,
    long LibraryRevision);

public sealed class ExistingItemCoverUpdateException(
    string code,
    string message,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}
