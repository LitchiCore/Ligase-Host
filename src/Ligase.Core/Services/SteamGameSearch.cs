using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public static class SteamGameSearch
{
    public static bool Matches(SteamGame game, string? query)
    {
        ArgumentNullException.ThrowIfNull(game);
        var normalizedQuery = query?.Trim();
        if (string.IsNullOrEmpty(normalizedQuery))
        {
            return true;
        }

        return game.Name.Contains(normalizedQuery, StringComparison.CurrentCultureIgnoreCase) ||
               game.AppId.ToString().Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
               game.InstallPath.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
    }
}
