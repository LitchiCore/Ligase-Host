using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public interface IApplicationLibrary
{
    Task<LibraryState> LoadAsync(CancellationToken cancellationToken = default);
    Task<LibraryItem> AddSteamAsync(
        SteamGame game,
        string? coverImagePath = null,
        CancellationToken cancellationToken = default);
    Task<LibraryItem> AddExecutableAsync(
        string name,
        string executablePath,
        string? arguments,
        string? workingDirectory,
        string? coverImagePath = null,
        CancellationToken cancellationToken = default);
    Task SetSortModeAsync(LibrarySortMode sortMode, CancellationToken cancellationToken = default);
    Task SetPublishedToClientsAsync(
        Guid id,
        bool published,
        CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
