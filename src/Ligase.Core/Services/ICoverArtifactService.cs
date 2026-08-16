using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public interface ICoverArtifactService
{
    Task<PreparedCoverArtifact> PrepareAsync(
        CoverCandidate candidate,
        CancellationToken cancellationToken = default);

    Task RollbackAsync(
        PreparedCoverArtifact artifact,
        CancellationToken cancellationToken = default);

    Task PruneUnreferencedAsync(
        IReadOnlyCollection<LibraryItem> items,
        CancellationToken cancellationToken = default);
}

public sealed class PreparedCoverArtifact : IDisposable
{
    private readonly FileStream? _lease;
    private int _disposed;

    internal PreparedCoverArtifact(
        string path,
        CoverCacheAuthority authority,
        bool createdOwned,
        FileStream? lease)
    {
        Path = path;
        Authority = authority;
        CreatedOwned = createdOwned;
        _lease = lease;
    }

    public string Path { get; }
    public CoverCacheAuthority Authority { get; }
    public bool CreatedOwned { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _lease?.Dispose();
    }
}
