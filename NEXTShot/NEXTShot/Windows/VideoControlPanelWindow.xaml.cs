using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NEXTShot.Native;
using WpfPoint = System.Windows.Point;

namespace NEXTShot.Windows;

public partial class VideoControlPanelWindow : Window
{
    private bool _recordingStarted;
    private WpfPoint _dragStart;
    private bool _dragging;

    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action? Cancelled;

    public VideoControlPanelWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        MouseLeftButtonDown += OnDragMouseDown;
        MouseLeftButtonUp += OnDragMouseUp;
        MouseMove += OnDragMouseMove;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CenterAtScreenBottom();
        ExcludeFromScreenCapture();
    }

    private void CenterAtScreenBottom()
    {
        UpdateLayout();
        var w = ActualWidth > 10 ? ActualWidth : 220;
        var h = ActualHeight > 10 ? ActualHeight : 48;
        var vsLeft = SystemParameters.VirtualScreenLeft;
        var vsTop = SystemParameters.VirtualScreenTop;
        var vsW = SystemParameters.VirtualScreenWidth;
        var vsH = SystemParameters.VirtualScreenHeight;
        Left = vsLeft + (vsW - w) / 2;
        Top = vsTop + vsH - h - 24;
    }

    private void ExcludeFromScreenCapture()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WdaExcludeFromCapture);
    }

    private void OnDragMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Button) return;
        _dragging = true;
        _dragStart = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnDragMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        Left += p.X - _dragStart.X;
        Top += p.Y - _dragStart.Y;
    }

    private void OnDragMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
    }

    public void SetRecording(bool recording)
    {
        if (recording) _recordingStarted = true;
        BtnStart.IsEnabled = !recording;
        BtnStop.IsEnabled = recording;
        RecDot.Fill = new SolidColorBrush(recording ? Color.FromRgb(0xE7, 0x4C, 0x3C) : Color.FromRgb(0x55, 0x55, 0x55));
    }

    private void Start_OnClick(object sender, RoutedEventArgs e) => StartRequested?.Invoke();

    private void Stop_OnClick(object sender, RoutedEventArgs e) => StopRequested?.Invoke();

    protected override void OnClosed(EventArgs e)
    {
        if (!_recordingStarted)
            Cancelled?.Invoke();
        base.OnClosed(e);
    }
}
