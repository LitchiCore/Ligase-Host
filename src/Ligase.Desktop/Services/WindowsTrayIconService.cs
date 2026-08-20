using System.Runtime.InteropServices;
using Ligase.Host.Core.Services;
using Microsoft.UI.Xaml;

namespace Ligase.Host.Desktop.Services;

public sealed class WindowsTrayIconService : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 74;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint WmLbuttonUp = 0x0202;
    private const uint WmLbuttonDblclk = 0x0203;
    private const uint WmRbuttonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const int GwlpWndproc = -4;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmNoNotify = 0x0080;
    private const int SwRestore = 9;
    private const int IdiApplication = 32512;
    private const uint CommandOpen = 1001;
    private const uint CommandToggleCore = 1002;
    private const uint CommandExit = 1003;

    private readonly WindowProcedure _windowProcedure;
    private readonly ApolloInstanceManager _core;
    private readonly TrayActivationDeduplicator _activationDeduplicator;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
    private IntPtr _windowHandle;
    private IntPtr _previousWindowProcedure;
    private bool _initialized;

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public event Action? CoreStatusChanged;

    public WindowsTrayIconService(ApolloInstanceManager core)
    {
        _core = core;
        _activationDeduplicator = new TrayActivationDeduplicator(
            TimeSpan.FromMilliseconds(GetDoubleClickTime()));
        _windowProcedure = WindowMessageHandler;
    }

    public void Initialize(Window window)
    {
        if (_initialized) return;
        _dispatcherQueue = window.DispatcherQueue;
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _previousWindowProcedure = SetWindowLongPtr(
            _windowHandle,
            GwlpWndproc,
            Marshal.GetFunctionPointerForDelegate(_windowProcedure));
        if (_previousWindowProcedure == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法连接 Ligase Host 托盘窗口。");
        }

        var iconData = CreateIconData();
        if (!ShellNotifyIcon(NimAdd, ref iconData))
        {
            SetWindowLongPtr(_windowHandle, GwlpWndproc, _previousWindowProcedure);
            _previousWindowProcedure = IntPtr.Zero;
            throw new InvalidOperationException("无法创建 Ligase Host 系统托盘图标。");
        }

        _initialized = true;
    }

    public void UpdateTooltip()
    {
        if (!_initialized) return;
        var iconData = CreateIconData();
        ShellNotifyIcon(NimModify, ref iconData);
    }

    public void RestoreWindow()
    {
        if (_windowHandle == IntPtr.Zero) return;
        ShowWindow(_windowHandle, SwRestore);
        SetForegroundWindow(_windowHandle);
        OpenRequested?.Invoke();
    }

    public void Dispose()
    {
        if (!_initialized) return;
        var iconData = CreateIconData();
        ShellNotifyIcon(NimDelete, ref iconData);
        if (_previousWindowProcedure != IntPtr.Zero)
        {
            SetWindowLongPtr(_windowHandle, GwlpWndproc, _previousWindowProcedure);
        }

        _initialized = false;
        _activationDeduplicator.Dispose();
        _dispatcherQueue = null;
        _previousWindowProcedure = IntPtr.Zero;
        _windowHandle = IntPtr.Zero;
    }

    private IntPtr WindowMessageHandler(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (message == CallbackMessage)
        {
            var notification = unchecked((uint)lParam.ToInt64());
            if (notification == WmLbuttonUp)
            {
                _activationDeduplicator.OnLeftButtonUp(RequestRestoreOnUiThread);
                return IntPtr.Zero;
            }
            if (notification == WmLbuttonDblclk)
            {
                _activationDeduplicator.OnLeftButtonDoubleClick(
                    RequestRestoreOnUiThread);
                return IntPtr.Zero;
            }

            if (notification is WmRbuttonUp or WmContextMenu)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        return CallWindowProc(_previousWindowProcedure, window, message, wParam, lParam);
    }

    private void RequestRestoreOnUiThread()
    {
        var dispatcher = _dispatcherQueue;
        if (dispatcher is null) return;
        if (dispatcher.HasThreadAccess) RestoreWindow();
        else _ = dispatcher.TryEnqueue(RestoreWindow);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, MfString, CommandOpen, "打开 Ligase Host");
            AppendMenu(
                menu,
                MfString,
                CommandToggleCore,
                _core.IsRunning ? "停止串流核心" : "启动串流核心");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, CommandExit, "退出 Ligase Host");
            GetCursorPos(out var point);
            SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenu(
                menu,
                TpmRightButton | TpmReturnCmd | TpmNoNotify,
                point.X,
                point.Y,
                0,
                _windowHandle,
                IntPtr.Zero);
            switch (command)
            {
                case CommandOpen:
                    RestoreWindow();
                    break;
                case CommandToggleCore:
                    _ = ToggleCoreAsync();
                    break;
                case CommandExit:
                    ExitRequested?.Invoke();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private async Task ToggleCoreAsync()
    {
        if (_core.IsRunning)
        {
            await _core.StopAsync();
        }
        else
        {
            await _core.StartAsync();
        }

        UpdateTooltip();
        CoreStatusChanged?.Invoke();
    }

    private NotifyIconData CreateIconData() => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(),
        WindowHandle = _windowHandle,
        Id = 1,
        Flags = NifMessage | NifIcon | NifTip,
        CallbackMessage = CallbackMessage,
        IconHandle = LoadIcon(IntPtr.Zero, new IntPtr(IdiApplication)),
        Tip = _core.IsRunning ? "Ligase Host · 串流核心运行中" : "Ligase Host · 串流核心已停止"
    };

    private delegate IntPtr WindowProcedure(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(
        uint message,
        ref NotifyIconData data);

    private static bool ShellNotifyIcon(uint message, ref NotifyIconData data) =>
        Shell_NotifyIcon(message, ref data);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr window,
        int index,
        IntPtr newLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(
        IntPtr previousWindowProcedure,
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(
        IntPtr menu,
        uint flags,
        uint identifier,
        string? text);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr window,
        IntPtr rectangle);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
}

internal sealed class TrayActivationDeduplicator(TimeSpan singleClickDelay) : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _pending;
    private bool _suppressTrailingUp;

    public void OnLeftButtonUp(Action restore)
    {
        CancellationTokenSource pending;
        lock (_sync)
        {
            if (_suppressTrailingUp)
            {
                _suppressTrailingUp = false;
                return;
            }
            _pending?.Cancel();
            pending = _pending = new CancellationTokenSource();
        }

        _ = CompleteSingleClickAsync(pending, restore);
    }

    public void OnLeftButtonDoubleClick(Action restore)
    {
        lock (_sync)
        {
            _pending?.Cancel();
            _pending = null;
            _suppressTrailingUp = true;
        }
        restore();
    }

    private async Task CompleteSingleClickAsync(
        CancellationTokenSource pending,
        Action restore)
    {
        try
        {
            await Task.Delay(singleClickDelay, pending.Token);
            lock (_sync)
            {
                if (!ReferenceEquals(_pending, pending)) return;
                _pending = null;
            }
            restore();
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        {
        }
        finally
        {
            pending.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _pending?.Cancel();
            _pending = null;
        }
    }
}
