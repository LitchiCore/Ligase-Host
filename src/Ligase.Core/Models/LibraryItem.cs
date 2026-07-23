using System.Text.Json.Serialization;

namespace Ligase.Host.Core.Models;

public sealed class LibraryItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public LibraryItemKind Kind { get; init; }
    public required string Name { get; set; }
    public string? ExecutablePath { get; init; }
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public uint? SteamAppId { get; init; }
    public string? SteamInstallPath { get; init; }
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastPlayedAt { get; set; }

    [JsonIgnore]
    public bool IsSystemEntry =>
        Kind is LibraryItemKind.Desktop or LibraryItemKind.VirtualDesktop;

    [JsonIgnore]
    public string SourceLabel => Kind switch
    {
        LibraryItemKind.Desktop => "Windows · 复制显示器",
        LibraryItemKind.VirtualDesktop => "Windows · 扩展显示器",
        LibraryItemKind.Steam => $"Steam · App ID {SteamAppId}",
        _ => "本地应用"
    };

    [JsonIgnore]
    public string LocationLabel => Kind switch
    {
        LibraryItemKind.Desktop => "串流当前 Windows 桌面",
        LibraryItemKind.VirtualDesktop => "创建并串流独立虚拟显示器",
        LibraryItemKind.Steam => SteamInstallPath ?? string.Empty,
        _ => ExecutablePath ?? string.Empty
    };

    [JsonIgnore]
    public string AddedLabel => IsSystemEntry
        ? "系统入口"
        : $"添加于 {AddedAt.ToLocalTime():yyyy-MM-dd}";

    [JsonIgnore]
    public string LastPlayedLabel => IsSystemEntry
        ? "始终可用"
        : LastPlayedAt is null
        ? "尚未启动"
        : $"上次启动 {LastPlayedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}";

    [JsonIgnore]
    public string IconGlyph => Kind switch
    {
        LibraryItemKind.Desktop => "\uE7F4",
        LibraryItemKind.VirtualDesktop => "\uE8A7",
        LibraryItemKind.Executable => "\uE756",
        _ => "\uE7FC"
    };
}
