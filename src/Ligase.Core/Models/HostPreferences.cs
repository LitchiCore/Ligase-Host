namespace Ligase.Host.Core.Models;

public sealed class HostPreferences
{
    public int SchemaVersion { get; init; } = 1;
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
}
