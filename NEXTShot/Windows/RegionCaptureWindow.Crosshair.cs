using System.Drawing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using NEXTShot.Helpers;
using WpfPoint = System.Windows.Point;
using DrawingBitmap = System.Drawing.Bitmap;
using WpfColor = System.Windows.Media.Color;

namespace NEXTShot.Windows;

public partial class RegionCaptureWindow
{
    private const double CrosshairArm = 8;
    private const double CrosshairThickness = 2;
    private const double CrosshairSegmentStep = 2;

    private void UpdateCrosshairOverlay(WpfPoint center)
    {
        if (_editing)
        {
            HideCrosshairOverlay();
            return;
        }

        CrosshairCanvas.Visibility = Visibility.Visible;
        CrosshairCanvas.Children.Clear();

        var up = new WpfPoint(center.X, center.Y - CrosshairArm);
        var down = new WpfPoint(center.X, center.Y + CrosshairArm);
        var left = new WpfPoint(center.X - CrosshairArm, center.Y);
        var right = new WpfPoint(center.X + CrosshairArm, center.Y);

        DrawContrastLine(center, up);
        DrawContrastLine(center, down);
        DrawContrastLine(center, left);
        DrawContrastLine(center, right);
    }

    private void HideCrosshairOverlay()
    {
        CrosshairCanvas.Children.Clear();
        CrosshairCanvas.Visibility = Visibility.Collapsed;
    }

    private void DrawContrastLine(WpfPoint a, WpfPoint b)
    {
        var delta = b - a;
        var total = delta.Length;
        if (total < 0.5) return;

        var dir = delta;
        dir.Normalize();
        var segments = Math.Max(1, (int)Math.Ceiling(total / CrosshairSegmentStep));

        for (var i = 0; i < segments; i++)
        {
            var t0 = i * CrosshairSegmentStep;
            var t1 = Math.Min((i + 1) * CrosshairSegmentStep, total);
            var p0 = a + dir * t0;
            var p1 = a + dir * t1;
            var mid = a + dir * ((t0 + t1) * 0.5);
            var stroke = SampleContrastStroke(mid);

            CrosshairCanvas.Children.Add(new Line
            {
                X1 = p0.X,
                Y1 = p0.Y,
                X2 = p1.X,
                Y2 = p1.Y,
                Stroke = new SolidColorBrush(stroke),
                StrokeThickness = CrosshairThickness,
                StrokeStartLineCap = PenLineCap.Square,
                StrokeEndLineCap = PenLineCap.Square,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            });
        }
    }

    private WpfColor SampleContrastStroke(WpfPoint overlayPoint)
    {
        if (TrySampleDesktop(overlayPoint, out var bg))
            return BrushContrast.PickContrastStroke(bg);
        return Colors.White;
    }

    private bool TrySampleDesktop(WpfPoint overlayPoint, out WpfColor color)
    {
        color = default;
        if (_desktopSnapshot is not DrawingBitmap bmp) return false;

        var dpi = VisualTreeHelper.GetDpi(this);
        var px = (int)Math.Clamp(Math.Round(overlayPoint.X * dpi.DpiScaleX), 0, bmp.Width - 1);
        var py = (int)Math.Clamp(Math.Round(overlayPoint.Y * dpi.DpiScaleY), 0, bmp.Height - 1);
        var c = bmp.GetPixel(px, py);
        color = WpfColor.FromArgb(c.A, c.R, c.G, c.B);
        return true;
    }
}