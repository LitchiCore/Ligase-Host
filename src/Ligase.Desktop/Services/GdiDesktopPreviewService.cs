using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Ligase.Host.Desktop.Services;

public sealed class GdiDesktopPreviewService : IDesktopPreviewService
{
    private const int DibRgbColors = 0;
    private const int Srccopy = 0x00CC0020;
    private const int Captureblt = 0x40000000;
    private const int Halftone = 4;
    private const int SmCxscreen = 0;
    private const int SmCyscreen = 1;

    public DesktopPreviewFrame Capture(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "预览尺寸必须大于零。");
        }

        var sourceWidth = GetSystemMetrics(SmCxscreen);
        var sourceHeight = GetSystemMetrics(SmCyscreen);
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
                    0,
                    0,
                    sourceWidth,
                    sourceHeight,
                    Srccopy | Captureblt))
            {
                throw CreateWin32Exception("桌面预览采集失败。");
            }

            var buffer = new byte[checked(width * height * 4)];
            Marshal.Copy(pixels, buffer, 0, buffer.Length);
            return new DesktopPreviewFrame(
                buffer,
                width,
                height,
                sourceWidth,
                sourceHeight,
                DateTimeOffset.Now);
        }
        finally
        {
            SelectObject(memoryDc, previousObject);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

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
