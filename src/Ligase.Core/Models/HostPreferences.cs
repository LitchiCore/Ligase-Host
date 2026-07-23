namespace Ligase.Host.Core.Models;

public enum HostLanguage
{
    System,
    SimplifiedChinese,
    English
}

public sealed class HostPreferences
{
    public int SchemaVersion { get; init; } = 1;
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public HostLanguage Language { get; set; } = HostLanguage.System;
}
