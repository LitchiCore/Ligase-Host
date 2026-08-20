using System.Diagnostics;
using Ligase.Host.Desktop.Services;
using Ligase.Host.Desktop.ViewModels;
using Ligase.Host.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Ligase.Host.Desktop.Pages;

public sealed partial class StreamMonitorPage : Page
{
    private const int PreviewWidth = 800;
    private const int PreviewHeight = 450;
    private readonly IDesktopPreviewService _previewService;
    private readonly IVirtualDisplayControlService _virtualDisplay;
    private readonly DispatcherTimer _timer;
    private readonly PreviewFrameGate _frameGate = new();
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();
    private bool _captureInProgress;
    private int _framesSinceSample;
    private long _lastSampleMilliseconds;
    private WriteableBitmap? _previewBitmap;

    public StreamMonitorViewModel ViewModel { get; }

    public StreamMonitorPage()
    {
        var services = ((App)Application.Current).Services;
        ViewModel = services.GetRequiredService<StreamMonitorViewModel>();
        _previewService = services.GetRequiredService<IDesktopPreviewService>();
        _virtualDisplay = services.GetRequiredService<IVirtualDisplayControlService>();
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += OnPreviewTick;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshCoreStatus();
        await RefreshPreviewSourcesAsync();
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

    private void OnPreviewSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || PreviewSourcePicker.SelectedItem is not DesktopPreviewSource source)
            return;
        _frameGate.ChangeSource(source.DeviceName);
        ViewModel.SetPreviewStarted(source.Label);
        if (_timer.IsEnabled) _ = CaptureFrameAsync();
    }

    private async Task RefreshPreviewSourcesAsync()
    {
        var sources = _previewService.GetSources().ToList();
        try
        {
            var state = await _virtualDisplay.GetStateAsync();
            if (state.IsEnabled &&
                sources.All(source => !source.DeviceName.Equals(
                    state.DisplayName, StringComparison.OrdinalIgnoreCase)))
                ViewModel.SetError("虚拟桌面已启用，但 Windows 显示枚举尚未返回该显示器。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ViewModel.SetError($"虚拟桌面状态不可用 · {exception.Message}");
        }

        PreviewSourcePicker.ItemsSource = sources;
        PreviewSourcePicker.SelectedItem =
            sources.FirstOrDefault(source => source.Primary) ?? sources.FirstOrDefault();
    }

    private async void OnEndStream(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "结束当前串流？",
            Content = "正在连接的设备会断开，但游戏库和配对不会被删除。",
            PrimaryButtonText = "结束串流",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.EndStreamAsync();
    }

    private void StartPreview()
    {
        if (_timer.IsEnabled) return;
        var source = PreviewSourcePicker.SelectedItem as DesktopPreviewSource;
        _frameGate.Start(source?.DeviceName);
        ViewModel.SetPreviewStarted(source?.Label ?? "主显示器");
        EmptyState.Visibility = Visibility.Visible;
        _lastSampleMilliseconds = _frameClock.ElapsedMilliseconds;
        _framesSinceSample = 0;
        _timer.Start();
        _ = CaptureFrameAsync();
    }

    private void StopPreview()
    {
        _frameGate.Stop();
        _timer.Stop();
        ViewModel.SetPreviewStopped();
    }

    private void OnPreviewTick(object? sender, object e) => _ = CaptureFrameAsync();

    private async Task CaptureFrameAsync()
    {
        if (_captureInProgress || !_timer.IsEnabled) return;
        _captureInProgress = true;
        var lease = _frameGate.Capture();
        var source = PreviewSourcePicker.SelectedItem as DesktopPreviewSource;
        try
        {
            var frame = await Task.Run(() => _previewService.Capture(
                PreviewWidth,
                PreviewHeight,
                source?.DeviceName));
            if (!_timer.IsEnabled || !_frameGate.CanPublish(lease)) return;

            if (_previewBitmap is null || _previewBitmap.PixelWidth != frame.Width ||
                _previewBitmap.PixelHeight != frame.Height)
            {
                _previewBitmap = new WriteableBitmap(frame.Width, frame.Height);
                PreviewImage.Source = _previewBitmap;
            }
            using (var pixels = _previewBitmap.PixelBuffer.AsStream())
            {
                pixels.Position = 0;
                await pixels.WriteAsync(frame.Pixels);
            }
            _previewBitmap.Invalidate();
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

            ViewModel.SetFrame(frame, fps, source?.Label ?? "主显示器");
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

}
