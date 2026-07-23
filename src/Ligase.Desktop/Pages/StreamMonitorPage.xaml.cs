using System.Diagnostics;
using Ligase.Host.Desktop.Services;
using Ligase.Host.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace Ligase.Host.Desktop.Pages;

public sealed partial class StreamMonitorPage : Page
{
    private const int PreviewWidth = 800;
    private const int PreviewHeight = 450;
    private readonly IDesktopPreviewService _previewService;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();
    private bool _captureInProgress;
    private int _framesSinceSample;
    private long _lastSampleMilliseconds;

    public StreamMonitorViewModel ViewModel { get; }

    public StreamMonitorPage()
    {
        var services = ((App)Application.Current).Services;
        ViewModel = services.GetRequiredService<StreamMonitorViewModel>();
        _previewService = services.GetRequiredService<IDesktopPreviewService>();
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += OnPreviewTick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshCoreStatus();
        PreviewToggle.IsChecked = true;
        StartPreview();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        StopPreview();
        base.OnNavigatedFrom(e);
    }

    private void OnPreviewToggleChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (PreviewToggle.IsChecked == true)
        {
            StartPreview();
        }
        else
        {
            StopPreview();
        }
    }

    private void StartPreview()
    {
        if (_timer.IsEnabled) return;
        ViewModel.SetPreviewStarted();
        EmptyState.Visibility = Visibility.Visible;
        _lastSampleMilliseconds = _frameClock.ElapsedMilliseconds;
        _framesSinceSample = 0;
        _timer.Start();
        _ = CaptureFrameAsync();
    }

    private void StopPreview()
    {
        _timer.Stop();
        ViewModel.SetPreviewStopped();
    }

    private void OnPreviewTick(object? sender, object e) => _ = CaptureFrameAsync();

    private async Task CaptureFrameAsync()
    {
        if (_captureInProgress || !_timer.IsEnabled) return;
        _captureInProgress = true;
        try
        {
            var frame = await Task.Run(() => _previewService.Capture(PreviewWidth, PreviewHeight));
            if (!_timer.IsEnabled) return;

            var bitmap = new BitmapImage();
            using var bitmapStream = CreateBitmapStream(frame);
            using var randomAccessStream = bitmapStream.AsRandomAccessStream();
            await bitmap.SetSourceAsync(randomAccessStream);
            PreviewImage.Source = bitmap;
            EmptyState.Visibility = Visibility.Collapsed;

            _framesSinceSample++;
            var now = _frameClock.ElapsedMilliseconds;
            var elapsed = now - _lastSampleMilliseconds;
            var fps = elapsed > 0 ? _framesSinceSample * 1000d / elapsed : 0d;
            if (elapsed >= 1000)
            {
                _lastSampleMilliseconds = now;
                _framesSinceSample = 0;
            }

            ViewModel.SetFrame(frame, fps);
        }
        catch (Exception exception)
        {
            _timer.Stop();
            ViewModel.SetError($"预览不可用 · {exception.Message}");
            EmptyState.Visibility = Visibility.Visible;
        }
        finally
        {
            _captureInProgress = false;
        }
    }

    private static MemoryStream CreateBitmapStream(DesktopPreviewFrame frame)
    {
        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        var pixelDataSize = frame.Pixels.Length;
        var stream = new MemoryStream(fileHeaderSize + infoHeaderSize + pixelDataSize);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0x4D42);
            writer.Write(fileHeaderSize + infoHeaderSize + pixelDataSize);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(fileHeaderSize + infoHeaderSize);

            writer.Write(infoHeaderSize);
            writer.Write(frame.Width);
            writer.Write(-frame.Height);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0);
            writer.Write(pixelDataSize);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(frame.Pixels);
        }

        stream.Position = 0;
        return stream;
    }
}
