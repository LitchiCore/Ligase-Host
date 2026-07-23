using Microsoft.Win32;

namespace Ligase.Host.Core.Services;

public sealed class WindowsSteamInstallationLocator : ISteamInstallationLocator
{
    private static readonly (RegistryHive Hive, RegistryView View, string Key, string Value)[] RegistryCandidates =
    [
        (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath"),
        (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Valve\Steam", "InstallPath"),
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Valve\Steam", "InstallPath")
    ];

    public string? FindSteamPath()
    {
        foreach (var candidate in RegistryCandidates)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(candidate.Hive, candidate.View);
                using var key = baseKey.OpenSubKey(candidate.Key);
                if (NormalizeExistingDirectory(key?.GetValue(candidate.Value) as string) is { } path)
                {
                    return path;
                }
            }
            catch (Exception) when (OperatingSystem.IsWindows() && candidate.Hive is RegistryHive.LocalMachine)
            {
                // A restricted account may not be able to read a machine-wide key.
            }
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return NormalizeExistingDirectory(Path.Combine(programFilesX86, "Steam"));
    }

    private static string? NormalizeExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
        return Directory.Exists(Path.Combine(normalized, "steamapps")) ? normalized : null;
    }
}
