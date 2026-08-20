using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Ligase.Host.Desktop.Services;

public sealed class GdiDesktopPreviewService : IDesktopPreviewService
{
    private const int DibRgbColors = 0;
    private const int Srccopy = 0x00CC0020;
    private const int Halftone = 4;
    private const int SmCxscreen = 0;
    private const int SmCyscreen = 1;
    private const int CursorShowing = 0x00000001;
    private const int DiNormal = 0x0003;

    public IReadOnlyList<DesktopPreviewSource> GetSources()
    {
        var sources = new List<DesktopPreviewSource>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(monitor, ref info))
                sources.Add(new DesktopPreviewSource(
                    info.DeviceName,
                    (info.Flags & 1) != 0 ? "主显示器" : info.DeviceName,
                    (info.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return sources.OrderByDescending(source => source.Primary)
            .ThenBy(source => source.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public DesktopPreviewFrame Capture(int width, int height, string? deviceName = null)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "预览尺寸必须大于零。");
        }

        var source = GetSources().FirstOrDefault(candidate =>
                string.IsNullOrWhiteSpace(deviceName)
                    ? candidate.Primary
                    : candidate.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("指定的 Windows 显示器当前不可用。");
        var sourceRect = GetMonitorRectangle(source.DeviceName);
        var sourceWidth = sourceRect.Right - sourceRect.Left;
        var sourceHeight = sourceRect.Bottom - sourceRect.Top;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            throw new InvalidOperationException("无法读取主显示器尺寸。");
        }

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) throw CreateWin32Exception("无法访问桌面画面。");

        var memoryDc = CreateCompatibleDC(screenDc);
        if (memoryDc == IntPtr.Zero)
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            throw CreateWin32Exception("无法创建桌面预览缓冲区。");
        }

        var bitmapInfo = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = 0
            }
        };

        var bitmap = CreateDIBSection(
            memoryDc,
            ref bitmapInfo,
            DibRgbColors,
            out var pixels,
            IntPtr.Zero,
            0);
        if (bitmap == IntPtr.Zero)
        {
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
            throw CreateWin32Exception("无法创建桌面预览图像。");
        }

        var previousObject = SelectObject(memoryDc, bitmap);
        try
        {
            SetStretchBltMode(memoryDc, Halftone);
            if (!StretchBlt(
                    memoryDc,
                    0,
                    0,
                    width,
                    height,
                    screenDc,
                    sourceRect.Left,
                    sourceRect.Top,
                    sourceWidth,
                    sourceHeight,
                    Srccopy))
            {
                throw CreateWin32Exception("桌面预览采集失败。");
            }

            var cursorPlan = DrawCursor(memoryDc, sourceRect, width, height);

            var buffer = new byte[checked(width * height * 4)];
            Marshal.Copy(pixels, buffer, 0, buffer.Length);
            return new DesktopPreviewFrame(
                buffer,
                width,
                height,
                sourceWidth,
                sourceHeight,
                DateTimeOffset.Now,
                cursorPlan.Visible,
                cursorPlan.Visible ? cursorPlan.X : null,
                cursorPlan.Visible ? cursorPlan.Y : null,
                "gdiSrccopyWithOwnedCursorOverlay",
                0);
        }
        finally
        {
            SelectObject(memoryDc, previousObject);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static CursorOverlayPlan DrawCursor(
        IntPtr destination, Rect source, int width, int height)
    {
        var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref cursor) ||
            (cursor.Flags & CursorShowing) == 0 || cursor.Cursor == IntPtr.Zero)
            return CursorOverlayPlan.Hidden;

        if (!GetIconInfo(cursor.Cursor, out var icon))
            throw CreateWin32Exception("无法读取鼠标指针形状。");
        try
        {
            var cursorSize = GetCursorSize(icon);
            var plan = CursorOverlayPlanner.Plan(
                cursor.ScreenPosition.X,
                cursor.ScreenPosition.Y,
                checked((int)icon.XHotspot),
                checked((int)icon.YHotspot),
                cursorSize.Width,
                cursorSize.Height,
                new PreviewRectangle(source.Left, source.Top, source.Right, source.Bottom),
                width,
                height);
            if (plan.Visible && !DrawIconEx(
                    destination, plan.X, plan.Y, cursor.Cursor,
                    plan.Width, plan.Height, 0, IntPtr.Zero, DiNormal))
                throw CreateWin32Exception("无法合成鼠标指针。");
            return plan;
        }
        finally
        {
            if (icon.MaskBitmap != IntPtr.Zero) DeleteObject(icon.MaskBitmap);
            if (icon.ColorBitmap != IntPtr.Zero) DeleteObject(icon.ColorBitmap);
        }
    }

    private static (int Width, int Height) GetCursorSize(IconInfo icon)
    {
        var bitmapHandle = icon.ColorBitmap != IntPtr.Zero
            ? icon.ColorBitmap : icon.MaskBitmap;
        if (bitmapHandle == IntPtr.Zero ||
            GetObject(bitmapHandle, Marshal.SizeOf<NativeBitmap>(), out var bitmap) == 0)
            return (Math.Max(1, GetSystemMetrics(13)),
                Math.Max(1, GetSystemMetrics(14)));
        var height = icon.ColorBitmap == IntPtr.Zero
            ? Math.Abs(bitmap.Height) / 2 : Math.Abs(bitmap.Height);
        return (Math.Max(1, Math.Abs(bitmap.Width)), Math.Max(1, height));
    }

    private static Rect GetMonitorRectangle(string deviceName)
    {
        Rect? match = null;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(monitor, ref info) &&
                info.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
                match = info.Monitor;
            return true;
        }, IntPtr.Zero);
        return match ?? throw new InvalidOperationException("无法读取显示器边界。");
    }

    private static Win32Exception CreateWin32Exception(string message) =>
        new(Marshal.GetLastWin32Error(), message);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public uint[]? Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Cursor;
        public Point ScreenPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public uint XHotspot;
        public uint YHotspot;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    private delegate bool MonitorEnumProc(
        IntPtr monitor, IntPtr deviceContext, IntPtr rectangle, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorInfo(ref CursorInfo cursorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo iconInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DrawIconEx(
        IntPtr deviceContext, int x, int y, IntPtr icon, int width, int height,
        uint step, IntPtr flickerFreeBrush, uint flags);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr graphicObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetObject(
        IntPtr graphicObject, int bufferSize, out NativeBitmap bitmap);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr deviceContext, int mode);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool StretchBlt(
        IntPtr destination,
        int destinationX,
        int destinationY,
        int destinationWidth,
        int destinationHeight,
        IntPtr source,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        int rasterOperation);
}

internal readonly record struct PreviewRectangle(
    int Left, int Top, int Right, int Bottom);

internal readonly record struct CursorOverlayPlan(
    bool Visible, int X, int Y, int Width, int Height)
{
    public static CursorOverlayPlan Hidden { get; } = new(false, 0, 0, 0, 0);
}

internal static class CursorOverlayPlanner
{
    public static CursorOverlayPlan Plan(
        int cursorX, int cursorY, int hotspotX, int hotspotY,
        int cursorWidth, int cursorHeight, PreviewRectangle source,
        int destinationWidth, int destinationHeight)
    {
        var sourceWidth = source.Right - source.Left;
        var sourceHeight = source.Bottom - source.Top;
        if (sourceWidth <= 0 || sourceHeight <= 0 || destinationWidth <= 0 ||
            destinationHeight <= 0 || cursorWidth <= 0 || cursorHeight <= 0)
            return CursorOverlayPlan.Hidden;
        var left = cursorX - hotspotX;
        var top = cursorY - hotspotY;
        if (left >= source.Right || top >= source.Bottom ||
            left + cursorWidth <= source.Left || top + cursorHeight <= source.Top)
            return CursorOverlayPlan.Hidden;
        return new CursorOverlayPlan(
            true,
            (int)Math.Round((left - source.Left) *
                (double)destinationWidth / sourceWidth),
            (int)Math.Round((top - source.Top) *
                (double)destinationHeight / sourceHeight),
            Math.Max(1, (int)Math.Round(cursorWidth *
                (double)destinationWidth / sourceWidth)),
            Math.Max(1, (int)Math.Round(cursorHeight *
                (double)destinationHeight / sourceHeight)));
    }
}
