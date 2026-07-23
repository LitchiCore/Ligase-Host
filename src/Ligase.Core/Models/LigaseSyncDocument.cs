namespace Ligase.Host.Core.Models;

public sealed class LigaseSyncDocument
{
    public int SchemaVersion { get; init; } = 1;
    public required LibrarySyncState Library { get; init; }
    public required StreamingSettingsState Streaming { get; init; }
}

public sealed class LibrarySyncState
{
    public long Revision { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public LibrarySortMode SortMode { get; init; }
    public required IReadOnlyList<LibrarySyncItem> Items { get; init; }
}

public sealed class LibrarySyncItem
{
    public required Guid Id { get; init; }
    public required LibraryItemKind Kind { get; init; }
    public required string Name { get; init; }
    public uint? SteamAppId { get; init; }
    public required bool System { get; init; }
    public required DateTimeOffset AddedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? LastPlayedAt { get; init; }
}
