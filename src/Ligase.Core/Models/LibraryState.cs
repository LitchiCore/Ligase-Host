namespace Ligase.Host.Core.Models;

public sealed class LibraryState
{
    public int SchemaVersion { get; init; } = 1;
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public LibrarySortMode SortMode { get; set; } = LibrarySortMode.NameAscending;
    public List<LibraryItem> Items { get; init; } = [];
}
