using System.Runtime.InteropServices;

namespace Ligase.Host.Desktop.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\Ligase.Host.Desktop.SingleInstance";
    private const string ShowMessageName = "Ligase.Host.Desktop.ShowWindow";
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);
    private Mutex? _mutex;
    private bool _ownsMutex;

    public static uint ShowWindowMessage { get; } = RegisterWindowMessage(ShowMessageName);

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            PostMessage(HwndBroadcast, ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
        }

        return createdNew;
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _mutex?.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex?.Dispose();
        _mutex = null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);
}
