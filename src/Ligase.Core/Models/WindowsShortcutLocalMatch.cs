namespace Ligase.Host.Core.Models;

public enum WindowsShortcutMatchKind
{
    LocalExecutable,
    ExactSteamInstall,
    MultipleSteamInstalls
}

public sealed record WindowsShortcutLocalMatch(
    WindowsShortcutMatchKind Kind,
    string ProductName,
    string FileDescription,
    string FileVersion,
    string CompanyName,
    uint? SteamAppId,
    string Summary)
{
    public bool RequiresExplicitFallbackConfirmation =>
        Kind == WindowsShortcutMatchKind.MultipleSteamInstalls;
}
