using System.Text.Json;
using Ligase.Host.Core.Services;

var service = new SteamLibraryService(new WindowsSteamInstallationLocator());
var games = await service.DiscoverGamesAsync();

Console.WriteLine(JsonSerializer.Serialize(
    new
    {
        Count = games.Count,
        Games = games.Select(game => new
        {
            game.AppId,
            game.Name,
            game.InstallPath,
            game.SizeLabel,
            game.LaunchUri
        })
    },
    new JsonSerializerOptions { WriteIndented = true }));

return games.Count == 0 ? 2 : 0;
