using CommunityToolkit.Mvvm.ComponentModel;
using Ligase.Host.Core.Services;

namespace Ligase.Host.Desktop.ViewModels;

public partial class StreamMonitorViewModel(ApolloInstanceManager core) : ObservableObject
{
    [ObservableProperty]
    private string _coreStatus = "正在读取核心状态";

    [ObservableProperty]
    private string _previewStatus = "正在准备预览";

    [ObservableProperty]
    private string _resolution = "—";

    [ObservableProperty]
    private string _frameRate = "—";

    [ObservableProperty]
    private string _lastUpdated = "—";

    [ObservableProperty]
    private bool _isPreviewActive;

    public void RefreshCoreStatus()
    {
        CoreStatus = core.IsRunning
            ? $"Apollo 正在运行 · 独立端口 {core.BasePort}"
            : core.StartupError ?? "Apollo 核心未运行";
    }

    public void SetPreviewStarted()
    {
        IsPreviewActive = true;
        PreviewStatus = "实时预览中 · 主显示器";
    }

    public void SetPreviewStopped()
    {
        IsPreviewActive = false;
        PreviewStatus = "预览已暂停";
        FrameRate = "—";
    }

    public void SetFrame(Services.DesktopPreviewFrame frame, double framesPerSecond)
    {
        Resolution = $"{frame.SourceWidth} × {frame.SourceHeight}";
        FrameRate = $"{framesPerSecond:0.0} FPS";
        LastUpdated = frame.CapturedAt.ToString("HH:mm:ss");
        PreviewStatus = "实时预览中 · 主显示器";
    }

    public void SetError(string message)
    {
        IsPreviewActive = false;
        PreviewStatus = message;
        FrameRate = "—";
    }
}
