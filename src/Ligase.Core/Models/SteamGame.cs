namespace Ligase.Host.Core.Models;

public sealed record SteamGame(
    uint AppId,
    string Name,
    string InstallDirectory,
    string LibraryPath,
    string ManifestPath,
    ulong SizeOnDisk)
{
    public string InstallPath => Path.Combine(LibraryPath, "steamapps", "common", InstallDirectory);
    public string LaunchUri => $"steam://rungameid/{AppId}";
    public string AppIdLabel => $"App ID {AppId}";
    public string SizeLabel => SizeOnDisk == 0 ? "大小未知" : FormatSize(SizeOnDisk);

    private static string FormatSize(ulong bytes)
    {
        var gibibytes = bytes / 1024d / 1024d / 1024d;
        return gibibytes >= 0.1 ? $"{gibibytes:0.#} GiB" : $"{bytes / 1024d / 1024d:0.#} MiB";
    }
}
