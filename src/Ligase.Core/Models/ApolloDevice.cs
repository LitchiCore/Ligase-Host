using System.Text.Json.Serialization;

namespace Ligase.Host.Core.Models;

public sealed class ApolloDevice
{
    public required string Name { get; init; }
    public required string Uuid { get; init; }

    [JsonPropertyName("display_mode")]
    public string DisplayMode { get; init; } = string.Empty;

    [JsonPropertyName("perm")]
    public uint Permissions { get; init; }

    [JsonPropertyName("allow_client_commands")]
    public bool AllowClientCommands { get; init; }

    [JsonPropertyName("always_use_virtual_display")]
    public bool AlwaysUseVirtualDisplay { get; init; }

    public bool Connected { get; init; }
}

public sealed class ApolloDeviceSnapshot
{
    public int SchemaVersion { get; init; }
    public IReadOnlyList<ApolloDevice> Devices { get; init; } = [];
}
