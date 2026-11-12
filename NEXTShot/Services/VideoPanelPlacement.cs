using System.Windows;
using System.Windows.Media;
using NEXTShot.Native;

namespace NEXTShot.Services;

public static class VideoPanelPlacement
{
    public static void PlaceBottomCenter(Window panel, NativeMethods.Rect? capturePhysical, bool fullScreen)
    {
        panel.UpdateLayout();
        var pw = panel.ActualWidth > 1 ? panel.ActualWidth : panel.Width;
        var ph = panel.ActualHeight > 1 ? panel.ActualHeight : panel.Height;
        if (pw < 1) pw = 200;
        if (ph < 1) ph = 48;

        var vLeft = SystemParameters.VirtualScreenLeft;
        var vTop = SystemParameters.VirtualScreenTop;
        var vW = SystemParameters.VirtualScreenWidth;
        var vH = SystemParameters.VirtualScreenHeight;
        var dpi = VisualTreeHelper.GetDpi(panel);

        double left, top;

        if (fullScreen || capturePhysical is not { } cap)
        {
            left = vLeft + (vW - pw) / 2;
            top = vTop + vH - ph - 28;
        }
        else
        {
            var vs = ScreenCaptureService.GetVirtualScreenRect();
            var fx = (cap.Left - vs.Left) / dpi.DpiScaleX;
            var fy = (cap.Top - vs.Top) / dpi.DpiScaleY;
            var fw = cap.Width / dpi.DpiScaleX;
            var fh = cap.Height / dpi.DpiScaleY;

            left = vLeft + fx + (fw - pw) / 2;
            top = vTop + fy + fh + 12;
            if (top + ph > vTop + vH - 16)
                top = vTop + fy - ph - 12;

            left = Math.Clamp(left, vLeft + 8, vLeft + vW - pw - 8);
            top = Math.Clamp(top, vTop + 8, vTop + vH - ph - 8);
        }

        panel.Left = left;
        panel.Top = top;
    }
}
