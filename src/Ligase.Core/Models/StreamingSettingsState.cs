namespace Ligase.Host.Core.Models;

public sealed record StreamResolution
{
    public StreamResolution(int width, int height)
    {
        if (width is < 320 or > 16384)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height is < 240 or > 16384)
            throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
    }

    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed class AppStreamingSettings
{
    public StreamResolution? Resolution { get; set; }
}

public sealed class StreamingSettingsState
{
    public int SchemaVersion { get; init; } = 1;
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public StreamResolution GlobalResolution { get; set; } = new(1920, 1080);
    public Dictionary<Guid, AppStreamingSettings> Apps { get; init; } = [];

    public StreamResolution GetEffectiveResolution(Guid appId) =>
        Apps.TryGetValue(appId, out var settings) && settings.Resolution is not null
            ? settings.Resolution
            : GlobalResolution;
}
