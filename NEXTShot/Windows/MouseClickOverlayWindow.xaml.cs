using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NEXTShot.Native;

namespace NEXTShot.Windows;

public enum MouseClickButton { Left, Right, Middle }

public partial class MouseClickOverlayWindow : Window
{
    private static readonly SolidColorBrush LeftBrush = new(Color.FromRgb(0x2E, 0xA3, 0xFF));
    private static readonly SolidColorBrush RightBrush = new(Color.FromRgb(0xE7, 0x4C, 0x3C));
    private static readonly SolidColorBrush MiddleBrush = new(Color.FromRgb(0xF3, 0x9C, 0x12));

    static MouseClickOverlayWindow()
    {
        LeftBrush.Freeze();
        RightBrush.Freeze();
        MiddleBrush.Freeze();
    }

    public MouseClickOverlayWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExstyle);
        ex |= NativeMethods.WsExTransparent | NativeMethods.WsExLayered | NativeMethods.WsExToolwindow | NativeMethods.WsExNoactivate;
        _ = NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExstyle, ex);
    }

    public void ShowForVirtualScreen()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        EffectsCanvas.Width = Width;
        EffectsCanvas.Height = Height;
        if (!IsVisible) Show();
        UpdateLayout();
    }

    public void PlayClick(MouseClickButton button, int screenX, int screenY)
    {
        Point local;
        try
        {
            local = EffectsCanvas.PointFromScreen(new Point(screenX, screenY));
        }
        catch
        {
            return;
        }

        var x = local.X;
        var y = local.Y;
        if (x < 0 || y < 0 || x > EffectsCanvas.Width || y > EffectsCanvas.Height) return;

        var (brush, label) = button switch
        {
            MouseClickButton.Left => (LeftBrush, "L"),
            MouseClickButton.Right => (RightBrush, "R"),
            _ => (MiddleBrush, "M")
        };

        const double baseSize = 28;
        var host = new Canvas
        {
            Width = baseSize * 3,
            Height = baseSize * 3,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(host, x - baseSize * 1.5);
        Canvas.SetTop(host, y - baseSize * 1.5);

        var ring = new System.Windows.Shapes.Ellipse
        {
            Width = baseSize,
            Height = baseSize,
            Stroke = brush,
            StrokeThickness = 3,
            Fill = Brushes.Transparent,
            RenderTransformOrigin = new Point(0.5, 0.5),
            IsHitTestVisible = false
        };
        ring.RenderTransform = new ScaleTransform(1, 1);
        Canvas.SetLeft(ring, baseSize);
        Canvas.SetTop(ring, baseSize);

        var badge = new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(10),
            Width = 22,
            Height = 22,
            Child = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            },
            Opacity = 0.95,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(badge, baseSize + baseSize / 2 - 11);
        Canvas.SetTop(badge, baseSize + baseSize / 2 - 11);

        host.Children.Add(ring);
        host.Children.Add(badge);
        EffectsCanvas.Children.Add(host);

        var duration = TimeSpan.FromMilliseconds(600);
        var scaleAnimX = new DoubleAnimation(1, 2.8, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var scaleAnimY = new DoubleAnimation(1, 2.8, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var fadeAnim = new DoubleAnimation(1, 0, duration);
        Storyboard.SetTarget(scaleAnimX, ring);
        Storyboard.SetTargetProperty(scaleAnimX, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        Storyboard.SetTarget(scaleAnimY, ring);
        Storyboard.SetTargetProperty(scaleAnimY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        Storyboard.SetTarget(fadeAnim, host);
        Storyboard.SetTargetProperty(fadeAnim, new PropertyPath("Opacity"));

        var sb = new Storyboard();
        sb.Children.Add(scaleAnimX);
        sb.Children.Add(scaleAnimY);
        sb.Children.Add(fadeAnim);
        sb.Completed += (_, _) => EffectsCanvas.Children.Remove(host);
        sb.Begin();
    }
}