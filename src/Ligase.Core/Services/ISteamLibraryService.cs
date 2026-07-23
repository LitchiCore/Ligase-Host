using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public interface ISteamLibraryService
{
    Task<IReadOnlyList<SteamGame>> DiscoverGamesAsync(CancellationToken cancellationToken = default);
}
