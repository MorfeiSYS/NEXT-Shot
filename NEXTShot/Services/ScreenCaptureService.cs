using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using NEXTShot.Native;
using DSize = System.Drawing.Size;

namespace NEXTShot.Services;

public static class ScreenCaptureService
{
    public static Bitmap CaptureVirtualScreen(bool includeCursor) =>
        CaptureRect(GetVirtualScreenRect(), includeCursor);

    public static NativeMethods.Rect? GetForegroundWindowRect()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0) return null;
        return rect;
    }

    public static NativeMethods.Rect GetVirtualScreenRect()
    {
        var left = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);
        return new NativeMethods.Rect
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height
        };
    }

    public static Bitmap? CaptureForegroundWindow(bool includeCursor)
    {
        var rect = GetForegroundWindowRect();
        if (rect is null) return null;
        return CaptureRect(rect.Value, includeCursor);
    }

    public static Bitmap CaptureRect(NativeMethods.Rect rect, bool includeCursor)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new ArgumentException("Некорректный размер области захвата.");

        var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new DSize(rect.Width, rect.Height), CopyPixelOperation.SourceCopy);
        if (includeCursor)
        {
            try { DrawCursor(g, rect.Left, rect.Top); }
            catch { /* cursor overlay is optional */ }
        }
        return bmp;
    }

    private static void DrawCursor(Graphics g, int offsetX, int offsetY)
    {
        var cursorInfo = new CursorInfo { CbSize = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref cursorInfo) || cursorInfo.Flags != CursorShowing) return;
        using var icon = Icon.FromHandle(cursorInfo.HCursor);
        g.DrawIcon(icon, cursorInfo.Position.X - offsetX, cursorInfo.Position.Y - offsetY);
    }

    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        finally { DeleteObject(hBitmap); }
    }

    public static void SaveBitmap(Bitmap bitmap, string path, int jpegQuality, bool asJpeg)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (asJpeg)
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(Encoder.Quality, jpegQuality);
            bitmap.Save(path, codec, ep);
        }
        else bitmap.Save(path, ImageFormat.Png);
    }

    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo pci);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    private const int CursorShowing = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int CbSize;
        public int Flags;
        public IntPtr HCursor;
        public System.Drawing.Point Position;
    }
}