namespace Ligase.Host.Core.Models;

public enum WindowsShortcutPreviewKind
{
    Executable,
    SteamShortcut,
    Unsupported,
    Risk,
    Invalid
}

public enum WindowsShortcutErrorCode
{
    None,
    NotShortcut,
    ShortcutNotFound,
    DamagedShortcut,
    NetworkLocation,
    TargetMissing,
    TargetIsDirectory,
    UrlTarget,
    UwpTarget,
    ScriptTarget,
    CommandShellTarget,
    InstallerTarget,
    UnsupportedTarget,
    InvalidSteamShortcut
}

public sealed class WindowsShortcutPreview
{
    public required string ShortcutPath { get; init; }
    public required string DisplayName { get; init; }
    public required WindowsShortcutPreviewKind Kind { get; init; }
    public required WindowsShortcutErrorCode Code { get; init; }
    public string? TargetExecutable { get; init; }
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? IconSource { get; init; }
    public uint? SteamAppId { get; init; }
    public string? CanonicalTargetArgumentsKey { get; init; }

    public bool CanConfirmExecutable => Kind == WindowsShortcutPreviewKind.Executable;

    public override string ToString() =>
        $"{Kind}:{Code}:{DisplayName}";
}
