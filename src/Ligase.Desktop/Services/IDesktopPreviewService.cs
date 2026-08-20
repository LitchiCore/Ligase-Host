namespace Ligase.Host.Desktop.Services;

public interface IDesktopPreviewService
{
    IReadOnlyList<DesktopPreviewSource> GetSources();
    DesktopPreviewFrame Capture(int width, int height, string? deviceName = null);
}

public sealed record DesktopPreviewSource(string DeviceName, string Label, bool Primary);
