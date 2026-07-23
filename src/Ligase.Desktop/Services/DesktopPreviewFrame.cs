namespace Ligase.Host.Desktop.Services;

public sealed record DesktopPreviewFrame(
    byte[] Pixels,
    int Width,
    int Height,
    int SourceWidth,
    int SourceHeight,
    DateTimeOffset CapturedAt);
