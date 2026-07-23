namespace Ligase.Host.Desktop.Services;

public interface IDesktopPreviewService
{
    DesktopPreviewFrame Capture(int width, int height);
}
