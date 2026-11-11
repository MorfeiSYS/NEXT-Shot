using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WpfPoint = System.Windows.Point;

namespace NEXTShot.Windows;

public partial class RegionCaptureWindow
{
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(HitTestHook);
    }

    private IntPtr HitTestHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest || !_editorVideoRecording)
            return IntPtr.Zero;

        var packed = lParam.ToInt64();
        var sx = (short)(packed & 0xFFFF);
        var sy = (short)((packed >> 16) & 0xFFFF);
        var screen = new Point(sx, sy);
        WpfPoint p;
        try
        {
            p = OverlayCanvas.PointFromScreen(screen);
        }
        catch
        {
            return IntPtr.Zero;
        }

        if (!IsPointInSelection(p)) return IntPtr.Zero;
        if (IsToolbarScreenPoint(screen)) return IntPtr.Zero;

        handled = true;
        return (IntPtr)HtTransparent;
    }

    private bool IsPointInSelection(WpfPoint p) =>
        p.X >= _selX && p.X <= _selX + _selW && p.Y >= _selY && p.Y <= _selY + _selH;

    private bool IsToolbarScreenPoint(Point screen)
    {
        return IsUnderElement(RightToolbar, screen) || IsUnderElement(BottomToolbar, screen) || IsUnderElement(SizeBadge, screen);
    }

    private static bool IsUnderElement(FrameworkElement el, Point screen)
    {
        if (el.Visibility != Visibility.Visible) return false;
        try
        {
            var topLeft = el.PointToScreen(new Point(0, 0));
            var bottomRight = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
            return screen.X >= topLeft.X && screen.X <= bottomRight.X
                   && screen.Y >= topLeft.Y && screen.Y <= bottomRight.Y;
        }
        catch
        {
            return false;
        }
    }
}
